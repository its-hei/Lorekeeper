using System;
using System.Threading.Tasks;

namespace Lorekeeper;

public sealed class TranslationRouter : ITranslator
{
    private readonly Configuration configuration;
    private readonly Translator openAiTranslator;
    private readonly LibreTranslateTranslator libreTranslator;
    private readonly LorekeeperCloudClient cloudClient;
    private readonly LocalProperNounStore localProperNounStore;
    private readonly ILorekeeperLogger logger;

    public TranslationRouter(
        Configuration configuration,
        Translator openAiTranslator,
        LibreTranslateTranslator libreTranslator,
        LorekeeperCloudClient cloudClient,
        LocalProperNounStore localProperNounStore,
        ILorekeeperLogger logger)
    {
        this.configuration = configuration
            ?? throw new ArgumentNullException(nameof(configuration));

        this.openAiTranslator = openAiTranslator
            ?? throw new ArgumentNullException(nameof(openAiTranslator));

        this.libreTranslator = libreTranslator
            ?? throw new ArgumentNullException(nameof(libreTranslator));

        this.cloudClient = cloudClient
            ?? throw new ArgumentNullException(nameof(cloudClient));

        this.localProperNounStore = localProperNounStore
            ?? throw new ArgumentNullException(nameof(localProperNounStore));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string npcName,
        TranslationContext context)
    {
        context ??= TranslationContext.Default;

        bool usesPrivateProperName =
            localProperNounStore.HasPrivateMatch(text);

        bool containsSpeakerName =
            ContainsSpeakerName(
                text,
                npcName);

        bool requiresCurrentPolicyRefresh =
            containsSpeakerName
            || localProperNounStore.HasBuiltInMatch(text)
            || ContainsPolicySensitiveText(text);

        // Jeśli cała kwestia jest po prostu imieniem mówcy (np. "Estinien."),
        // nie uruchamiamy translatora. To nazwa własna i musi zostać 1:1.
        if (IsSpeakerNameOnly(
                text,
                npcName))
        {
            string canonicalText =
                text.Trim();

            TranslationResult canonicalResult =
                openAiTranslator.StoreCanonicalTranslation(
                    text,
                    npcName,
                    context,
                    canonicalText);

            if (configuration.CloudEnabled
                && !usesPrivateProperName)
            {
                _ = cloudClient.SubmitOpenAiAsync(
                    text,
                    npcName,
                    context,
                    canonicalText,
                    configuration.OpenAiModel);
            }

            logger.Information(
                "ROUTER: Kwestia jest imieniem mówcy - zachowuję nazwę 1:1.");

            return canonicalResult;
        }

        // 1. Najwyższy priorytet: istniejący lokalny cache OpenAI.
        if (openAiTranslator.TryGetCachedTranslation(
                text,
                npcName,
                context,
                out TranslationResult cachedOpenAiResult))
        {
            logger.Information(
                "ROUTER: Lokalny cache OpenAI.");

            // Cache OpenAI z bieżącej wersji jest źródłem kanonicznym.
            // Synchronizujemy go również przy cache HIT, dzięki czemu poprawione
            // tłumaczenie z nowszej wersji klienta może zaktualizować Cloud.
            if (configuration.CloudEnabled
                && !usesPrivateProperName)
            {
                _ = cloudClient.SubmitOpenAiAsync(
                    text,
                    npcName,
                    context,
                    cachedOpenAiResult.TranslatedText,
                    configuration.OpenAiModel);
            }

            return cachedOpenAiResult;
        }

        // 2. Wspólna biblioteka zawiera WYŁĄCZNIE tłumaczenia OpenAI.
        // Cloud można całkowicie wyłączyć w ustawieniach użytkownika.
        if (configuration.CloudEnabled
            && !usesPrivateProperName)
        {
            CloudTranslationHit? cloudHit =
                await cloudClient.TryGetOpenAiAsync(
                    text,
                    npcName,
                    context);

            if (cloudHit is not null)
            {
                bool cloudIsFreshEnough =
                    !requiresCurrentPolicyRefresh
                    || cloudClient.IsFromCurrentOrNewerClient(
                        cloudHit.ClientVersion);

                if (cloudIsFreshEnough)
                {
                    logger.Information(
                        "ROUTER: Lorekeeper Cloud HIT OpenAI.");

                    return openAiTranslator.StoreCloudTranslation(
                        text,
                        npcName,
                        context,
                        cloudHit.TranslatedText);
                }

                logger.Information(
                    "ROUTER: Cloud HIT pochodzi ze starszej polityki tłumaczeń - odświeżam lokalnie i zaktualizuję Cloud.");
            }
        }
        else if (usesPrivateProperName)
        {
            logger.Information(
                "ROUTER: Prywatna nazwa własna - pomijam Lorekeeper Cloud.");
        }
        else
        {
            logger.Information(
                "ROUTER: Lorekeeper Cloud wyłączony.");
        }

        bool useLibre =
            configuration.SelectedTranslationProvider
            == TranslationProvider.LibreTranslate;

        // 3. Libre pozostaje całkowicie lokalny.
        if (useLibre)
        {
            if (libreTranslator.TryGetCachedTranslation(
                    text,
                    npcName,
                    context,
                    out TranslationResult cachedLibreResult))
            {
                logger.Information(
                    "ROUTER: Lokalny cache Libre.");

                return cachedLibreResult;
            }

            logger.Information(
                configuration.CloudEnabled
                && !usesPrivateProperName
                    ? "ROUTER: Cloud OpenAI MISS. Uruchamiam lokalny LibreTranslate."
                    : "ROUTER: Uruchamiam lokalny LibreTranslate.");

            return await libreTranslator.TranslateAsync(
                text,
                npcName,
                context);
        }

        // 4. OpenAI tworzy nowe tłumaczenie, a po sukcesie
        // wynik jest synchronizowany z Cloud w tle.
        logger.Information(
            configuration.CloudEnabled
            && !usesPrivateProperName
                ? "ROUTER: Cloud OpenAI MISS. Uruchamiam OpenAI."
                : "ROUTER: Uruchamiam OpenAI.");

        TranslationResult openAiResult =
            await openAiTranslator.TranslateAsync(
                text,
                npcName,
                context);

        if (configuration.CloudEnabled
            && !usesPrivateProperName
            && openAiTranslator.TryGetCachedText(
                text,
                npcName,
                context,
                out string cachedOpenAiAfterTranslation))
        {
            _ = cloudClient.SubmitOpenAiAsync(
                text,
                npcName,
                context,
                cachedOpenAiAfterTranslation,
                configuration.OpenAiModel);
        }

        return openAiResult;
    }
    private static bool ContainsSpeakerName(
        string text,
        string npcName)
    {
        string name =
            (npcName ?? string.Empty).Trim();

        return !string.IsNullOrWhiteSpace(name)
               && (text ?? string.Empty).IndexOf(
                   name,
                   StringComparison.Ordinal) >= 0;
    }

    private static bool IsSpeakerNameOnly(
        string text,
        string npcName)
    {
        string name =
            (npcName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string candidate =
            (text ?? string.Empty)
            .Trim()
            .TrimEnd('.', '!', '?', '…');

        return string.Equals(
            candidate,
            name,
            StringComparison.Ordinal);
    }

    private static bool ContainsPolicySensitiveText(
        string text)
    {
        string value =
            text ?? string.Empty;

        return value.IndexOf(
                   "magick",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "bearings",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "realmship",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "water brand",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "Descending",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "sound plan",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "behoove",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "excitement",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || (value.IndexOf(
                       "keep",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   && value.IndexOf(
                       "company",
                       StringComparison.OrdinalIgnoreCase) >= 0)
               || value.IndexOf(
                   "care for conversation",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "peddlin",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "fit right in",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "fresh off the carriage",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "cerebral",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "my business",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || value.IndexOf(
                   "beastmen",
                   StringComparison.OrdinalIgnoreCase) >= 0
               || (value.IndexOf(
                       "stub",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   && value.IndexOf(
                       "toe",
                       StringComparison.OrdinalIgnoreCase) >= 0);
    }

}
