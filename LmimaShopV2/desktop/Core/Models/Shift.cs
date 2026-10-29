namespace LmimaShopV2.Desktop.Core.Models;

public sealed class Shift
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal CreditSales { get; set; }
    public decimal DebtPaymentsCash { get; set; }
    public decimal ReturnsRefund { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Difference { get; set; }
    public decimal TvaCollected { get; set; }
    public int LoyaltyGranted { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }

    public bool IsOpen => ClosedAt is null;
}

public sealed class ShiftReport
{
    public long ShiftId { get; set; }
    public string ShiftUuid { get; set; } = string.Empty;
    public string CashierName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningFloat { get; set; }
    public int SalesCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal CreditSales { get; set; }
    public decimal DebtPaymentsCash { get; set; }
    public decimal ReturnsRefund { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Difference { get; set; }
    public decimal TvaCollected { get; set; }
    public int LoyaltyGranted { get; set; }
    public string? Notes { get; set; }

    public bool IsOpen => ClosedAt is null;
}

public sealed class DailyReport
{
    public DateTime Date { get; set; }
    public int SalesCount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Tva { get; set; }
    public decimal Cash { get; set; }
    public decimal Card { get; set; }
    public decimal Credit { get; set; }
    public decimal Returns { get; set; }
    public decimal DebtPayments { get; set; }
    public List<TopProduct> TopProducts { get; set; } = [];
}

public sealed class TopProduct
{
    public string Name { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal Revenue { get; set; }
}
