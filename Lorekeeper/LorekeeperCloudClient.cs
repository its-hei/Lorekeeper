using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Lorekeeper;

public sealed record CloudTranslationHit(
    string TranslatedText,
    string? Model,
    int Confirmations);

public sealed class LorekeeperCloudClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Configuration configuration;
    private readonly ILorekeeperLogger logger;
    private readonly CloudTranslationIdentityBuilder identityBuilder;
    private readonly HttpClient httpClient = new();
    private readonly string clientVersion;
    private bool disposed;

    public LorekeeperCloudClient(
        Configuration configuration,
        TerminologyStore? terminologyStore,
        ILorekeeperLogger logger)
    {
        this.configuration = configuration
            ?? throw new ArgumentNullException(nameof(configuration));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        identityBuilder =
            new CloudTranslationIdentityBuilder(
                terminologyStore);

        clientVersion =
            typeof(Plugin).Assembly
                .GetName()
                .Version?
                .ToString()
            ?? "0.0.0.0";
    }

    public bool IsConfigured =>
        configuration.CloudEnabled
        && Uri.TryCreate(
            NormalizeBaseUrl(configuration.CloudApiUrl),
            UriKind.Absolute,
            out _);

    public async Task<CloudTranslationHit?> TryGetOpenAiAsync(
        string text,
        string npcName,
        TranslationContext context)
    {
        if (!IsConfigured)
        {
            return null;
        }

        CloudTranslationIdentity identity =
            identityBuilder.Create(
                text,
                npcName,
                context);

        string requestUrl =
            $"{NormalizeBaseUrl(configuration.CloudApiUrl)}" +
            $"/v1/translations/{identity.LookupKey}";

        try
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    requestUrl);

            AddClientHeaders(request);

            using var cts =
                new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(
                        Math.Clamp(
                            configuration.CloudLookupTimeoutMilliseconds,
                            1800,
                            3000)));

            using HttpResponseMessage response =
                await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.Warning(
                    $"CLOUD: Lookup HTTP {(int)response.StatusCode}.");

                return null;
            }

            string json =
                await response.Content.ReadAsStringAsync(
                    cts.Token);

            LookupResponse? result =
                JsonSerializer.Deserialize<LookupResponse>(
                    json,
                    JsonOptions);

            if (result is null
                || !result.Hit
                || !string.Equals(
                    result.Provider,
                    "openai",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(
                    result.TranslatedText))
            {
                return null;
            }

            logger.Information(
                $"CLOUD: HIT OpenAI, confirmations={result.Confirmations}.");

            return new CloudTranslationHit(
                result.TranslatedText.Trim(),
                result.Model,
                result.Confirmations);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException exception)
        {
            logger.Warning(
                $"CLOUD: Nieprawidłowa odpowiedź JSON: {exception.Message}");

            return null;
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"CLOUD: Lookup nie powiódł się: {exception.Message}");

            return null;
        }
    }

    public async Task SubmitOpenAiAsync(
        string text,
        string npcName,
        TranslationContext context,
        string translatedText,
        string? model)
    {
        if (!IsConfigured
            || string.IsNullOrWhiteSpace(translatedText))
        {
            return;
        }

        CloudTranslationIdentity identity =
            identityBuilder.Create(
                text,
                npcName,
                context);

        var payload =
            new SubmitRequest
            {
                LookupKey =
                    identity.LookupKey,
                ProfileVersion =
                    identity.ProfileVersion,
                SourceLanguage =
                    identity.SourceLanguage,
                TargetLanguage =
                    identity.TargetLanguage,
                SourceText =
                    identity.SourceText,
                TranslatedText =
                    translatedText.Trim(),
                NpcName =
                    identity.NpcName,
                PlayerSex =
                    identity.PlayerSex,
                SpeakerSex =
                    identity.SpeakerSex,
                TerminologyFingerprint =
                    identity.TerminologyFingerprint,
                Provider =
                    "openai",
                Model =
                    model,
                ClientId =
                    configuration.CloudClientId,
                ClientVersion =
                    clientVersion
            };

        try
        {
            string json =
                JsonSerializer.Serialize(payload);

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{NormalizeBaseUrl(configuration.CloudApiUrl)}/v1/translations")
                {
                    Content =
                        new StringContent(
                            json,
                            Encoding.UTF8,
                            "application/json")
                };

            AddClientHeaders(request);

            using var cts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));

            using HttpResponseMessage response =
                await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.Warning(
                    $"CLOUD: OpenAI submit HTTP {(int)response.StatusCode}.");
            }
        }
        catch
        {
            // Cloud jest opcjonalny. Błąd uploadu nie może wpływać
            // na tłumaczenie widoczne dla gracza.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        httpClient.Dispose();
    }

    private void AddClientHeaders(
        HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation(
            "X-Lorekeeper-Client",
            configuration.CloudClientId);

        request.Headers.TryAddWithoutValidation(
            "X-Lorekeeper-Version",
            clientVersion);
    }

    private static string NormalizeBaseUrl(
        string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .TrimEnd('/');
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("hit")]
        public bool Hit { get; init; }

        [JsonPropertyName("provider")]
        public string Provider { get; init; } = string.Empty;

        [JsonPropertyName("translatedText")]
        public string TranslatedText { get; init; } = string.Empty;

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("confirmations")]
        public int Confirmations { get; init; }
    }

    private sealed class SubmitRequest
    {
        [JsonPropertyName("lookupKey")]
        public string LookupKey { get; init; } = string.Empty;

        [JsonPropertyName("profileVersion")]
        public int ProfileVersion { get; init; }

        [JsonPropertyName("sourceLanguage")]
        public string SourceLanguage { get; init; } = string.Empty;

        [JsonPropertyName("targetLanguage")]
        public string TargetLanguage { get; init; } = string.Empty;

        [JsonPropertyName("sourceText")]
        public string SourceText { get; init; } = string.Empty;

        [JsonPropertyName("translatedText")]
        public string TranslatedText { get; init; } = string.Empty;

        [JsonPropertyName("npcName")]
        public string NpcName { get; init; } = string.Empty;

        [JsonPropertyName("playerSex")]
        public string PlayerSex { get; init; } = string.Empty;

        [JsonPropertyName("speakerSex")]
        public string SpeakerSex { get; init; } = string.Empty;

        [JsonPropertyName("terminologyFingerprint")]
        public string TerminologyFingerprint { get; init; } = string.Empty;

        [JsonPropertyName("provider")]
        public string Provider { get; init; } = "openai";

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("clientId")]
        public string ClientId { get; init; } = string.Empty;

        [JsonPropertyName("clientVersion")]
        public string? ClientVersion { get; init; }
    }
}
