using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Infrastructure.Data;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class ReportService : IReportService
{
    private readonly Db _db;

    public ReportService(Db db) => _db = db;

    public async Task<ShiftReport> GetZReportAsync(long shiftId, CancellationToken cancellationToken = default)
    {
        var shift = await _db.QueryFirstOrDefaultAsync<Shift>(
            """
            SELECT s.*, u.FullName AS CashierName
            FROM cash_register_shifts s JOIN users u ON u.Id = s.UserId
            WHERE s.Id = @shiftId AND s.DeletedAt IS NULL
            """,
            new { shiftId }, cancellationToken: cancellationToken)
            ?? throw new BusinessRuleException("Shift introuvable.");

        var totals = await _db.QueryFirstOrDefaultAsync<SalesTotals>(
            """
            SELECT COUNT(*) AS SalesCount,
                   COALESCE(SUM(Total), 0) AS Revenue,
                   COALESCE(SUM(TvaAmount), 0) AS Tva,
                   COALESCE(SUM(LoyaltyEarned), 0) AS Loyalty,
                   COALESCE(SUM(ChangeAmount), 0) AS ChangeGiven
            FROM sales WHERE ShiftId = @shiftId AND DeletedAt IS NULL
            """,
            new { shiftId }, cancellationToken: cancellationToken) ?? new SalesTotals();

        var paymentRows = await _db.QueryAsync<MethodTotal>(
            """
            SELECT sp.Method, COALESCE(SUM(sp.Amount), 0) AS Amount
            FROM sale_payments sp JOIN sales s ON s.Id = sp.SaleId
            WHERE s.ShiftId = @shiftId AND s.DeletedAt IS NULL
            GROUP BY sp.Method
            """,
            new { shiftId }, cancellationToken: cancellationToken);

        var cashSales = paymentRows.Where(r => r.Method == "cash").Sum(r => r.Amount) - totals.ChangeGiven;
        var cardSales = paymentRows.Where(r => r.Method == "card").Sum(r => r.Amount);
        var creditSales = paymentRows.Where(r => r.Method == "credit").Sum(r => r.Amount);

        var debtPayments = await _db.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(Amount), 0) FROM customer_payments
            WHERE ShiftId = @shiftId AND Method = 'cash' AND DeletedAt IS NULL
            """,
            new { shiftId }, cancellationToken: cancellationToken);

        var refunds = await _db.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(CashRefund), 0) FROM returns
            WHERE ShiftId = @shiftId AND DeletedAt IS NULL
            """,
            new { shiftId }, cancellationToken: cancellationToken);

        var expectedCash = shift.OpeningFloat + cashSales + debtPayments - refunds;

        return new ShiftReport
        {
            ShiftId = shift.Id,
            ShiftUuid = shift.Uuid,
            CashierName = string.IsNullOrWhiteSpace(shift.CashierName) ? "Caisse" : shift.CashierName,
            OpenedAt = shift.OpenedAt,
            ClosedAt = shift.ClosedAt,
            OpeningFloat = shift.OpeningFloat,
            SalesCount = totals.SalesCount,
            TotalRevenue = totals.Revenue,
            CashSales = cashSales,
            CardSales = cardSales,
            CreditSales = creditSales,
            DebtPaymentsCash = debtPayments,
            ReturnsRefund = refunds,
            ExpectedCash = expectedCash,
            CountedCash = shift.CountedCash,
            Difference = shift.CountedCash is null ? null : shift.CountedCash.Value - expectedCash,
            TvaCollected = totals.Tva,
            LoyaltyGranted = totals.Loyalty,
            Notes = shift.Notes
        };
    }

    public async Task<DailyReport> GetDailyReportAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var start = date.Date;
        var end = start.AddDays(1);

        var totals = await _db.QueryFirstOrDefaultAsync<SalesTotals>(
            """
            SELECT COUNT(*) AS SalesCount,
                   COALESCE(SUM(Total), 0) AS Revenue,
                   COALESCE(SUM(TvaAmount), 0) AS Tva,
                   COALESCE(SUM(LoyaltyEarned), 0) AS Loyalty,
                   COALESCE(SUM(ChangeAmount), 0) AS ChangeGiven
            FROM sales WHERE CreatedAt >= @start AND CreatedAt < @end AND DeletedAt IS NULL
            """,
            new { start, end }, cancellationToken: cancellationToken) ?? new SalesTotals();

        var paymentRows = await _db.QueryAsync<MethodTotal>(
            """
            SELECT sp.Method, COALESCE(SUM(sp.Amount), 0) AS Amount
            FROM sale_payments sp JOIN sales s ON s.Id = sp.SaleId
            WHERE s.CreatedAt >= @start AND s.CreatedAt < @end AND s.DeletedAt IS NULL
            GROUP BY sp.Method
            """,
            new { start, end }, cancellationToken: cancellationToken);

        var refunds = await _db.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(RefundTotal), 0) FROM returns WHERE CreatedAt >= @start AND CreatedAt < @end AND DeletedAt IS NULL",
            new { start, end }, cancellationToken: cancellationToken);

        var debtPayments = await _db.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(Amount), 0) FROM customer_payments
            WHERE CreatedAt >= @start AND CreatedAt < @end AND Method = 'cash' AND DeletedAt IS NULL
            """,
            new { start, end }, cancellationToken: cancellationToken);

        var top = await _db.QueryAsync<TopProduct>(
            """
            SELECT si.ProductName AS Name, COALESCE(SUM(si.Qty), 0) AS Qty, COALESCE(SUM(si.LineTotal), 0) AS Revenue
            FROM sale_items si JOIN sales s ON s.Id = si.SaleId
            WHERE s.CreatedAt >= @start AND s.CreatedAt < @end AND s.DeletedAt IS NULL AND si.DeletedAt IS NULL
            GROUP BY si.ProductName
            ORDER BY Revenue DESC
            LIMIT 10
            """,
            new { start, end }, cancellationToken: cancellationToken);

        return new DailyReport
        {
            Date = start,
            SalesCount = totals.SalesCount,
            Revenue = totals.Revenue,
            Tva = totals.Tva,
            Cash = paymentRows.Where(r => r.Method == "cash").Sum(r => r.Amount) - totals.ChangeGiven,
            Card = paymentRows.Where(r => r.Method == "card").Sum(r => r.Amount),
            Credit = paymentRows.Where(r => r.Method == "credit").Sum(r => r.Amount),
            Returns = refunds,
            DebtPayments = debtPayments,
            TopProducts = top
        };
    }

    private sealed class SalesTotals
    {
        public int SalesCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal Tva { get; set; }
        public int Loyalty { get; set; }
        public decimal ChangeGiven { get; set; }
    }

    private sealed class MethodTotal
    {
        public string Method { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
