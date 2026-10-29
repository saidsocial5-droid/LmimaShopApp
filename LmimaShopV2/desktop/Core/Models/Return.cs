namespace LmimaShopV2.Desktop.Core.Models;

public sealed class SaleReturn
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public long SaleId { get; set; }
    public long? ShiftId { get; set; }
    public long? UserId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public decimal RefundTotal { get; set; }
    public decimal CashRefund { get; set; }
    public decimal DueReduction { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class ReturnItem
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public long ReturnId { get; set; }
    public long SaleItemId { get; set; }
    public long ProductId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitNetPrice { get; set; }
    public decimal LineRefund { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class ReturnLineRequest
{
    public string SaleItemUuid { get; set; } = string.Empty;
    public decimal Qty { get; set; }
}

public sealed class ReturnRequest
{
    public string OpId { get; set; } = string.Empty;
    public string SaleUuid { get; set; } = string.Empty;
    public List<ReturnLineRequest> Items { get; set; } = [];
    public string? Reason { get; set; }
    public string? Cashier { get; set; }
}

public sealed record ReturnContext(string CashierName, long? UserId);

public sealed class ReturnResult
{
    public string ReturnUuid { get; set; } = string.Empty;
    public decimal RefundTotal { get; set; }
    public decimal CashRefund { get; set; }
    public decimal DueReduction { get; set; }
    public DateTime CreatedAt { get; set; }
}
