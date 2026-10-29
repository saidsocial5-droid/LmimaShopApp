using System.Text.Json;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class ReturnsService : IReturnsService
{
    private const string OpType = "return_create";
    private readonly Db _db;
    private readonly IAuditLogger _audit;
    private readonly ISseHub _sse;
    private readonly ILogger<ReturnsService> _logger;

    public ReturnsService(Db db, IAuditLogger audit, ISseHub sse, ILogger<ReturnsService> logger)
    {
        _db = db;
        _audit = audit;
        _sse = sse;
        _logger = logger;
    }

    public async Task<ReturnResult> ReturnAsync(
        ReturnRequest request,
        ReturnContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.Items.Count == 0) throw new BusinessRuleException("Aucun article a retourner.");
        if (request.Items.Count > 500) throw new BusinessRuleException("Trop de lignes de retour.");

        var opId = string.IsNullOrWhiteSpace(request.OpId) ? Guid.NewGuid().ToString() : request.OpId.Trim();
        if (opId.Length > 64) throw new BusinessRuleException("Identifiant operation invalide.");
        var saleUuid = InputValidator.RequireUuid(request.SaleUuid, "Vente");
        var reason = InputValidator.OptionalText(request.Reason, 500);

        var existing = await TryReadExistingAsync(opId, cancellationToken);
        if (existing is not null) return existing;

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var duplicate = await BeginOperationAsync(connection, transaction, opId, cancellationToken);
            if (duplicate is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return duplicate;
            }

            var sale = await connection.QueryFirstOrDefaultAsync<Sale>(new CommandDefinition(
                "SELECT * FROM sales WHERE Uuid = @saleUuid AND DeletedAt IS NULL FOR UPDATE",
                new { saleUuid }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessRuleException("Vente introuvable.");

            var shiftId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT Id FROM cash_register_shifts WHERE ClosedAt IS NULL ORDER BY Id DESC LIMIT 1",
                transaction: transaction, cancellationToken: cancellationToken));

            var returnUuid = Db.NewUuid();
            var lines = new List<(SaleItem Item, decimal Qty, decimal UnitNet, decimal Refund)>();
            var refundTotal = 0m;

            foreach (var requested in request.Items)
            {
                var itemUuid = InputValidator.RequireUuid(requested.SaleItemUuid, "Ligne de vente");
                InputValidator.ValidateQuantity(requested.Qty);

                var item = await connection.QueryFirstOrDefaultAsync<SaleItem>(new CommandDefinition(
                    "SELECT * FROM sale_items WHERE Uuid = @itemUuid AND SaleId = @saleId AND DeletedAt IS NULL LIMIT 1",
                    new { itemUuid, saleId = sale.Id }, transaction, cancellationToken: cancellationToken))
                    ?? throw new BusinessRuleException("Ligne de vente introuvable.");

                var alreadyReturned = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    """
                    SELECT COALESCE(SUM(ri.Qty), 0)
                    FROM return_items ri
                    JOIN returns r ON r.Id = ri.ReturnId
                    WHERE ri.SaleItemId = @itemId AND ri.DeletedAt IS NULL AND r.DeletedAt IS NULL
                    """,
                    new { itemId = item.Id }, transaction, cancellationToken: cancellationToken));

                if (alreadyReturned + requested.Qty > item.Qty + 0.0009m)
                    throw new BusinessRuleException($"Quantite retournee superieure a la quantite vendue pour {item.ProductName}.");

                var unitNet = item.Qty == 0
                    ? 0m
                    : item.LineTotal / item.Qty * (1m - sale.DiscountPct / 100m);
                var lineRefund = MoneyHelper.Round(requested.Qty * unitNet);
                refundTotal = MoneyHelper.Round(refundTotal + lineRefund);
                lines.Add((item, requested.Qty, MoneyHelper.Round(unitNet), lineRefund));

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE products SET StockQuantity = StockQuantity + @qty WHERE Id = @productId",
                    new { qty = requested.Qty, productId = item.ProductId },
                    transaction, cancellationToken: cancellationToken));
            }

            if (refundTotal <= 0) throw new BusinessRuleException("Montant de remboursement invalide.");

            decimal dueReduction = 0m;
            if (sale.CustomerId is not null && sale.DueAmount > 0)
            {
                var alreadyReduced = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(SUM(DueReduction), 0) FROM returns WHERE SaleId = @saleId AND DeletedAt IS NULL",
                    new { saleId = sale.Id }, transaction, cancellationToken: cancellationToken));
                var reducible = Math.Max(0m, sale.DueAmount - alreadyReduced);
                dueReduction = MoneyHelper.Round(Math.Min(refundTotal, reducible));
                if (dueReduction > 0)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE customers SET DueBalance = GREATEST(0, DueBalance - @dueReduction) WHERE Id = @customerId",
                        new { dueReduction, customerId = sale.CustomerId },
                        transaction, cancellationToken: cancellationToken));
                }
            }

            var cashRefund = MoneyHelper.Round(refundTotal - dueReduction);
            var cashierName = string.IsNullOrWhiteSpace(context.CashierName) ? "Caisse" : context.CashierName.Trim();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO returns
                  (Uuid, SaleId, ShiftId, UserId, CashierName, Reason, RefundTotal, CashRefund, DueReduction)
                VALUES
                  (@uuid, @saleId, @shiftId, @userId, @cashier, @reason, @refund, @cashRefund, @dueReduction)
                """,
                new
                {
                    uuid = returnUuid,
                    saleId = sale.Id,
                    shiftId,
                    userId = context.UserId,
                    cashier = cashierName,
                    reason,
                    refund = refundTotal,
                    cashRefund,
                    dueReduction
                },
                transaction, cancellationToken: cancellationToken));

            var returnId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken));

            foreach (var line in lines)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO return_items (Uuid, ReturnId, SaleItemId, ProductId, Qty, UnitNetPrice, LineRefund)
                    VALUES (@uuid, @returnId, @saleItemId, @productId, @qty, @unitNet, @lineRefund)
                    """,
                    new
                    {
                        uuid = Db.NewUuid(),
                        returnId,
                        saleItemId = line.Item.Id,
                        productId = line.Item.ProductId,
                        qty = line.Qty,
                        unitNet = line.UnitNet,
                        lineRefund = line.Refund
                    },
                    transaction, cancellationToken: cancellationToken));
            }

            var result = new ReturnResult
            {
                ReturnUuid = returnUuid,
                RefundTotal = refundTotal,
                CashRefund = cashRefund,
                DueReduction = dueReduction,
                CreatedAt = DateTime.Now
            };

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE processed_operations SET ResultJson = @json, CompletedAt = NOW(3) WHERE OpId = @opId",
                new { json = JsonSerializer.Serialize(result, JsonDefaults.Options), opId },
                transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await _audit.LogAsync("return_created", "returns", returnUuid,
                $"Retour facture {sale.InvoiceNo}, remboursement {refundTotal:0.00} DH (dette {dueReduction:0.00} DH)",
                context.UserId, cashierName, cancellationToken);
            _sse.BroadcastDataChanged();
            return result;
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackError)
                {
                    _logger.LogError(rollbackError, "Return rollback failed for operation {OpId}", opId);
                }
            }

            if (ex is MySqlException { Number: 1062 } && !committed)
            {
                var duplicate = await TryReadExistingAsync(opId, cancellationToken);
                if (duplicate is not null) return duplicate;
            }

            throw;
        }
    }

    private async Task<ReturnResult?> TryReadExistingAsync(string opId, CancellationToken cancellationToken)
    {
        var json = await _db.ExecuteScalarAsync<string>(
            "SELECT ResultJson FROM processed_operations WHERE OpId = @opId AND OpType = @opType LIMIT 1",
            new { opId, opType = OpType }, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ReturnResult>(json, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Corrupt idempotency payload for return {OpId}", opId);
            throw new DomainException("Retour deja traite mais resultat illisible.", ex);
        }
    }

    private static async Task<ReturnResult?> BeginOperationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string opId,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO processed_operations (OpId, OpType, CreatedAt) VALUES (@opId, @opType, NOW(3))",
                new { opId, opType = OpType }, transaction, cancellationToken: cancellationToken));
            return null;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            var json = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT ResultJson FROM processed_operations WHERE OpId = @opId LIMIT 1",
                new { opId }, transaction, cancellationToken: cancellationToken));
            if (string.IsNullOrWhiteSpace(json))
                throw new BusinessRuleException("Operation deja en cours de traitement.");
            return JsonSerializer.Deserialize<ReturnResult>(json, JsonDefaults.Options);
        }
    }
}
