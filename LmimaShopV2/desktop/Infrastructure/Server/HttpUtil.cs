using System.Net;
using System.Text;
using System.Text.Json;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

public sealed class PayloadTooLargeException : Exception
{
    public PayloadTooLargeException() : base("Request body too large.")
    {
    }
}

public static class HttpUtil
{
    public const long MaxBodyBytes = 1_048_576;
    private static readonly byte[] Empty = [];

    public static Dictionary<string, string> ParseQuery(string? rawQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(rawQuery)) return result;
        var query = rawQuery.StartsWith('?') ? rawQuery[1..] : rawQuery;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx < 0 ? pair : pair[..idx];
            var value = idx < 0 ? string.Empty : pair[(idx + 1)..];
            key = Decode(key);
            value = Decode(value);
            if (!string.IsNullOrEmpty(key)) result[key] = value;
        }

        return result;
    }

    public static async Task<string> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength64 > MaxBodyBytes) throw new PayloadTooLargeException();
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = await request.InputStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxBodyBytes) throw new PayloadTooLargeException();
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    public static async Task WriteJsonAsync<T>(HttpListenerResponse response, int statusCode, T value,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, typeof(T), ApiJsonContext.Default);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    public static async Task WriteTextAsync(HttpListenerResponse response, int statusCode, string text,
        string contentType = "text/plain; charset=utf-8", CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    public static T? Deserialize<T>(string json)
    {
        return (T?)JsonSerializer.Deserialize(json, typeof(T), ApiJsonContext.Default);
    }

    public static string NormalizeDeviceName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Appareil mobile";
        var cleaned = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0) return "Appareil mobile";
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    public static byte[] EmptyBytes => Empty;
}
