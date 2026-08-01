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
    private const string CacheKeyVersion = "3";

    private const string SystemPrompt =
        "Jesteś profesjonalnym tłumaczem dialogów z gry Final Fantasy XIV. " +
        "Tłumacz z języka angielskiego na naturalny język polski. " +
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
    private readonly string cacheNamespace;
    private readonly ChatClient? chatClient;

    public Translator(
        TranslationCache cache,
        OpenAiTranslatorOptions options,
        ILorekeeperLogger logger)
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        string cacheKey = CreateCacheKey(text, npcName, context);

        if (cache.TryGet(cacheKey, out string cachedTranslation))
        {
            logger.Information(
                "OPENAI: Tłumaczenie znalezione w lokalnej bazie.");

            return CreateResult(
                text,
                cachedTranslation,
                fromCache: true);
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

    private static List<ChatMessage> CreateMessages(
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

        const string speakerContext =
            "Płeć NPC będącego mówcą jest nieznana. Nazwa NPC nie określa " +
            "jego płci. Nie zgaduj rodzaju mówcy i nie przypisuj mu płci " +
            "postaci gracza.";

        return
        [
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(
                $"Kontekst adresata: {playerContext}\n" +
                $"Kontekst mówcy: {speakerContext}\n" +
                $"NPC/mówca: {npcName}\n" +
                $"Dialog:\n{text}")
        ];
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
        return $"{cacheNamespace}\u001F" +
               $"{context.PlayerCharacterSex}\u001F" +
               $"{npcName}\u001F{text}";
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
