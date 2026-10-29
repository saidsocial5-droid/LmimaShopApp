using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;

namespace LmimaShopV2.Desktop.Core.Services;

/// <summary>Read-only sale lookups used by the returns screen.</summary>
public sealed class SalesQueryService : ISalesService
{
    private readonly Db _db;

    public SalesQueryService(Db db) => _db = db;

    public Task<List<SaleDetail>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        QueryAsync(null, Math.Clamp(limit, 1, 200), cancellationToken);

    public async Task<List<SaleDetail>> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        var normalized = InputValidator.RequireText(term, "رقم الفاتورة أو اسم العميل", 120);
        return await QueryAsync(normalized, 100, cancellationToken);
    }

    public async Task<SaleDetail?> GetDetailAsync(string saleUuid, CancellationToken cancellationToken = default)
    {
        var uuid = InputValidator.RequireUuid(saleUuid, "Vente");
        var sales = await _db.QueryAsync<SaleDetail>(
            """
            SELECT s.Uuid, s.InvoiceNo, c.FullName AS CustomerName, s.CashierName,
                   s.Total, s.PaidAmount, s.DueAmount, s.CreatedAt
            FROM sales s
            LEFT JOIN customers c ON c.Id = s.CustomerId
            WHERE s.Uuid = @uuid AND s.DeletedAt IS NULL
            LIMIT 1
            """,
            new { uuid }, cancellationToken: cancellationToken);

        var sale = sales.FirstOrDefault();
        if (sale is null) return null;
        sale.Items = await GetLinesAsync(uuid, cancellationToken);
        return sale;
    }

    public async Task<List<ReturnListItem>> GetRecentReturnsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        await _db.QueryAsync<ReturnListItem>(
            """
            SELECT r.Uuid, s.InvoiceNo, c.FullName AS CustomerName, r.CashierName,
                   r.RefundTotal, r.CashRefund, r.DueReduction, r.Reason, r.CreatedAt
            FROM returns r
            JOIN sales s ON s.Id = r.SaleId
            LEFT JOIN customers c ON c.Id = s.CustomerId
            WHERE r.DeletedAt IS NULL
            ORDER BY r.Id DESC
            LIMIT @limit
            """,
            new { limit = Math.Clamp(limit, 1, 200) }, cancellationToken: cancellationToken);

    private async Task<List<SaleDetail>> QueryAsync(string? term, int limit, CancellationToken cancellationToken)
    {
        var like = term is null ? null : $"%{term}%";
        var sales = await _db.QueryAsync<SaleDetail>(
            """
            SELECT s.Uuid, s.InvoiceNo, c.FullName AS CustomerName, s.CashierName,
                   s.Total, s.PaidAmount, s.DueAmount, s.CreatedAt
            FROM sales s
            LEFT JOIN customers c ON c.Id = s.CustomerId
            WHERE s.DeletedAt IS NULL
              AND (@like IS NULL OR s.InvoiceNo LIKE @like OR c.FullName LIKE @like)
            ORDER BY s.Id DESC
            LIMIT @limit
            """,
            new { like, limit }, cancellationToken: cancellationToken);

        foreach (var sale in sales)
            sale.Items = await GetLinesAsync(sale.Uuid, cancellationToken);
        return sales;
    }

    private async Task<List<ReturnableLine>> GetLinesAsync(string saleUuid, CancellationToken cancellationToken) =>
        await _db.QueryAsync<ReturnableLine>(
            """
            SELECT si.Uuid AS SaleItemUuid, si.ProductName, si.Qty, si.UnitPrice, si.LineTotal,
                   COALESCE(SUM(CASE WHEN ri.DeletedAt IS NULL THEN ri.Qty ELSE 0 END), 0) AS ReturnedQty
            FROM sale_items si
            LEFT JOIN return_items ri ON ri.SaleItemId = si.Id
            WHERE si.SaleId = (SELECT Id FROM sales WHERE Uuid = @saleUuid LIMIT 1)
              AND si.DeletedAt IS NULL
            GROUP BY si.Uuid, si.ProductName, si.Qty, si.UnitPrice, si.LineTotal
            ORDER BY si.Id
            """,
            new { saleUuid }, cancellationToken: cancellationToken);
}
