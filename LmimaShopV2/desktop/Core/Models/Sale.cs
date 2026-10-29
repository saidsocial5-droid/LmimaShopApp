using System.Text.Json.Serialization;

namespace LmimaShopV2.Desktop.Core.Models;

public sealed class Sale
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public long? UserId { get; set; }
    public long ShiftId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TvaAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    public decimal DueAmount { get; set; }
    public int LoyaltyEarned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SaleItem
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public long SaleId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal LineTotal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class SalePayment
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public string Method { get; set; } = "cash";
    public decimal Amount { get; set; }
}

public sealed class CheckoutItem
{
    public string ProductUuid { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
}

public sealed class CheckoutPayment
{
    public string Method { get; set; } = "cash";
    public decimal Amount { get; set; }
}

public sealed class CheckoutRequest
{
    public string OpId { get; set; } = string.Empty;
    public string? CustomerUuid { get; set; }
    public List<CheckoutItem> Items { get; set; } = [];
    public List<CheckoutPayment> Payments { get; set; } = [];
    public decimal DiscountPct { get; set; }
    public string? Cashier { get; set; }
}

public sealed record CheckoutContext(string CashierName, long? UserId);

public sealed class CheckoutResult
{
    public string SaleUuid { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Tva { get; set; }
    public decimal Paid { get; set; }
    public decimal Change { get; set; }
    public decimal Due { get; set; }
    public int LoyaltyEarned { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore] public long SaleId { get; set; }
    [JsonIgnore] public long? CustomerId { get; set; }
    [JsonIgnore] public string? CustomerName { get; set; }
    [JsonIgnore] public decimal DiscountAmount { get; set; }
    [JsonIgnore] public List<CheckoutLineResult> Lines { get; set; } = [];
    [JsonIgnore] public List<CheckoutPayment> Payments { get; set; } = [];
}

public sealed class CheckoutLineResult
{
    public string Name { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal LineTotal { get; set; }
}
