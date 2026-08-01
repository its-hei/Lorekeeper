using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lorekeeper.OBS;

public sealed class ObsOverlayServer : IDisposable
{
    public const int DefaultPort = 19742;

    private static readonly Encoding Utf8WithoutBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<DialogueSnapshot> snapshotProvider;
    private readonly ILorekeeperLogger logger;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly TcpListener listener;
    private readonly string? overlayPath;

    private Task? listenLoopTask;
    private bool isRunning;
    private bool disposed;

    public ObsOverlayServer(
        Func<DialogueSnapshot> snapshotProvider,
        ILorekeeperLogger logger,
        int port = DefaultPort)
    {
        this.snapshotProvider = snapshotProvider
            ?? throw new ArgumentNullException(nameof(snapshotProvider));

        this.logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        listener = new TcpListener(IPAddress.Loopback, port);
        overlayPath = FindOverlayPath();

        TryStart(port);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellationTokenSource.Cancel();

        if (isRunning)
        {
            try
            {
                listener.Stop();
            }
            catch (Exception exception)
            {
                logger.Warning(
                    $"Nie udało się zatrzymać lokalnego API OBS: {exception.Message}");
            }
        }

        try
        {
            listenLoopTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"Lokalne API OBS zakończyło pracę z błędem: {exception.Message}");
        }

        cancellationTokenSource.Dispose();
    }

    private string? FindOverlayPath()
    {
        string? pluginDirectory =
            Plugin.PluginInterface.AssemblyLocation.DirectoryName;

        string? loadedAssemblyDirectory =
            Path.GetDirectoryName(
                typeof(ObsOverlayServer).Assembly.Location);

        string[] candidateDirectories =
        {
            pluginDirectory ?? string.Empty,
            loadedAssemblyDirectory ?? string.Empty,
            AppContext.BaseDirectory
        };

        foreach (string directory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidatePath = Path.Combine(
                directory,
                "Assets",
                "OBS",
                "overlay.html");

            logger.Information(
                $"Sprawdzam plik overlayu OBS: {candidatePath}");

            if (!File.Exists(candidatePath))
            {
                continue;
            }

            logger.Information(
                $"Znaleziono overlay OBS: {candidatePath}");

            return candidatePath;
        }

        logger.Warning(
            "Nie znaleziono pliku Assets/OBS/overlay.html " +
            "w folderze pluginu.");

        return null;
    }

    private string? ReadOverlayHtml()
    {
        if (string.IsNullOrWhiteSpace(overlayPath))
        {
            return null;
        }

        try
        {
            if (!File.Exists(overlayPath))
            {
                logger.Warning(
                    $"Nie znaleziono pliku overlayu OBS: {overlayPath}");

                return null;
            }

            return File.ReadAllText(
                overlayPath,
                Utf8WithoutBom);
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"Nie udało się odczytać overlayu OBS z {overlayPath}: " +
                exception.Message);

            return null;
        }
    }

    private void TryStart(int port)
    {
        try
        {
            listener.Start();
            isRunning = true;

            listenLoopTask = Task.Run(
                () => ListenLoopAsync(cancellationTokenSource.Token));

            logger.Information(
                $"OBS overlay uruchomiony: http://127.0.0.1:{port}/");

            logger.Information(
                $"OBS API uruchomione: http://127.0.0.1:{port}/api/dialogue");
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"Nie udało się uruchomić lokalnego API OBS na porcie {port}. " +
                $"Lorekeeper i overlay ImGui nadal będą działać. " +
                $"Szczegóły: {exception.Message}");
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using TcpClient client =
                    await listener.AcceptTcpClientAsync(cancellationToken);

                await HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.Error(
                    exception,
                    "Błąd podczas obsługi lokalnego API OBS.");

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(250),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        client.NoDelay = true;

        using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Utf8WithoutBom,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        string? requestLine =
            await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        await ReadHeadersAsync(reader, cancellationToken);

        string[] requestParts = requestLine.Split(
            ' ',
            3,
            StringSplitOptions.RemoveEmptyEntries);

        if (requestParts.Length < 2
            || !string.Equals(
                requestParts[0],
                "GET",
                StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(
                stream,
                HttpStatusCode.MethodNotAllowed,
                "text/plain; charset=utf-8",
                "Method Not Allowed",
                cancellationToken);

            return;
        }

        string path = requestParts[1];
        int queryStart = path.IndexOf('?');

        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        if (string.Equals(
                path,
                "/api/dialogue",
                StringComparison.OrdinalIgnoreCase))
        {
            DialogueSnapshot snapshot = snapshotProvider();
            string json = JsonSerializer.Serialize(snapshot);

            await WriteResponseAsync(
                stream,
                HttpStatusCode.OK,
                "application/json; charset=utf-8",
                json,
                cancellationToken);

            return;
        }

        if (string.Equals(
                path,
                "/",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                path,
                "/overlay",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                path,
                "/overlay.html",
                StringComparison.OrdinalIgnoreCase))
        {
            string? overlayHtml = ReadOverlayHtml();

            if (overlayHtml is null)
            {
                await WriteResponseAsync(
                    stream,
                    HttpStatusCode.NotFound,
                    "text/plain; charset=utf-8",
                    "Nie znaleziono pliku Assets/OBS/overlay.html.",
                    cancellationToken);

                return;
            }

            await WriteResponseAsync(
                stream,
                HttpStatusCode.OK,
                "text/html; charset=utf-8",
                overlayHtml,
                cancellationToken);

            return;
        }

        await WriteResponseAsync(
            stream,
            HttpStatusCode.NotFound,
            "text/plain; charset=utf-8",
            "Not Found",
            cancellationToken);
    }

    private static async Task ReadHeadersAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? headerLine =
                await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(headerLine))
            {
                return;
            }
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        HttpStatusCode statusCode,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Utf8WithoutBom.GetBytes(body);

        string statusText = statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            _ => "Not Found"
        };

        string headers =
            $"HTTP/1.1 {(int)statusCode} {statusText}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            "Pragma: no-cache\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        byte[] headerBytes = Utf8WithoutBom.GetBytes(headers);

        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
