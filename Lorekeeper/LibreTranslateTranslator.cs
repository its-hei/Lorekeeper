using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Lorekeeper;

public sealed class LibreTranslateTranslator : ITranslator
{
    private const string CacheKeyVersion = "1";
    private const string SourceLanguage = "en";
    private const string TargetLanguage = "pl";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly TranslationCache cache;
    private readonly ILorekeeperLogger logger;
    private readonly ConversationMemory? conversationMemory;
    private readonly string endpoint;

    public LibreTranslateTranslator(
        TranslationCache cache,
        ILorekeeperLogger logger,
        ConversationMemory? conversationMemory = null,
        string baseUrl = "http://127.0.0.1:5000")
    {
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.conversationMemory = conversationMemory;

        string normalizedBaseUrl =
            string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:5000"
                : baseUrl.Trim().TrimEnd('/');

        endpoint = $"{normalizedBaseUrl}/translate";
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string npcName,
        TranslationContext context)
    {
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

        string cacheKey = CreateCacheKey(text);

        return await TranslateWithLibreAsync(
            text,
            npcName,
            cacheKey);
    }

    public bool TryGetCachedTranslation(
        string text,
        string npcName,
        TranslationContext context,
        out TranslationResult result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = CreateResult(
                text,
                string.Empty,
                fromCache: true);

            return true;
        }

        string cacheKey =
            CreateCacheKey(text);

        if (!cache.TryGet(
                cacheKey,
                out string cachedTranslation))
        {
            result = null!;
            return false;
        }

        logger.Information(
            "LIBRE: Tłumaczenie znalezione w lokalnej bazie.");

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
        out string translatedText)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            translatedText = string.Empty;
            return true;
        }

        return cache.TryGet(
            CreateCacheKey(text),
            out translatedText!);
    }

    public TranslationResult StoreCloudTranslation(
        string text,
        string npcName,
        TranslationContext context,
        string translatedText)
    {
        string cacheKey =
            CreateCacheKey(text);

        TrySaveToCache(
            cacheKey,
            translatedText);

        conversationMemory?.Add(
            npcName,
            text,
            translatedText);

        logger.Information(
            "LIBRE CACHE: Zapisano tłumaczenie pobrane z Lorekeeper Cloud.");

        return CreateResult(
            text,
            translatedText,
            fromCache: true);
    }

    private async Task<TranslationResult> TranslateWithLibreAsync(
        string text,
        string npcName,
        string cacheKey)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            logger.Information(
                $"LIBRE: Wysyłanie zapytania do {endpoint}...");

            var request = new LibreTranslateRequest
            {
                Q = text,
                Source = SourceLanguage,
                Target = TargetLanguage,
                Format = "text"
            };

            string json = JsonSerializer.Serialize(request);

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await HttpClient.PostAsync(endpoint, content);

            string responseBody =
                await response.Content.ReadAsStringAsync();

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                logger.Warning(
                    $"LIBRE: HTTP {(int)response.StatusCode} po " +
                    $"{stopwatch.ElapsedMilliseconds} ms. Odpowiedź: {responseBody}");

                return CreateResult(
                    text,
                    $"Błąd LibreTranslate: HTTP {(int)response.StatusCode}.");
            }

            LibreTranslateResponse? result =
                JsonSerializer.Deserialize<LibreTranslateResponse>(
                    responseBody,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            string translatedText =
                result?.TranslatedText?.Trim()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                logger.Warning(
                    "LIBRE: Otrzymano pustą odpowiedź.");

                return CreateResult(
                    text,
                    "LibreTranslate zwrócił pustą odpowiedź.");
            }

            logger.Information(
                $"LIBRE: Odpowiedź odebrana po {stopwatch.ElapsedMilliseconds} ms.");

            TrySaveToCache(
                cacheKey,
                translatedText);

            conversationMemory?.Add(
                npcName,
                text,
                translatedText);

            return CreateResult(
                text,
                translatedText);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();

            logger.Error(
                exception,
                $"LIBRE: Nie można połączyć się z {endpoint}.");

            return CreateResult(
                text,
                "LibreTranslate nie odpowiada pod adresem http://127.0.0.1:5000. " +
                "Uruchom lokalny translator albo wybierz OpenAI w /lore.");
        }
        catch (TaskCanceledException exception)
        {
            stopwatch.Stop();

            logger.Error(
                exception,
                "LIBRE: Przekroczono limit czasu zapytania.");

            return CreateResult(
                text,
                "LibreTranslate nie odpowiedział na czas.");
        }
        catch (JsonException exception)
        {
            stopwatch.Stop();

            logger.Error(
                exception,
                "LIBRE: Nie udało się odczytać odpowiedzi JSON.");

            return CreateResult(
                text,
                "LibreTranslate zwrócił nieprawidłową odpowiedź.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            logger.Error(
                exception,
                $"LIBRE: Tłumaczenie nie powiodło się po " +
                $"{stopwatch.ElapsedMilliseconds} ms.");

            return CreateResult(
                text,
                $"Błąd LibreTranslate: {exception.Message}");
        }
    }

    private void TrySaveToCache(
        string cacheKey,
        string translatedText)
    {
        try
        {
            cache.Add(cacheKey, translatedText);

            logger.Information(
                "LIBRE: Tłumaczenie zapisane w translations-libre.json.");
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "LIBRE CACHE: Nie udało się zapisać tłumaczenia. " +
                "Gotowe tłumaczenie zostanie mimo to wyświetlone.");
        }
    }

    private static string CreateCacheKey(string text)
    {
        return $"{CacheKeyVersion}\u001F{SourceLanguage}\u001F{TargetLanguage}\u001F{text}";
    }

    private static TranslationResult CreateResult(
        string originalText,
        string translatedText,
        bool fromCache = false)
    {
        return new TranslationResult
        {
            OriginalText = originalText,
            TranslatedText = translatedText,
            FromCache = fromCache,
            InputTokens = 0,
            OutputTokens = 0,
            CostUsd = 0m
        };
    }

    private sealed class LibreTranslateRequest
    {
        [JsonPropertyName("q")]
        public string Q { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; init; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; init; } = "text";
    }

    private sealed class LibreTranslateResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; init; }
    }
}
