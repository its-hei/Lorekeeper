using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace Lorekeeper;

public sealed class Translator : ITranslator
{
    private const decimal InputPricePerMillionTokens = 0.15m;
    private const decimal OutputPricePerMillionTokens = 0.60m;
    private const string CacheKeyVersion = "4";

    private const string SystemPrompt =
        "Jesteś profesjonalnym tłumaczem dialogów z gry Final Fantasy XIV. " +
        "Tłumacz z języka angielskiego na naturalny, współczesny język polski. " +
        "Płeć postaci wykorzystuj do poprawnej odmiany czasowników, zaimków, " +
        "przymiotników i innych form gramatycznych, ale nie twórz sztucznych " +
        "ani nienaturalnych feminatywów lub maskulinatywów tylko dlatego, " +
        "że znasz płeć postaci. Jeśli naturalne polskie użycie albo ustalona " +
        "terminologia brzmi lepiej bez mechanicznego zaznaczania rodzaju, " +
        "zachowaj tę formę. Naturalność polszczyzny ma pierwszeństwo przed " +
        "mechanicznym zaznaczaniem płci. " +
        "Zachowuj ton wypowiedzi, emocje oraz klimat fantasy. " +
        "Nie tłumacz nazw postaci, lokacji, organizacji, przedmiotów, " +
        "jobów, klas, umiejętności, dungeonów, triali i raidów. " +
        "Informacja o płci postaci gracza dotyczy wyłącznie postaci gracza " +
        "jako możliwego adresata wypowiedzi. Nie przenoś jej na NPC mówiącego. " +
        "Nazwa NPC jest wyłącznie identyfikatorem mówcy. Nie wnioskuj płci NPC " +
        "z imienia, przydomka ani brzmienia nazwy. Jeśli płeć mówcy nie wynika " +
        "jednoznacznie z przekazanego kontekstu lub treści dialogu, stosuj " +
        "naturalne konstrukcje neutralne płciowo i nie zgaduj rodzaju. " +
        "Nie dodawaj objaśnień, komentarzy, etykiet ani cudzysłowów. " +
        "Używaj wyłącznie zwykłego myślnika '-' zamiast półpauzy i pauzy. " +
        "Wtrącenia zapisuj dokładnie jako ' - ' ze spacją po obu stronach. " +
        "Zwracaj wyłącznie gotowe polskie tłumaczenie.";

    private readonly TranslationCache cache;
    private readonly ILorekeeperLogger logger;
    private readonly TerminologyStore? terminologyStore;
    private readonly ConversationMemory? conversationMemory;
    private readonly string cacheNamespace;
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
            null)
    {
    }

    public Translator(
        TranslationCache cache,
        OpenAiTranslatorOptions options,
        ILorekeeperLogger logger,
        TerminologyStore? terminologyStore,
        ConversationMemory? conversationMemory)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.terminologyStore = terminologyStore;
        this.conversationMemory = conversationMemory;
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

        conversationMemory?.Add(
            npcName,
            text,
            cachedTranslation);

        result = CreateResult(
            text,
            cachedTranslation,
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

        return cache.TryGet(
            cacheKey,
            out translatedText!);
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

        TrySaveToCache(
            cacheKey,
            translatedText);

        conversationMemory?.Add(
            npcName,
            text,
            translatedText);

        logger.Information(
            "OPENAI CACHE: Zapisano tłumaczenie pobrane z Lorekeeper Cloud.");

        return CreateResult(
            text,
            translatedText,
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

            string translatedText = GetTranslatedText(completion);

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                logger.Warning(
                    "OPENAI: Otrzymano pustą odpowiedź.");

                return CreateResult(
                    text,
                    "OpenAI zwróciło pustą odpowiedź.");
            }

            int inputTokens = completion.Usage?.InputTokenCount ?? 0;
            int outputTokens = completion.Usage?.OutputTokenCount ?? 0;
            decimal costUsd = CalculateCost(inputTokens, outputTokens);

            LogUsage(inputTokens, outputTokens, costUsd);

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

        string conversationContext =
            BuildConversationContext();

        return
        [
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(
                $"Kontekst adresata: {playerContext}\n" +
                $"Kontekst mówcy: {speakerContext}\n" +
                terminologyContext +
                conversationContext +
                $"NPC/mówca: {npcName}\n" +
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

        return $"{cacheNamespace}\u001F" +
               $"{context.PlayerCharacterSex}\u001F" +
               $"{context.SpeakerSex}\u001F" +
               $"{terminologyKey}\u001F" +
               $"{npcName}\u001F{text}";
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

    private static string GetTranslatedText(ChatCompletion completion)
    {
        return completion.Content.Count > 0
            ? completion.Content[0].Text.Trim()
            : string.Empty;
    }

    private static decimal CalculateCost(
        int inputTokens,
        int outputTokens)
    {
        decimal inputCost =
            inputTokens / 1_000_000m
            * InputPricePerMillionTokens;

        decimal outputCost =
            outputTokens / 1_000_000m
            * OutputPricePerMillionTokens;

        return inputCost + outputCost;
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
