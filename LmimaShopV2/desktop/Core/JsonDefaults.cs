using System.Text.Json;
using System.Text.Json.Serialization;

namespace LmimaShopV2.Desktop.Core;

/// <summary>JSON options used for local persistence (idempotency ledger, etc.).</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
