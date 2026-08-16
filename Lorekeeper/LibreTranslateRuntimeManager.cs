using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Lorekeeper;

public enum LibreTranslateRuntimeStatus
{
    NotInstalled,
    DownloadingRuntime,
    PreparingRuntime,
    InstallingLibreTranslate,
    Starting,
    Ready,
    Removing,
    Error
}

/// <summary>
/// Installs and manages a private LibreTranslate runtime owned by Lorekeeper.
/// Nothing is added to PATH and the user does not need Python or Docker.
/// </summary>
public sealed class LibreTranslateRuntimeManager : IDisposable
{
    private const string PythonVersion = "3.11.9";
    private const string LibreTranslateVersion = "1.9.6";

    private const string PythonArchiveUrl =
        "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip";

    private const string GetPipUrl =
        "https://bootstrap.pypa.io/get-pip.py";

    private const string TranslateEndpoint =
        "http://127.0.0.1:5000/translate";

    private static readonly TimeSpan ServerStartupTimeout =
        TimeSpan.FromMinutes(10);

    private readonly ILorekeeperLogger logger;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object stateLock = new();

    private readonly string rootDirectory;
    private readonly string runtimeDirectory;
    private readonly string dataDirectory;
    private readonly string configDirectory;
    private readonly string cacheDirectory;
    private readonly string modelsDirectory;
    private readonly string markerPath;
    private readonly string pythonExecutablePath;

    private Process? serverProcess;
    private LibreTranslateRuntimeStatus status;
    private string statusText = "Niezainstalowany";
    private string lastError = string.Empty;
    private string lastRuntimeMessage = string.Empty;
    private int installationProgressPercent;
    private bool disposed;

    public LibreTranslateRuntimeManager(
        string pluginConfigDirectory,
        ILorekeeperLogger logger)
    {
        if (string.IsNullOrWhiteSpace(pluginConfigDirectory))
        {
            throw new ArgumentException(
                "Katalog konfiguracji pluginu nie może być pusty.",
                nameof(pluginConfigDirectory));
        }

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        rootDirectory = Path.Combine(
            pluginConfigDirectory,
            "LibreTranslate");

        runtimeDirectory = Path.Combine(
            rootDirectory,
            "runtime");

        dataDirectory = Path.Combine(
            rootDirectory,
            "data");

        configDirectory = Path.Combine(
            rootDirectory,
            "config");

        cacheDirectory = Path.Combine(
            rootDirectory,
            "cache");

        modelsDirectory = Path.Combine(
            rootDirectory,
            "models");

        markerPath = Path.Combine(
            rootDirectory,
            "installed.txt");

        pythonExecutablePath = Path.Combine(
            runtimeDirectory,
            "python.exe");

        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        if (IsInstalled)
        {
            SetState(
                LibreTranslateRuntimeStatus.Starting,
                "Zainstalowany - oczekiwanie na uruchomienie");
        }
        else
        {
            SetState(
                LibreTranslateRuntimeStatus.NotInstalled,
                "Niezainstalowany");
        }
    }

    public string RootDirectory => rootDirectory;

    public bool IsInstalled =>
        File.Exists(markerPath)
        && File.Exists(pythonExecutablePath);

    public bool IsReady =>
        Status == LibreTranslateRuntimeStatus.Ready;

    public bool IsBusy =>
        Status is LibreTranslateRuntimeStatus.DownloadingRuntime
            or LibreTranslateRuntimeStatus.PreparingRuntime
            or LibreTranslateRuntimeStatus.InstallingLibreTranslate
            or LibreTranslateRuntimeStatus.Starting
            or LibreTranslateRuntimeStatus.Removing;

    public LibreTranslateRuntimeStatus Status
    {
        get
        {
            lock (stateLock)
            {
                return status;
            }
        }
    }

    public string StatusText
    {
        get
        {
            lock (stateLock)
            {
                return statusText;
            }
        }
    }

    public int InstallationProgressPercent
    {
        get
        {
            lock (stateLock)
            {
                return installationProgressPercent;
            }
        }
    }

    public string LastError
    {
        get
        {
            lock (stateLock)
            {
                return lastError;
            }
        }
    }

    public async Task StartIfInstalledAsync()
    {
        ThrowIfDisposed();

        await operationGate.WaitAsync();

        try
        {
            if (!IsInstalled)
            {
                SetState(
                    LibreTranslateRuntimeStatus.NotInstalled,
                    "Niezainstalowany");

                return;
            }

            if (await IsEndpointReadyAsync())
            {
                SetState(
                    LibreTranslateRuntimeStatus.Ready,
                    "Gotowy");

                return;
            }

            SetState(
                LibreTranslateRuntimeStatus.Starting,
                "Uruchamianie LibreTranslate...",
                88);

            await StartServerAndWaitUntilReadyAsync();
        }
        catch (Exception exception)
        {
            HandleFailure(
                exception,
                "Nie udało się uruchomić LibreTranslate.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task InstallAsync()
    {
        return InstallAsync(reinstall: false);
    }

    public async Task InstallAsync(bool reinstall)
    {
        ThrowIfDisposed();

        await operationGate.WaitAsync();

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Automatyczny runtime LibreTranslate jest obecnie przygotowany dla Windows x64.");
            }

            if (!Environment.Is64BitOperatingSystem)
            {
                throw new PlatformNotSupportedException(
                    "LibreTranslate wymaga 64-bitowego Windows w tej paczce runtime.");
            }

            ClearError();
            StopServer();

            if (reinstall || Directory.Exists(rootDirectory))
            {
                DeleteRuntimeDirectoryIfPresent();
            }

            Directory.CreateDirectory(rootDirectory);
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(cacheDirectory);
            Directory.CreateDirectory(modelsDirectory);

            await InstallPythonRuntimeAsync();
            await InstallPipAsync();
            await InstallLibreTranslateAsync();

            SetState(
                LibreTranslateRuntimeStatus.Starting,
                "Pobieranie modelu i uruchamianie LibreTranslate...",
                85);

            await StartServerAndWaitUntilReadyAsync();

            await File.WriteAllTextAsync(
                markerPath,
                $"Python={PythonVersion}{Environment.NewLine}" +
                $"LibreTranslate={LibreTranslateVersion}{Environment.NewLine}" +
                "Languages=en,pl" + Environment.NewLine,
                Encoding.UTF8);

            SetState(
                LibreTranslateRuntimeStatus.Ready,
                "Gotowy");

            logger.Information(
                "LIBRE RUNTIME: Instalacja zakończona. Serwer jest gotowy.");
        }
        catch (Exception exception)
        {
            StopServer();

            HandleFailure(
                exception,
                "Instalacja LibreTranslate nie powiodła się.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task RemoveAsync()
    {
        ThrowIfDisposed();

        await operationGate.WaitAsync();

        try
        {
            SetState(
                LibreTranslateRuntimeStatus.Removing,
                "Usuwanie LibreTranslate...",
                0);

            StopServer();
            DeleteRuntimeDirectoryIfPresent();

            SetState(
                LibreTranslateRuntimeStatus.NotInstalled,
                "Niezainstalowany");

            ClearError();

            logger.Information(
                "LIBRE RUNTIME: Lokalna instalacja została usunięta.");
        }
        catch (Exception exception)
        {
            HandleFailure(
                exception,
                "Nie udało się usunąć LibreTranslate.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        StopServer();
        httpClient.Dispose();
        operationGate.Dispose();
    }

    private async Task InstallPythonRuntimeAsync()
    {
        SetState(
            LibreTranslateRuntimeStatus.DownloadingRuntime,
            "Pobieranie lokalnego runtime Python...",
            2);

        string archivePath = Path.Combine(
            rootDirectory,
            "python-runtime.zip");

        await DownloadFileAsync(
            PythonArchiveUrl,
            archivePath,
            "Pobieranie lokalnego runtime Python",
            2,
            18);

        SetState(
            LibreTranslateRuntimeStatus.PreparingRuntime,
            "Rozpakowywanie runtime Python...",
            20);

        Directory.CreateDirectory(runtimeDirectory);

        ZipFile.ExtractToDirectory(
            archivePath,
            runtimeDirectory,
            overwriteFiles: true);

        TryDeleteFile(archivePath);

        if (!File.Exists(pythonExecutablePath))
        {
            throw new FileNotFoundException(
                "Nie znaleziono python.exe po rozpakowaniu runtime.",
                pythonExecutablePath);
        }

        // Windows Embeddable Package trzyma bibliotekę standardową w python311.zip.
        // Rozpakowujemy ją do Lib, żeby Python zawsze miał fizyczny dostęp m.in.
        // do modułu encodings. Unikamy w ten sposób błędu init_fs_encoding,
        // który może pojawić się przy bootstrapowaniu pip w trybie embedded.
        string standardLibraryZipPath = Path.Combine(
            runtimeDirectory,
            "python311.zip");

        if (!File.Exists(standardLibraryZipPath))
        {
            throw new FileNotFoundException(
                "Nie znaleziono python311.zip w pobranym runtime.",
                standardLibraryZipPath);
        }

        string standardLibraryDirectory = Path.Combine(
            runtimeDirectory,
            "Lib");

        Directory.CreateDirectory(standardLibraryDirectory);

        ZipFile.ExtractToDirectory(
            standardLibraryZipPath,
            standardLibraryDirectory,
            overwriteFiles: true);

        string encodingsDirectory = Path.Combine(
            standardLibraryDirectory,
            "encodings");

        string encodingsPyPath = Path.Combine(
            encodingsDirectory,
            "__init__.py");

        string encodingsPycPath = Path.Combine(
            encodingsDirectory,
            "__init__.pyc");

        // Embeddable Python dostarcza bibliotekę standardową głównie jako
        // prekompilowane pliki .pyc. Akceptujemy oba warianty.
        if (!File.Exists(encodingsPyPath)
            && !File.Exists(encodingsPycPath))
        {
            throw new FileNotFoundException(
                "Runtime Python został rozpakowany, ale brakuje modułu encodings (.py/.pyc).",
                encodingsDirectory);
        }

        Directory.CreateDirectory(
            Path.Combine(
                standardLibraryDirectory,
                "site-packages"));

        string pthPath = Path.Combine(
            runtimeDirectory,
            "python311._pth");

        if (!File.Exists(pthPath))
        {
            throw new FileNotFoundException(
                "Nie znaleziono python311._pth w pobranym runtime.",
                pthPath);
        }

        // Nie modyfikujemy już fabrycznego pliku linia po linii.
        // Budujemy minimalny, przewidywalny sys.path dla prywatnego runtime Lorekeepera.
        string pthContent =
            "python311.zip" + Environment.NewLine +
            "." + Environment.NewLine +
            "Lib" + Environment.NewLine +
            @"Lib\site-packages" + Environment.NewLine +
            "import site" + Environment.NewLine;

        await File.WriteAllTextAsync(
            pthPath,
            pthContent,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        SetState(
            LibreTranslateRuntimeStatus.PreparingRuntime,
            "Sprawdzanie lokalnego runtime Python...",
            26);

        await RunProcessAsync(
            pythonExecutablePath,
            [
                "-c",
                "import encodings, sys; print('Lorekeeper Python OK'); print(encodings.__file__); print(sys.executable)"
            ],
            "Test runtime Python");

        SetState(
            LibreTranslateRuntimeStatus.PreparingRuntime,
            "Runtime Python gotowy.",
            28);
    }

    private async Task InstallPipAsync()
    {
        SetState(
            LibreTranslateRuntimeStatus.PreparingRuntime,
            "Przygotowanie pip...",
            30);

        string getPipPath = Path.Combine(
            rootDirectory,
            "get-pip.py");

        await DownloadFileAsync(
            GetPipUrl,
            getPipPath,
            "Pobieranie instalatora pip",
            30,
            33);

        await RunProcessAsync(
            pythonExecutablePath,
            [
                getPipPath,
                "--disable-pip-version-check"
            ],
            "Instalacja pip");

        SetState(
            LibreTranslateRuntimeStatus.PreparingRuntime,
            "pip zainstalowany. Przygotowanie pakietów...",
            38);

        TryDeleteFile(getPipPath);

        await RunProcessAsync(
            pythonExecutablePath,
            [
                "-m",
                "pip",
                "install",
                "--disable-pip-version-check",
                "--no-warn-script-location",
                "--upgrade",
                "pip",
                "setuptools",
                "wheel"
            ],
            "Aktualizacja pip/setuptools/wheel");

        SetState(
            LibreTranslateRuntimeStatus.PreparingRuntime,
            "Środowisko instalacyjne gotowe.",
            45);
    }

    private async Task InstallLibreTranslateAsync()
    {
        SetState(
            LibreTranslateRuntimeStatus.InstallingLibreTranslate,
            $"Instalowanie LibreTranslate {LibreTranslateVersion}...",
            50);

        await RunProcessAsync(
            pythonExecutablePath,
            [
                "-m",
                "pip",
                "install",
                "--disable-pip-version-check",
                "--no-warn-script-location",
                $"libretranslate=={LibreTranslateVersion}"
            ],
            "Instalacja LibreTranslate");

        SetState(
            LibreTranslateRuntimeStatus.InstallingLibreTranslate,
            "LibreTranslate zainstalowany.",
            82);
    }

    private async Task StartServerAndWaitUntilReadyAsync()
    {
        if (await IsEndpointReadyAsync())
        {
            SetState(
                LibreTranslateRuntimeStatus.Ready,
                "Gotowy");

            return;
        }

        StopServer();
        EnsureRuntimeDirectories();

        var startInfo = CreatePythonStartInfo();

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("libretranslate.main");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("5000");
        startInfo.ArgumentList.Add("--load-only");
        startInfo.ArgumentList.Add("en,pl");
        startInfo.ArgumentList.Add("--disable-web-ui");
        startInfo.ArgumentList.Add("--disable-files-translation");

        serverProcess = new Process
        {
            StartInfo = startInfo
        };

        serverProcess.OutputDataReceived += OnRuntimeOutput;
        serverProcess.ErrorDataReceived += OnRuntimeOutput;

        if (!serverProcess.Start())
        {
            throw new InvalidOperationException(
                "Nie udało się uruchomić procesu LibreTranslate.");
        }

        serverProcess.BeginOutputReadLine();
        serverProcess.BeginErrorReadLine();

        SetState(
            LibreTranslateRuntimeStatus.Starting,
            "Oczekiwanie na gotowość lokalnego translatora...",
            92);

        Stopwatch timeout = Stopwatch.StartNew();

        while (timeout.Elapsed < ServerStartupTimeout)
        {
            if (serverProcess.HasExited)
            {
                string detail = GetLastRuntimeMessage();

                throw new InvalidOperationException(
                    $"LibreTranslate zakończył proces przed uruchomieniem " +
                    $"(kod {serverProcess.ExitCode}). " +
                    (string.IsNullOrWhiteSpace(detail)
                        ? string.Empty
                        : $"Ostatni komunikat: {detail}"));
            }

            if (await IsEndpointReadyAsync())
            {
                SetState(
                    LibreTranslateRuntimeStatus.Ready,
                    "Gotowy");

                return;
            }

            await Task.Delay(1500);
        }

        throw new TimeoutException(
            "LibreTranslate nie uruchomił się w ciągu 10 minut. " +
            "Sprawdź połączenie internetowe i log Dalamud.");
    }

    private ProcessStartInfo CreatePythonStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutablePath,
            WorkingDirectory = rootDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Nie dziedziczymy globalnego PYTHONHOME/PYTHONPATH użytkownika.
        // Prywatny runtime Lorekeepera ma własny python311._pth.
        startInfo.Environment.Remove("PYTHONHOME");
        startInfo.Environment.Remove("PYTHONPATH");
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONNOUSERSITE"] = "1";

        // Argos/LibreTranslate trzyma wszystko wewnątrz katalogu Lorekeepera.
        startInfo.Environment["XDG_DATA_HOME"] = dataDirectory;
        startInfo.Environment["XDG_CONFIG_HOME"] = configDirectory;
        startInfo.Environment["XDG_CACHE_HOME"] = cacheDirectory;
        startInfo.Environment["ARGOS_PACKAGES_DIR"] = modelsDirectory;
        startInfo.Environment["ARGOS_DEVICE_TYPE"] = "cpu";
        startInfo.Environment["ARGOS_CHUNK_TYPE"] = "MINISBD";

        return startInfo;
    }

    private async Task RunProcessAsync(
        string executable,
        string[] arguments,
        string operationName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = rootDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.Environment.Remove("PYTHONHOME");
        startInfo.Environment.Remove("PYTHONPATH");
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONNOUSERSITE"] = "1";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Nie udało się uruchomić procesu: {operationName}.");
        }

        Task<string> stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> stderrTask =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            logger.Information(
                $"LIBRE INSTALL [{operationName}]: {TrimLog(stdout)}");
        }

        if (process.ExitCode == 0)
        {
            return;
        }

        string detail = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : stderr;

        throw new InvalidOperationException(
            $"{operationName} zakończyła się kodem {process.ExitCode}. " +
            TrimLog(detail));
    }

    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        string progressLabel,
        int overallProgressStart,
        int overallProgressEnd)
    {
        string temporaryPath = destinationPath + ".download";

        TryDeleteFile(temporaryPath);

        using HttpResponseMessage response =
            await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        long? totalBytes =
            response.Content.Headers.ContentLength;

        await using (Stream input =
            await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            byte[] buffer = new byte[81920];
            long downloadedBytes = 0;
            int lastPercent = -1;

            while (true)
            {
                int read = await input.ReadAsync(buffer);

                if (read <= 0)
                {
                    break;
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read));

                downloadedBytes += read;

                if (totalBytes is > 0)
                {
                    int percent = (int)Math.Clamp(
                        downloadedBytes * 100L / totalBytes.Value,
                        0L,
                        100L);

                    if (percent != lastPercent
                        && (percent % 5 == 0 || percent == 100))
                    {
                        lastPercent = percent;

                        int overallPercent =
                            overallProgressStart
                            + (int)Math.Round(
                                (overallProgressEnd - overallProgressStart)
                                * (percent / 100.0));

                        SetState(
                            Status,
                            progressLabel + "...",
                            overallPercent);
                    }
                }
            }

            await output.FlushAsync();
        }

        TryDeleteFile(destinationPath);
        File.Move(
            temporaryPath,
            destinationPath);
    }

    private async Task<bool> IsEndpointReadyAsync()
    {
        try
        {
            var request = new LibreHealthRequest
            {
                Q = "Hello",
                Source = "en",
                Target = "pl",
                Format = "text"
            };

            string json = JsonSerializer.Serialize(request);

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            using var requestCts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(4));

            using HttpResponseMessage response =
                await httpClient.PostAsync(
                    TranslateEndpoint,
                    content,
                    requestCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            string responseBody =
                await response.Content.ReadAsStringAsync(
                    requestCts.Token);

            LibreHealthResponse? result =
                JsonSerializer.Deserialize<LibreHealthResponse>(
                    responseBody,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            return !string.IsNullOrWhiteSpace(
                result?.TranslatedText);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureRuntimeDirectories()
    {
        Directory.CreateDirectory(rootDirectory);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(modelsDirectory);
    }

    private void DeleteRuntimeDirectoryIfPresent()
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        Directory.Delete(
            rootDirectory,
            recursive: true);
    }

    private void StopServer()
    {
        Process? process = serverProcess;
        serverProcess = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);

                process.WaitForExit(5000);
            }
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"LIBRE RUNTIME: Nie udało się łagodnie zatrzymać procesu: " +
                exception.Message);
        }
        finally
        {
            process.Dispose();
        }
    }

    private void OnRuntimeOutput(
        object sender,
        DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }

        lock (stateLock)
        {
            lastRuntimeMessage = args.Data;
        }

        logger.Information(
            $"LIBRE RUNTIME: {args.Data}");
    }

    private string GetLastRuntimeMessage()
    {
        lock (stateLock)
        {
            return lastRuntimeMessage;
        }
    }

    private void SetState(
        LibreTranslateRuntimeStatus newStatus,
        string text,
        int? progressPercent = null)
    {
        lock (stateLock)
        {
            status = newStatus;
            statusText = text;

            if (progressPercent.HasValue)
            {
                installationProgressPercent =
                    Math.Clamp(progressPercent.Value, 0, 100);
            }
            else if (newStatus == LibreTranslateRuntimeStatus.Ready)
            {
                installationProgressPercent = 100;
            }
            else if (newStatus is LibreTranslateRuntimeStatus.NotInstalled
                     or LibreTranslateRuntimeStatus.Error)
            {
                installationProgressPercent = 0;
            }
        }
    }

    private void ClearError()
    {
        lock (stateLock)
        {
            lastError = string.Empty;
        }
    }

    private void HandleFailure(
        Exception exception,
        string publicMessage)
    {
        logger.Error(
            exception,
            $"LIBRE RUNTIME: {publicMessage}");

        lock (stateLock)
        {
            status = LibreTranslateRuntimeStatus.Error;
            statusText = "Błąd";
            lastError = string.IsNullOrWhiteSpace(exception.Message)
                ? publicMessage
                : exception.Message;
        }
    }

    private static string TrimLog(
        string text)
    {
        const int maxLength = 3000;

        string normalized = text.Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[^maxLength..];
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Plik tymczasowy zostanie nadpisany przy kolejnym podejściu.
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(LibreTranslateRuntimeManager));
        }
    }

    private sealed class LibreHealthRequest
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

    private sealed class LibreHealthResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; init; }
    }
}
