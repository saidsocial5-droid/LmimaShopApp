using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

public sealed class PingResponse
{
    public string App { get; set; } = "LmimaShopV2";
    public string Version { get; set; } = "2.0.0";
}

public sealed class PairRequest
{
    public string? Code { get; set; }
}

public sealed class PairResponse
{
    public long DeviceId { get; set; }
    public string Token { get; set; } = string.Empty;
}

public sealed class ErrorResponse
{
    public string Error { get; set; } = string.Empty;

    public ErrorResponse()
    {
    }

    public ErrorResponse(string error) => Error = error;
}

public sealed class CheckoutItemDto
{
    public string ProductUuid { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
}

public sealed class CheckoutPaymentDto
{
    public string Method { get; set; } = "cash";
    public decimal Amount { get; set; }
}

public sealed class CheckoutRequestDto
{
    public string? OpId { get; set; }
    public string? Cashier { get; set; }
    public string? CustomerUuid { get; set; }
    public List<CheckoutItemDto> Items { get; set; } = [];
    public List<CheckoutPaymentDto> Payments { get; set; } = [];
    public decimal DiscountPct { get; set; }
}

public sealed class CheckoutResponseDto
{
    public string SaleUuid { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Tva { get; set; }
    public decimal Paid { get; set; }
    public decimal Change { get; set; }
    public decimal Due { get; set; }
    public int LoyaltyEarned { get; set; }
}

public sealed class ReturnItemDto
{
    public string SaleItemUuid { get; set; } = string.Empty;
    public decimal Qty { get; set; }
}

public sealed class ReturnRequestDto
{
    public string? OpId { get; set; }
    public string? SaleUuid { get; set; }
    public List<ReturnItemDto> Items { get; set; } = [];
    public string? Reason { get; set; }
    public string? Cashier { get; set; }
}

public sealed class ReturnResponseDto
{
    public string ReturnUuid { get; set; } = string.Empty;
    public decimal RefundTotal { get; set; }
}

public sealed class BarcodeResponseDto
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal SellPrice { get; set; }
    public decimal StockQty { get; set; }
    public decimal TvaRate { get; set; }
    public string? Category { get; set; }
    public bool IsWeighed { get; set; }
}

public sealed class StatementEntryDto
{
    public string Date { get; set; } = string.Empty;
    public string Type { get; set; } = "sale";
    public string Ref { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
}

public sealed class StatementResponseDto
{
    public CustomerSyncDto Customer { get; set; } = new();
    public decimal OpeningBalance { get; set; }
    public List<StatementEntryDto> Entries { get; set; } = [];
}
