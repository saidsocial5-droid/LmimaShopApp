using System.Text.Json.Serialization;
using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(PairRequest))]
[JsonSerializable(typeof(PairResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(CheckoutRequestDto))]
[JsonSerializable(typeof(CheckoutResponseDto))]
[JsonSerializable(typeof(ReturnRequestDto))]
[JsonSerializable(typeof(ReturnResponseDto))]
[JsonSerializable(typeof(BarcodeResponseDto))]
[JsonSerializable(typeof(StatementResponseDto))]
[JsonSerializable(typeof(StatementEntryDto))]
[JsonSerializable(typeof(SyncResponse))]
[JsonSerializable(typeof(SyncPushRequest))]
[JsonSerializable(typeof(SyncPushResponse))]
public partial class ApiJsonContext : JsonSerializerContext
{
}
