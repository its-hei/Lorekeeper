using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Lorekeeper;

public sealed class Translator : ITranslator
{
    private const decimal Gpt4oMiniInputPricePerMillionTokens = 0.15m;
    private const decimal Gpt4oMiniCachedInputPricePerMillionTokens = 0.075m;
    private const decimal Gpt4oMiniOutputPricePerMillionTokens = 0.60m;

    private const decimal Gpt56LunaInputPricePerMillionTokens = 0.20m;
    private const decimal Gpt56LunaCachedInputPricePerMillionTokens = 0.02m;
    private const decimal Gpt56LunaOutputPricePerMillionTokens = 1.20m;

    private const decimal Gpt41MiniInputPricePerMillionTokens = 0.40m;
    private const decimal Gpt41MiniCachedInputPricePerMillionTokens = 0.10m;
    private const decimal Gpt41MiniOutputPricePerMillionTokens = 1.60m;
    private const string CacheKeyVersion = "5";

    private const string SystemPrompt =
        "Jesteś profesjonalnym tłumaczem dialogów z gry Final Fantasy XIV " +
        "z języka angielskiego na język polski. " +
        "Najważniejsza zasada: najpierw WIERNOŚĆ ZNACZENIU, potem naturalność, " +
        "a dopiero na końcu styl i charakter postaci. " +
        "Zachowuj jawne znaczenie każdej kwestii. Nie zastępuj słów i pojęć " +
        "pojęciami pokrewnymi, interpretacją sytuacji ani domyślnym sensem. " +
        "Nie streszczaj, nie parafrazuj, nie upiększaj i nie dopisuj emocji, " +
        "których nie ma w tekście źródłowym. " +
        "Naturalizuj składnię tylko wtedy, gdy jest to potrzebne, aby zdanie " +
        "brzmiało poprawnie po polsku, ale nigdy kosztem zmiany znaczenia. " +
        "Jeśli istnieje kilka naturalnych polskich wersji, wybierz tę, która " +
        "jest znaczeniowo i strukturalnie najbliższa angielskiemu oryginałowi. " +
        "Zachowuj kolejność myśli, pytania, wykrzyknienia, zawahania, powtórzenia, " +
        "żarty, zwroty retoryczne, formy grzecznościowe i bezpośrednie zwroty " +
        "do rozmówcy. Nie pomijaj określeń takich jak 'my', 'dear', 'good', " +
        "tytułów ani innych elementów zwrotu do adresata, jeśli występują w źródle. " +
        "Przykład 1: 'Are you getting sleepy? Because I sure am! " +
        "Let's call it a day, shall we?' -> " +
        "'Czujesz się śpiący? Bo ja na pewno! Może zakończmy na dzisiaj, co?'. " +
        "Nie tłumacz 'sleepy' jako 'nudzić się', ponieważ zmienia to znaczenie. " +
        "Przykład 2: 'Enjoy yourself, my good detective.' -> " +
        "'Ciesz się, mój dobry detektywie.'. " +
        "Nie pomijaj zwrotu 'my good detective' i nie zastępuj go luźniejszą " +
        "parafrazą typu 'baw się dobrze, detektywie'. " +
        "Przykład 3: gdy kontekst fabularny mówi, że 'my faithful assistant' " +
        "Hildibranda oznacza Nashu Mhakaracca, tłumacz tę referencję w rodzaju " +
        "żeńskim, np. 'z moją wierną asystentką', a nie 'z moim wiernym asystentem'. " +
        "Kontekst rozmowy służy wyłącznie do rozwiązywania niejednoznaczności, " +
        "ustalania referencji, płci, tonu i ciągłości rozmowy. " +
        "Jeżeli przekazano osobny kontekst fabularny dotyczący osoby trzeciej, " +
        "użyj go do poprawnego rozpoznania, do kogo odnosi się mówca, oraz do " +
        "doboru właściwego rodzaju gramatycznego tej osoby. " +
        "Kontekst NIGDY nie może nadpisywać jawnego znaczenia aktualnie " +
        "tłumaczonej kwestii. " +
        "Płeć postaci wykorzystuj do poprawnej odmiany czasowników, zaimków, " +
        "przymiotników i innych form gramatycznych, ale nie twórz sztucznych " +
        "ani nienaturalnych feminatywów lub maskulinatywów tylko dlatego, " +
        "że znasz płeć postaci. Jeśli naturalne polskie użycie albo ustalona " +
        "terminologia brzmi lepiej bez mechanicznego zaznaczania rodzaju, " +
        "zachowaj tę formę. " +
        "Zachowuj ton wypowiedzi, emocje oraz klimat fantasy, o ile nie wymaga " +
        "to zmiany literalnego sensu kwestii. " +
        "Nie tłumacz nazw postaci, lokacji, organizacji, przedmiotów, " +
        "jobów, klas, umiejętności, dungeonów, triali i raidów. " +
        "Informacja o płci postaci gracza dotyczy wyłącznie postaci gracza " +
        "jako możliwego adresata wypowiedzi. Nie przenoś jej na NPC mówiącego. " +
        "Nazwa NPC jest wyłącznie identyfikatorem mówcy. Nie umieszczaj nazwy NPC " +
        "na początku tłumaczenia ani nie zwracaj etykiety mówcy w formie " +
        "'NPC:', ponieważ interfejs Lorekeepera wyświetla nazwę mówcy osobno. " +
        "Przykład: jeśli mówcą jest Monom, odpowiedź ma zaczynać się bezpośrednio " +
        "od treści tłumaczenia, nigdy od 'Monom:'. " +
        "Nie wnioskuj płci NPC z imienia, przydomka ani brzmienia nazwy. " +
        "Jeśli płeć mówcy nie wynika " +
        "jednoznacznie z przekazanego kontekstu lub treści dialogu, stosuj " +
        "naturalne konstrukcje neutralne płciowo i nie zgaduj rodzaju. " +
        "Nie dodawaj objaśnień, komentarzy, etykiet ani cudzysłowów. " +
        "Używaj wyłącznie zwykłego myślnika '-' zamiast półpauzy i pauzy. " +
        "Wtrącenia zapisuj dokładnie jako ' - ' ze spacją po obu stronach. " +
        "Zwracaj wyłącznie gotowe polskie tłumaczenie.";

    private readonly TranslationCache cache;
    private readonly ILorekeeperLogger logger;
    private readonly TerminologyStore? terminologyStore;
    private readonly LocalProperNounStore? localProperNounStore;
    private readonly ConversationMemory? conversationMemory;
    private readonly string cacheNamespace;
    private readonly string model;
    private readonly Action<decimal>? usageRecorder;
    private readonly ChatClient? chatClient;

    public Translator(
        TranslationCache cache,
        OpenAiTranslatorOptions options,
        ILorekeeperLogger logger)
        : this(
            cache,
            options,
            logger,
            null,
            null,
            null)
    {
    }

    public Translator(
        TranslationCache cache,
        OpenAiTranslatorOptions options,
        ILorekeeperLogger logger,
        TerminologyStore? terminologyStore)
        : this(
            cache,
            options,
            logger,
            terminologyStore,
            null,
            null)
    {
    }

    public Translator(
        TranslationCache cache,
        OpenAiTranslatorOptions options,
        ILorekeeperLogger logger,
        TerminologyStore? terminologyStore,
        LocalProperNounStore? localProperNounStore,
        ConversationMemory? conversationMemory,
        Action<decimal>? usageRecorder = null)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.terminologyStore = terminologyStore;
        this.localProperNounStore = localProperNounStore;
        this.conversationMemory = conversationMemory;
        this.usageRecorder = usageRecorder;
        model = options.Model;
        cacheNamespace = $"{CacheKeyVersion}\u001F{options.Model}";

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            logger.Warning(
                "OPENAI: Nie ustawiono klucza API. Klient OpenAI nie został utworzony.");

            return;
        }

        chatClient = new ChatClient(
            model: options.Model,
            apiKey: options.ApiKey);

        logger.Information(
            $"OPENAI: Klient został utworzony. Model: {options.Model}");
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string npcName,
        TranslationContext context)
    {
        context ??= TranslationContext.Default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return CreateResult(text, string.Empty);
        }

        if (TryGetCachedTranslation(
                text,
                npcName,
                context,
                out TranslationResult cachedResult))
        {
            return cachedResult;
        }

        if (chatClient is null)
        {
            logger.Warning(
                "OPENAI: Brak aktywnego klienta OpenAI.");

            return CreateResult(
                text,
                "Brak klucza API OpenAI. Wpisz klucz w ustawieniach i przeładuj plugin.");
        }

        return await TranslateWithOpenAiAsync(
            text,
            npcName,
            context);
    }

    public bool TryGetCachedTranslation(
        string text,
        string npcName,
        TranslationContext context,
        out TranslationResult result)
    {
        context ??= TranslationContext.Default;

        if (string.IsNullOrWhiteSpace(text))
        {
            result = CreateResult(text, string.Empty);
            return true;
        }

        string cacheKey = CreateCacheKey(
            text,
            npcName,
            context);

        if (!cache.TryGet(
                cacheKey,
                out string cachedTranslation))
        {
            result = null!;
            return false;
        }

        logger.Information(
            "OPENAI: Tłumaczenie znalezione w lokalnej bazie.");

        string sanitizedTranslation =
            TranslationTextNormalizer.RemoveSpeakerPrefix(
                cachedTranslation,
                npcName);

        if (!string.Equals(
                cachedTranslation,
                sanitizedTranslation,
                StringComparison.Ordinal))
        {
            TrySaveToCache(
                cacheKey,
                sanitizedTranslation);

            logger.Information(
                "OPENAI CACHE: Usunięto zbędny prefiks nazwy mówcy.");
        }

        conversationMemory?.Add(
            npcName,
            text,
            sanitizedTranslation);

        result = CreateResult(
            text,
            sanitizedTranslation,
            fromCache: true);

        return true;
    }

    public bool TryGetCachedText(
        string text,
        string npcName,
        TranslationContext context,
        out string translatedText)
    {
        context ??= TranslationContext.Default;

        if (string.IsNullOrWhiteSpace(text))
        {
            translatedText = string.Empty;
            return true;
        }

        string cacheKey =
            CreateCacheKey(
                text,
                npcName,
                context);

        if (!cache.TryGet(
                cacheKey,
                out translatedText!))
        {
            return false;
        }

        string sanitizedTranslation =
            TranslationTextNormalizer.RemoveSpeakerPrefix(
                translatedText,
                npcName);

        if (!string.Equals(
                translatedText,
                sanitizedTranslation,
                StringComparison.Ordinal))
        {
            translatedText =
                sanitizedTranslation;

            TrySaveToCache(
                cacheKey,
                translatedText);
        }

        return true;
    }

    public TranslationResult StoreCloudTranslation(
        string text,
        string npcName,
        TranslationContext context,
        string translatedText)
    {
        context ??= TranslationContext.Default;

        string cacheKey =
            CreateCacheKey(
                text,
                npcName,
                context);

        string sanitizedTranslation =
            TranslationTextNormalizer.RemoveSpeakerPrefix(
                translatedText,
                npcName);

        TrySaveToCache(
            cacheKey,
            sanitizedTranslation);

        conversationMemory?.Add(
            npcName,
            text,
            sanitizedTranslation);

        logger.Information(
            "OPENAI CACHE: Zapisano tłumaczenie pobrane z Lorekeeper Cloud.");

        return CreateResult(
            text,
            sanitizedTranslation,
            fromCache: true);
    }

    private async Task<TranslationResult> TranslateWithOpenAiAsync(
        string text,
        string npcName,
        TranslationContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            logger.Information(
                "OPENAI: Wysyłanie zapytania...");

            ChatCompletion completion =
                await chatClient!.CompleteChatAsync(
                    CreateMessages(text, npcName, context));

            stopwatch.Stop();

            logger.Information(
                $"OPENAI: Odpowiedź odebrana po {stopwatch.ElapsedMilliseconds} ms.");

            string translatedText =
                TranslationTextNormalizer.RemoveSpeakerPrefix(
                    GetTranslatedText(completion),
                    npcName);

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                logger.Warning(
                    "OPENAI: Otrzymano pustą odpowiedź.");

                return CreateResult(
                    text,
                    "OpenAI zwróciło pustą odpowiedź.");
            }

            int inputTokens =
                completion.Usage?.InputTokenCount
                ?? 0;

            int cachedInputTokens =
                completion.Usage?.InputTokenDetails?.CachedTokenCount
                ?? 0;

            int outputTokens =
                completion.Usage?.OutputTokenCount
                ?? 0;

            decimal costUsd =
                CalculateCost(
                    model,
                    inputTokens,
                    cachedInputTokens,
                    outputTokens);

            LogUsage(
                inputTokens,
                outputTokens,
                costUsd);

            usageRecorder?.Invoke(
                costUsd);

            string cacheKey = CreateCacheKey(text, npcName, context);
            TrySaveToCache(cacheKey, translatedText);

            conversationMemory?.Add(
                npcName,
                text,
                translatedText);

            return CreateResult(
                text,
                translatedText,
                inputTokens: inputTokens,
                outputTokens: outputTokens,
                costUsd: costUsd);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            logger.Error(
                exception,
                $"OPENAI: Zapytanie nie powiodło się po " +
                $"{stopwatch.ElapsedMilliseconds} ms.");

            return CreateResult(
                text,
                $"Błąd OpenAI: {exception.Message}");
        }
    }

    private List<ChatMessage> CreateMessages(
        string text,
        string npcName,
        TranslationContext context)
    {
        string playerContext = context.PlayerCharacterSex switch
        {
            PlayerSex.Female =>
                "Adresatem może być postać gracza będąca kobietą. " +
                "Formy kierowane bezpośrednio do postaci gracza tłumacz " +
                "w rodzaju żeńskim.",
            PlayerSex.Male =>
                "Adresatem może być postać gracza będąca mężczyzną. " +
                "Formy kierowane bezpośrednio do postaci gracza tłumacz " +
                "w rodzaju męskim.",
            _ =>
                "Płeć postaci gracza jako możliwego adresata jest nieznana. " +
                "Nie zakładaj jej bez jednoznacznych podstaw."
        };

        string speakerContext = context.SpeakerSex switch
        {
            PlayerSex.Female =>
                "NPC będący mówcą jest kobietą. Wszystkie formy odnoszące " +
                "się do mówcy stosuj w rodzaju żeńskim. Ta informacja " +
                "pochodzi z danych gry i ma pierwszeństwo przed domysłami " +
                "wynikającymi z imienia lub treści.",
            PlayerSex.Male =>
                "NPC będący mówcą jest mężczyzną. Wszystkie formy odnoszące " +
                "się do mówcy stosuj w rodzaju męskim. Ta informacja " +
                "pochodzi z danych gry i ma pierwszeństwo przed domysłami " +
                "wynikającymi z imienia lub treści.",
            _ =>
                "Płeć NPC będącego mówcą jest nieznana. Nazwa NPC nie określa " +
                "jego płci. Nie zgaduj rodzaju mówcy i nie przypisuj mu płci " +
                "postaci gracza."
        };

        string terminologyContext =
            BuildTerminologyContext(text, context);

        string localProperNounContext =
            BuildLocalProperNounContext(text);

        string conversationContext =
            BuildConversationContext();

        LoreReferenceInfo loreReference =
            LoreReferenceContext.Resolve(
                npcName,
                text);

        string loreContext =
            loreReference.HasContext
                ? loreReference.PromptContext
                : string.Empty;

        return
        [
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(
                $"Kontekst adresata: {playerContext}\n" +
                $"Kontekst mówcy: {speakerContext}\n" +
                terminologyContext +
                localProperNounContext +
                conversationContext +
                loreContext +
                $"NPC/mówca: {npcName}\n" +
                "Przetłumacz poniższą kwestię wiernie. Kontekst może pomóc " +
                "rozwiązać niejednoznaczność, ale nie może zmieniać jawnego " +
                "znaczenia tekstu źródłowego.\n" +
                $"Dialog do przetłumaczenia:\n{text}")
        ];
    }

    private string BuildConversationContext()
    {
        if (conversationMemory is null)
        {
            return string.Empty;
        }

        IReadOnlyList<ConversationLine> recent =
            conversationMemory.GetRecentForPrompt(5);

        if (recent.Count == 0)
        {
            return string.Empty;
        }

        List<string> lines = new();

        foreach (ConversationLine line in recent)
        {
            if (string.IsNullOrWhiteSpace(line.TranslatedText))
            {
                continue;
            }

            lines.Add(
                $"- {line.Speaker}: {line.TranslatedText}");
        }

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return
            "Poprzednie kwestie tej rozmowy (kontekst, nie tłumacz ich ponownie):\n" +
            string.Join("\n", lines) +
            "\n";
    }

    private string BuildTerminologyContext(
        string text,
        TranslationContext context)
    {
        if (terminologyStore is null)
        {
            return string.Empty;
        }

        List<string> rules = new();

        foreach (TerminologyEntry entry in terminologyStore.GetAll())
        {
            if (text.IndexOf(
                    entry.SourceTerm,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (entry.InflectForGender
                && !string.IsNullOrWhiteSpace(entry.FeminineForm))
            {
                rules.Add(
                    $"- '{entry.SourceTerm}' oznacza " +
                    $"'{entry.PreferredTranslation}'. " +
                    $"Jeśli termin odnosi się do kobiety, użyj " +
                    $"'{entry.FeminineForm}'. " +
                    $"Nie używaj innych znaczeń tego terminu.");
            }
            else
            {
                rules.Add(
                    $"- '{entry.SourceTerm}' tłumacz konsekwentnie jako " +
                    $"'{entry.PreferredTranslation}'.");
            }
        }

        if (rules.Count == 0)
        {
            return string.Empty;
        }

        return
            "Obowiązująca terminologia dla tej kwestii:\n" +
            string.Join("\n", rules) +
            "\n";
    }

    private string BuildLocalProperNounContext(
        string text)
    {
        if (localProperNounStore is null)
        {
            return string.Empty;
        }

        IReadOnlyList<string> matches =
            localProperNounStore.GetMatches(text);

        if (matches.Count == 0)
        {
            return string.Empty;
        }

        List<string> rules = new();

        foreach (string name in matches)
        {
            rules.Add(
                $"- '{name}' zachowaj dokładnie w oryginalnej formie. " +
                "Nie tłumacz tej nazwy i nie zmieniaj jej pisowni.");
        }

        return
            "Prywatne lokalne nazwy własne dla tej kwestii:\n" +
            string.Join("\n", rules) +
            "\n";
    }

    private void TrySaveToCache(
        string cacheKey,
        string translatedText)
    {
        try
        {
            cache.Add(cacheKey, translatedText);

            logger.Information(
                "OPENAI: Tłumaczenie zapisane w lokalnej bazie.");
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "CACHE: Nie udało się zapisać tłumaczenia. " +
                "Gotowe tłumaczenie zostanie mimo to wyświetlone.");
        }
    }

    private string CreateCacheKey(
        string text,
        string npcName,
        TranslationContext context)
    {
        string terminologyKey =
            CreateTerminologyCacheKey(text);

        string localProperNounKey =
            CreateLocalProperNounCacheKey(text);

        LoreReferenceInfo loreReference =
            LoreReferenceContext.Resolve(
                npcName,
                text);

        string baseKey =
            $"{cacheNamespace}\u001F" +
            $"{context.PlayerCharacterSex}\u001F" +
            $"{context.SpeakerSex}\u001F" +
            $"{terminologyKey}\u001F" +
            $"{npcName}\u001F{text}";

        if (!string.IsNullOrWhiteSpace(
                localProperNounKey))
        {
            baseKey +=
                $"\u001FLOCAL_NAMES:{localProperNounKey}";
        }

        return loreReference.HasContext
            ? $"{baseKey}\u001FLORE:{loreReference.Fingerprint}"
            : baseKey;
    }

    private string CreateTerminologyCacheKey(string text)
    {
        if (terminologyStore is null)
        {
            return "NO_TERMINOLOGY";
        }

        List<string> parts = new();

        foreach (TerminologyEntry entry in terminologyStore.GetAll())
        {
            if (text.IndexOf(
                    entry.SourceTerm,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            parts.Add(
                $"{entry.SourceTerm}={entry.PreferredTranslation}|" +
                $"{entry.FeminineForm}|" +
                $"{entry.InflectForGender}");
        }

        return parts.Count == 0
            ? "NO_MATCH"
            : string.Join(";", parts);
    }

    private string CreateLocalProperNounCacheKey(
        string text)
    {
        return localProperNounStore?.GetCacheFingerprint(text)
            ?? string.Empty;
    }

    private static string GetTranslatedText(ChatCompletion completion)
    {
        return completion.Content.Count > 0
            ? completion.Content[0].Text.Trim()
            : string.Empty;
    }

    private static decimal CalculateCost(
        string model,
        int inputTokens,
        int cachedInputTokens,
        int outputTokens)
    {
        (
            decimal inputPrice,
            decimal cachedInputPrice,
            decimal outputPrice) =
                GetPricing(
                    model);

        int safeCachedInputTokens =
            Math.Clamp(
                cachedInputTokens,
                0,
                Math.Max(
                    0,
                    inputTokens));

        int regularInputTokens =
            Math.Max(
                0,
                inputTokens - safeCachedInputTokens);

        decimal regularInputCost =
            regularInputTokens
            / 1_000_000m
            * inputPrice;

        decimal cachedInputCost =
            safeCachedInputTokens
            / 1_000_000m
            * cachedInputPrice;

        decimal outputCost =
            Math.Max(
                0,
                outputTokens)
            / 1_000_000m
            * outputPrice;

        return regularInputCost
               + cachedInputCost
               + outputCost;
    }

    private static (
        decimal Input,
        decimal CachedInput,
        decimal Output) GetPricing(
        string model)
    {
        if (string.Equals(
                model,
                "gpt-5.6-luna",
                StringComparison.OrdinalIgnoreCase))
        {
            return (
                Gpt56LunaInputPricePerMillionTokens,
                Gpt56LunaCachedInputPricePerMillionTokens,
                Gpt56LunaOutputPricePerMillionTokens);
        }

        if (string.Equals(
                model,
                "gpt-4.1-mini",
                StringComparison.OrdinalIgnoreCase))
        {
            return (
                Gpt41MiniInputPricePerMillionTokens,
                Gpt41MiniCachedInputPricePerMillionTokens,
                Gpt41MiniOutputPricePerMillionTokens);
        }

        return (
            Gpt4oMiniInputPricePerMillionTokens,
            Gpt4oMiniCachedInputPricePerMillionTokens,
            Gpt4oMiniOutputPricePerMillionTokens);
    }

    private void LogUsage(
        int inputTokens,
        int outputTokens,
        decimal costUsd)
    {
        logger.Information(
            $"OPENAI: Tokeny wejściowe: {inputTokens}");

        logger.Information(
            $"OPENAI: Tokeny wyjściowe: {outputTokens}");

        logger.Information(
            $"OPENAI: Koszt zapytania: {costUsd:F8} USD");
    }

    private static TranslationResult CreateResult(
        string originalText,
        string translatedText,
        bool fromCache = false,
        int inputTokens = 0,
        int outputTokens = 0,
        decimal costUsd = 0m)
    {
        return new TranslationResult
        {
            OriginalText = originalText,
            TranslatedText = translatedText,
            FromCache = fromCache,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd
        };
    }
}
