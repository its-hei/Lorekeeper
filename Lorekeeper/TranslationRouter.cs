using System;
using System.Threading.Tasks;

namespace Lorekeeper;

public sealed class TranslationRouter : ITranslator
{
    private readonly Configuration configuration;
    private readonly Translator openAiTranslator;
    private readonly LibreTranslateTranslator libreTranslator;
    private readonly LorekeeperCloudClient cloudClient;
    private readonly ILorekeeperLogger logger;

    public TranslationRouter(
        Configuration configuration,
        Translator openAiTranslator,
        LibreTranslateTranslator libreTranslator,
        LorekeeperCloudClient cloudClient,
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

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string npcName,
        TranslationContext context)
    {
        context ??= TranslationContext.Default;

        // 1. Najwyższy priorytet: istniejący lokalny cache OpenAI.
        if (openAiTranslator.TryGetCachedTranslation(
                text,
                npcName,
                context,
                out TranslationResult cachedOpenAiResult))
        {
            logger.Information(
                "ROUTER: Lokalny cache OpenAI.");

            return cachedOpenAiResult;
        }

        // 2. Wspólna biblioteka zawiera WYŁĄCZNIE tłumaczenia OpenAI.
        // Sprawdzamy ją również wtedy, gdy użytkownik wybrał Libre.
        CloudTranslationHit? cloudHit =
            await cloudClient.TryGetOpenAiAsync(
                text,
                npcName,
                context);

        if (cloudHit is not null)
        {
            logger.Information(
                "ROUTER: Lorekeeper Cloud HIT OpenAI.");

            return openAiTranslator.StoreCloudTranslation(
                text,
                npcName,
                context,
                cloudHit.TranslatedText);
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
                "ROUTER: Cloud OpenAI MISS. Uruchamiam lokalny LibreTranslate.");

            return await libreTranslator.TranslateAsync(
                text,
                npcName,
                context);
        }

        // 4. OpenAI tworzy nowe tłumaczenie, a po sukcesie
        // wynik jest synchronizowany z Cloud w tle.
        logger.Information(
            "ROUTER: Cloud OpenAI MISS. Uruchamiam OpenAI.");

        TranslationResult openAiResult =
            await openAiTranslator.TranslateAsync(
                text,
                npcName,
                context);

        if (openAiTranslator.TryGetCachedText(
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
}
