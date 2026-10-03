using System.Net;
using System.Net.Sockets;

namespace RonSijm.Blazyload.Components.IntegrationTests;

internal sealed class PublishedApplicationServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly string _basePath;
    private readonly Action<string> _log;
    private readonly Task _serving;

    public PublishedApplicationServer(string root, string basePath, Action<string> log)
    {
        if (!basePath.StartsWith('/') || !basePath.EndsWith('/'))
        {
            throw new ArgumentException("The application base path must start and end with '/'.", nameof(basePath));
        }

        _root = Path.GetFullPath(root);
        _basePath = basePath;
        _log = log;
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        BaseUri = $"http://127.0.0.1:{port}{basePath}";
        _listener.Prefixes.Add(BaseUri);
        _listener.Start();
        _serving = ServeAsync();
    }

    public string BaseUri { get; }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (!_listener.IsListening)
            {
                return;
            }
            catch (ObjectDisposedException) when (!_listener.IsListening)
            {
                return;
            }

            var relative = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath[_basePath.Length..]).Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0)
            {
                relative = "index.html";
            }

            var path = Path.GetFullPath(Path.Combine(_root, relative));
            if (Directory.Exists(path))
            {
                path = Path.Combine(path, "index.html");
            }

            using var response = context.Response;
            if (!path.StartsWith($"{_root}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                response.StatusCode = 404;
                continue;
            }

            response.ContentType = Path.GetExtension(path) switch
            {
                ".html" => "text/html",
                ".js" => "text/javascript",
                ".json" => "application/json",
                ".css" => "text/css",
                ".wasm" => "application/wasm",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
            var content = await File.ReadAllBytesAsync(path);
            response.ContentLength64 = content.Length;
            try
            {
                await response.OutputStream.WriteAsync(content);
            }
            catch (HttpListenerException exception) when (exception.NativeErrorCode is 64 or 1229)
            {
                _log($"Browser disconnected while serving {context.Request.Url}: {exception.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await _serving;
        _listener.Close();
    }
}
