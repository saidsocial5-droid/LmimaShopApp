namespace LmimaShopV2.Desktop.Core.Models;

public sealed class SaleDetail
{
    public string Uuid { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ReturnableLine> Items { get; set; } = [];
}

public sealed class ReturnableLine
{
    public string SaleItemUuid { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal ReturnedQty { get; set; }

    public decimal ReturnableQty => Math.Max(0m, Qty - ReturnedQty);
    public bool CanReturn => ReturnableQty > 0m;
}

public sealed class ReturnListItem
{
    public string Uuid { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public decimal RefundTotal { get; set; }
    public decimal CashRefund { get; set; }
    public decimal DueReduction { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
