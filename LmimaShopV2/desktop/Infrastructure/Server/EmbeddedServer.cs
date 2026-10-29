using System.Net;
using LmimaShopV2.Desktop.Config;
using Microsoft.Extensions.Logging;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

public sealed class EmbeddedServer : IAsyncDisposable
{
    private readonly AppConfigService _config;
    private readonly ApiHandlers _api;
    private readonly StaticFileServer _staticFiles;
    private readonly ILogger<EmbeddedServer> _logger;
    private readonly object _sync = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public EmbeddedServer(
        AppConfigService config,
        ApiHandlers api,
        StaticFileServer staticFiles,
        ILogger<EmbeddedServer> logger)
    {
        _config = config;
        _api = api;
        _staticFiles = staticFiles;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }
    public bool BoundToAllInterfaces { get; private set; }
    public string? BoundPrefix { get; private set; }
    public string? LastError { get; private set; }

    public string StatusText => IsRunning
        ? BoundToAllInterfaces
            ? $"Serveur actif sur {BoundPrefix}"
            : $"Serveur actif sur {BoundPrefix} (reseau local indisponible)"
        : LastError is null
            ? "Serveur arrete"
            : $"Serveur arrete : {LastError}";

    public Task<bool> StartAsync()
    {
        lock (_sync)
        {
            if (IsRunning) return Task.FromResult(true);

            LastError = null;
            var port = _config.ServerPort;

            // Try, in order: every interface ("+"), any interface ("*"), then loopback only.
            // "localhost" is deliberately NOT used as the first fallback: an HttpListener bound to
            // "localhost" rejects requests addressed to 127.0.0.1 / the LAN IP, which would silently
            // break the phone app. 127.0.0.1 accepts both "localhost" and "127.0.0.1".
            var candidates = new (string Prefix, bool AllInterfaces)[]
            {
                ($"http://+:{port}/", true),
                ($"http://*:{port}/", true),
                ($"http://127.0.0.1:{port}/", false)
            };

            foreach (var candidate in candidates)
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(candidate.Prefix);
                try
                {
                    listener.Start();
                    _listener = listener;
                    _cts = new CancellationTokenSource();
                    _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
                    BoundToAllInterfaces = candidate.AllInterfaces;
                    BoundPrefix = candidate.Prefix;
                    IsRunning = true;

                    if (candidate.AllInterfaces)
                    {
                        _logger.LogInformation("Embedded server listening on {Prefix}", candidate.Prefix);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Embedded server bound to {Prefix} only. The phone app will not be able to " +
                            "connect over the LAN until the URL ACL is granted: " +
                            "netsh http add urlacl url=http://+:{Port}/ user=Everyone",
                            candidate.Prefix, port);
                    }

                    return Task.FromResult(true);
                }
                catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
                {
                    listener.Close();
                    _logger.LogWarning(ex, "Cannot bind {Prefix}; trying the next fallback", candidate.Prefix);
                }
            }

            var loopbackHint = $"http://127.0.0.1:{port}/";
            LastError = $"Impossible de demarrer le serveur sur le port {port}.";
            _logger.LogError("Unable to start embedded server on port {Port} (all prefixes failed, e.g. {Hint})", port, loopbackHint);
            IsRunning = false;
            return Task.FromResult(false);
        }
    }

    public async Task StopAsync()
    {
        HttpListener? listener;
        CancellationTokenSource? cts;
        Task? loop;
        lock (_sync)
        {
            listener = _listener;
            cts = _cts;
            loop = _acceptLoop;
            _listener = null;
            _cts = null;
            _acceptLoop = null;
            IsRunning = false;
        }

        if (listener is null) return;

        try
        {
            cts?.Cancel();
            listener.Stop();
            listener.Close();
            if (loop is not null) await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or TimeoutException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Embedded server shutdown completed with a non-fatal error");
        }
        finally
        {
            cts?.Dispose();
            _logger.LogInformation("Embedded server stopped");
        }
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    _logger.LogDebug(ex, "HttpListener accept loop ended");
                break;
            }

            _ = Task.Run(() => HandleContextAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            context.Response.Headers["Server"] = "LmimaShopV2";
            var handled = await _api.TryHandleAsync(context, cancellationToken);
            if (!handled) await _staticFiles.ServeAsync(context, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Request aborted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled server error for {Path}", context.Request.Url?.AbsolutePath);
            try
            {
                await HttpUtil.WriteJsonAsync(context.Response, 500,
                    new ErrorResponse("internal_error"), cancellationToken);
            }
            catch (Exception writeError) when (writeError is IOException or HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                _logger.LogDebug(writeError, "Unable to write 500 response");
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException)
            {
                _logger.LogDebug(ex, "Error closing response");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
