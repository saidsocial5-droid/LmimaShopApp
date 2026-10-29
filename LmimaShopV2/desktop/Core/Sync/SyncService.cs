using System.Globalization;
using System.Text.Json;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Core.Sync;

public sealed class SyncService : ISyncService
{
    private readonly Db _db;
    private readonly IProductService _products;
    private readonly ICustomerService _customers;
    private readonly ISupplierService _suppliers;
    private readonly ICheckoutService _checkout;
    private readonly IReturnsService _returns;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        Db db,
        IProductService products,
        ICustomerService customers,
        ISupplierService suppliers,
        ICheckoutService checkout,
        IReturnsService returns,
        ILogger<SyncService> logger)
    {
        _db = db;
        _products = products;
        _customers = customers;
        _suppliers = suppliers;
        _checkout = checkout;
        _returns = returns;
        _logger = logger;
    }

    public async Task<SyncResponse> GetDeltaAsync(string? since, CancellationToken cancellationToken = default)
    {
        var sinceValue = DateTime.MinValue;
        if (!string.IsNullOrWhiteSpace(since) &&
            !DateTime.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.None, out sinceValue))
            throw new BusinessRuleException("Curseur de synchronisation invalide.");

        var cursor = await _db.ExecuteScalarAsync<DateTime>(
            "SELECT NOW(3)", cancellationToken: cancellationToken);
        var filters = new { since = sinceValue, cursor };

        var products = await _db.QueryAsync<ProductSyncDto>(
            """
            SELECT p.Uuid, p.Barcode, p.Name, p.CategoryId, c.Name AS Category, p.CostPrice, p.SellPrice,
                   p.StockQuantity AS StockQty, p.TvaRate, p.IsWeighed, p.ImagePath, p.UpdatedAt, p.DeletedAt, p.Version
            FROM products p LEFT JOIN categories c ON c.Id = p.CategoryId
            WHERE p.UpdatedAt > @since AND p.UpdatedAt <= @cursor
            ORDER BY p.UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var categories = await _db.QueryAsync<CategorySyncDto>(
            """
            SELECT Uuid, Name, UpdatedAt, DeletedAt, Version FROM categories
            WHERE UpdatedAt > @since AND UpdatedAt <= @cursor ORDER BY UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var customers = await _db.QueryAsync<CustomerSyncDto>(
            """
            SELECT Uuid, FullName, Phone, Address, CreditLimit, DueBalance, LoyaltyPoints, Notes, UpdatedAt, DeletedAt, Version
            FROM customers
            WHERE UpdatedAt > @since AND UpdatedAt <= @cursor ORDER BY UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var payments = await _db.QueryAsync<CustomerPaymentSyncDto>(
            """
            SELECT cp.Uuid, c.Uuid AS CustomerUuid, cp.Amount, cp.Method, cp.Notes, cp.CreatedAt, cp.UpdatedAt, cp.DeletedAt, cp.Version
            FROM customer_payments cp JOIN customers c ON c.Id = cp.CustomerId
            WHERE cp.UpdatedAt > @since AND cp.UpdatedAt <= @cursor ORDER BY cp.UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var suppliers = await _db.QueryAsync<SupplierSyncDto>(
            """
            SELECT Uuid, Name, Phone, Address, Notes, UpdatedAt, DeletedAt, Version FROM suppliers
            WHERE UpdatedAt > @since AND UpdatedAt <= @cursor ORDER BY UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var sales = await _db.QueryAsync<SaleSyncDto>(
            """
            SELECT s.Uuid, s.InvoiceNo, c.Uuid AS CustomerUuid, s.CashierName, s.Subtotal, s.DiscountPct,
                   s.DiscountAmount, s.TvaAmount, s.Total, s.PaidAmount, s.ChangeAmount, s.DueAmount,
                   s.LoyaltyEarned, s.CreatedAt, s.UpdatedAt, s.DeletedAt, s.Version
            FROM sales s LEFT JOIN customers c ON c.Id = s.CustomerId
            WHERE s.UpdatedAt > @since AND s.UpdatedAt <= @cursor ORDER BY s.UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var saleItems = await _db.QueryAsync<SaleItemSyncDto>(
            """
            SELECT si.Uuid, s.Uuid AS SaleUuid, p.Uuid AS ProductUuid, si.ProductName, si.Qty, si.UnitPrice,
                   si.DiscountPct, si.LineTotal, si.UpdatedAt, si.DeletedAt, si.Version
            FROM sale_items si
            JOIN sales s ON s.Id = si.SaleId
            JOIN products p ON p.Id = si.ProductId
            WHERE si.UpdatedAt > @since AND si.UpdatedAt <= @cursor ORDER BY si.UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        var returns = await _db.QueryAsync<ReturnSyncDto>(
            """
            SELECT r.Uuid, s.Uuid AS SaleUuid, r.Reason, r.RefundTotal, r.CashRefund, r.DueReduction,
                   r.CreatedAt, r.UpdatedAt, r.DeletedAt, r.Version
            FROM returns r JOIN sales s ON s.Id = r.SaleId
            WHERE r.UpdatedAt > @since AND r.UpdatedAt <= @cursor ORDER BY r.UpdatedAt
            """,
            filters, cancellationToken: cancellationToken);

        return new SyncResponse
        {
            Cursor = Db.FormatTimestamp(cursor),
            Products = products.Select(Wrap).ToList(),
            Categories = categories.Select(Wrap).ToList(),
            Customers = customers.Select(Wrap).ToList(),
            CustomerPayments = payments.Select(Wrap).ToList(),
            Suppliers = suppliers.Select(Wrap).ToList(),
            Sales = sales.Select(Wrap).ToList(),
            SaleItems = saleItems.Select(Wrap).ToList(),
            Returns = returns.Select(Wrap).ToList()
        };
    }

    public async Task<SyncPushResponse> PushAsync(
        SyncPushRequest request,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = new SyncPushResponse();
        if (request.Ops.Count > 500) throw new BusinessRuleException("Trop d operations dans un meme lot.");

        foreach (var op in request.Ops)
        {
            var result = new SyncOpResult { OpId = op.OpId ?? string.Empty };
            try
            {
                if (!Guid.TryParse(op.OpId, out _))
                    throw new BusinessRuleException("op_id invalide.");
                if (string.IsNullOrWhiteSpace(op.Type))
                    throw new BusinessRuleException("type d operation manquant.");
                if (op.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    throw new BusinessRuleException("payload manquant.");

                var duplicate = await IsProcessedAsync(op.OpId, cancellationToken);
                if (duplicate)
                {
                    result.Status = "duplicate";
                    result.Message = "Operation deja traitee.";
                    response.Results.Add(result);
                    continue;
                }

                switch (op.Type)
                {
                    case SyncOpTypes.SaleCreate:
                        await ApplySaleAsync(op, actor, cancellationToken);
                        break;
                    case SyncOpTypes.ReturnCreate:
                        await ApplyReturnAsync(op, actor, cancellationToken);
                        break;
                    default:
                        await ApplyCatalogOpAsync(op, actor, cancellationToken);
                        break;
                }

                result.Status = "applied";
                result.Message = null;
            }
            catch (DomainException ex)
            {
                result.Status = "error";
                result.Message = ex.Message;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid payload for sync operation {OpId}", op.OpId);
                result.Status = "error";
                result.Message = "Payload JSON invalide.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error applying sync operation {OpId} ({Type})", op.OpId, op.Type);
                result.Status = "error";
                result.Message = "Erreur interne lors du traitement de l operation.";
            }

            response.Results.Add(result);
        }

        return response;
    }

    private async Task ApplyCatalogOpAsync(SyncOperation op, string? actor, CancellationToken cancellationToken)
    {
        var began = await TryBeginOperationAsync(op.OpId, op.Type, cancellationToken);
        if (!began)
            throw new BusinessRuleException("Operation deja traitee.");

        try
        {
            switch (op.Type)
            {
                case SyncOpTypes.ProductSave:
                {
                    var dto = Deserialize<ProductPushDto>(op.Payload);
                    var categoryId = dto.CategoryId;
                    if (categoryId is null or <= 0 && !string.IsNullOrWhiteSpace(dto.Category))
                        categoryId = await _products.EnsureCategoryAsync(dto.Category!, actor, cancellationToken);
                    await _products.SaveAsync(new Product
                    {
                        Uuid = dto.Uuid,
                        Barcode = dto.Barcode,
                        Name = dto.Name,
                        CategoryId = categoryId,
                        CostPrice = dto.CostPrice,
                        SellPrice = dto.SellPrice,
                        StockQuantity = dto.StockQty,
                        TvaRate = dto.TvaRate,
                        IsWeighed = dto.IsWeighed,
                        ImagePath = dto.ImagePath
                    }, actor, cancellationToken);
                    break;
                }
                case SyncOpTypes.ProductDelete:
                    await _products.DeleteAsync(Deserialize<UuidPayload>(op.Payload).Uuid, actor, cancellationToken);
                    break;
                case SyncOpTypes.CustomerSave:
                {
                    var dto = Deserialize<CustomerPushDto>(op.Payload);
                    await _customers.SaveAsync(new Customer
                    {
                        Uuid = dto.Uuid,
                        FullName = dto.FullName,
                        Phone = dto.Phone,
                        Address = dto.Address,
                        CreditLimit = dto.CreditLimit,
                        Notes = dto.Notes
                    }, actor, cancellationToken);
                    break;
                }
                case SyncOpTypes.CustomerDelete:
                    await _customers.DeleteAsync(Deserialize<UuidPayload>(op.Payload).Uuid, actor, cancellationToken);
                    break;
                case SyncOpTypes.CustomerPaymentAdd:
                {
                    var dto = Deserialize<CustomerPaymentPushDto>(op.Payload);
                    await _customers.AddPaymentAsync(dto.CustomerUuid, dto.Amount, dto.Method, dto.Notes,
                        userId: null, actor, cancellationToken);
                    break;
                }
                case SyncOpTypes.SupplierSave:
                {
                    var dto = Deserialize<SupplierPushDto>(op.Payload);
                    await _suppliers.SaveAsync(new Supplier
                    {
                        Uuid = dto.Uuid,
                        Name = dto.Name,
                        Phone = dto.Phone,
                        Address = dto.Address,
                        Notes = dto.Notes
                    }, actor, cancellationToken);
                    break;
                }
                case SyncOpTypes.SupplierDelete:
                    await _suppliers.DeleteAsync(Deserialize<UuidPayload>(op.Payload).Uuid, actor, cancellationToken);
                    break;
                default:
                    throw new BusinessRuleException($"Type d operation inconnu: {op.Type}");
            }

            await CompleteOperationAsync(op.OpId, null, cancellationToken);
        }
        catch
        {
            await AbortOperationAsync(op.OpId, cancellationToken);
            throw;
        }
    }

    private async Task ApplySaleAsync(SyncOperation op, string? actor, CancellationToken cancellationToken)
    {
        var request = Deserialize<CheckoutRequest>(op.Payload);
        request.OpId = op.OpId;
        var cashier = string.IsNullOrWhiteSpace(request.Cashier) ? actor ?? "Mobile" : request.Cashier!;
        await _checkout.CheckoutAsync(request, new CheckoutContext(cashier, null), cancellationToken);
    }

    private async Task ApplyReturnAsync(SyncOperation op, string? actor, CancellationToken cancellationToken)
    {
        var request = Deserialize<ReturnRequest>(op.Payload);
        request.OpId = op.OpId;
        var cashier = string.IsNullOrWhiteSpace(request.Cashier) ? actor ?? "Mobile" : request.Cashier!;
        await _returns.ReturnAsync(request, new ReturnContext(cashier, null), cancellationToken);
    }

    private static T Deserialize<T>(JsonElement payload) where T : new()
    {
        var value = payload.Deserialize<T>(JsonDefaults.Options);
        return value ?? throw new BusinessRuleException("Payload vide.");
    }

    private static SyncRow<T> Wrap<T>(T row)
    {
        var deleted = false;
        if (row is ProductSyncDto p) deleted = p.DeletedAt is not null;
        else if (row is CategorySyncDto c) deleted = c.DeletedAt is not null;
        else if (row is CustomerSyncDto cu) deleted = cu.DeletedAt is not null;
        else if (row is CustomerPaymentSyncDto cp) deleted = cp.DeletedAt is not null;
        else if (row is SupplierSyncDto su) deleted = su.DeletedAt is not null;
        else if (row is SaleSyncDto sa) deleted = sa.DeletedAt is not null;
        else if (row is SaleItemSyncDto si) deleted = si.DeletedAt is not null;
        else if (row is ReturnSyncDto re) deleted = re.DeletedAt is not null;
        return new SyncRow<T> { D = row, Deleted = deleted };
    }

    private async Task<bool> IsProcessedAsync(string opId, CancellationToken cancellationToken) =>
        await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM processed_operations WHERE OpId = @opId",
            new { opId }, cancellationToken: cancellationToken) > 0;

    private async Task<bool> TryBeginOperationAsync(string opId, string opType, CancellationToken cancellationToken)
    {
        try
        {
            await _db.ExecuteAsync(
                "INSERT INTO processed_operations (OpId, OpType, CreatedAt) VALUES (@opId, @opType, NOW(3))",
                new { opId, opType }, cancellationToken: cancellationToken);
            return true;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return false;
        }
    }

    private Task CompleteOperationAsync(string opId, string? resultJson, CancellationToken cancellationToken) =>
        _db.ExecuteAsync(
            "UPDATE processed_operations SET ResultJson = @resultJson, CompletedAt = NOW(3) WHERE OpId = @opId",
            new { opId, resultJson }, cancellationToken: cancellationToken);

    private Task AbortOperationAsync(string opId, CancellationToken cancellationToken) =>
        _db.ExecuteAsync(
            "DELETE FROM processed_operations WHERE OpId = @opId AND ResultJson IS NULL",
            new { opId }, cancellationToken: cancellationToken);
}
