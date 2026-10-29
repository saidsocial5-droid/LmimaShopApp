using System.Net;
using LmimaShopV2.Desktop.Config;
using Microsoft.Extensions.Logging;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

/// <summary>Serves the read-only PWA folder located next to the executable.</summary>
public sealed class StaticFileServer
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".webmanifest"] = "application/manifest+json; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".txt"] = "text/plain; charset=utf-8",
        [".map"] = "application/json; charset=utf-8"
    };

    private readonly AppConfigService _config;
    private readonly ILogger<StaticFileServer> _logger;

    public StaticFileServer(AppConfigService config, ILogger<StaticFileServer> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task ServeAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await HttpUtil.WriteTextAsync(response, 405, "Method not allowed", cancellationToken: cancellationToken);
            return;
        }

        var rawPath = request.Url?.AbsolutePath ?? "/";
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(rawPath);
        }
        catch (UriFormatException)
        {
            await HttpUtil.WriteTextAsync(response, 400, "Invalid path", cancellationToken: cancellationToken);
            return;
        }

        if (decoded.Contains("..", StringComparison.Ordinal) || decoded.Contains('\0'))
        {
            await HttpUtil.WriteTextAsync(response, 400, "Invalid path", cancellationToken: cancellationToken);
            return;
        }

        var relative = decoded.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || decoded.EndsWith('/')) relative = string.IsNullOrEmpty(relative) ? "index.html" : relative + "index.html";
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
        {
            await HttpUtil.WriteTextAsync(response, 400, "Invalid path", cancellationToken: cancellationToken);
            return;
        }

        var root = Path.GetFullPath(_config.ResolveMobileFolder());
        if (!Directory.Exists(root))
        {
            var message =
                "Application mobile non installee. Placez les fichiers de la PWA dans le dossier suivant, " +
                $"a cote de l executable : {root}";
            _logger.LogWarning("Mobile folder missing at {Root}", root);
            await HttpUtil.WriteTextAsync(response, 404, message, cancellationToken: cancellationToken);
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(ex, "Invalid static path {Path}", decoded);
            await HttpUtil.WriteTextAsync(response, 400, "Invalid path", cancellationToken: cancellationToken);
            return;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Blocked path traversal attempt: {Path}", decoded);
            await HttpUtil.WriteTextAsync(response, 400, "Invalid path", cancellationToken: cancellationToken);
            return;
        }

        if (!File.Exists(fullPath))
        {
            await HttpUtil.WriteTextAsync(response, 404, "Not found", cancellationToken: cancellationToken);
            return;
        }

        var extension = Path.GetExtension(fullPath);
        response.ContentType = ContentTypes.TryGetValue(extension, out var type)
            ? type
            : "application/octet-stream";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache"
            : "public, max-age=3600";

        var info = new FileInfo(fullPath);
        response.ContentLength64 = info.Length;
        response.StatusCode = 200;
        if (string.Equals(request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)) return;

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, useAsync: true);
        await stream.CopyToAsync(response.OutputStream, cancellationToken);
    }
}
