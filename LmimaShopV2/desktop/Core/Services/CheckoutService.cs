using System.Text.Json;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Core.Services;

/// <summary>
/// The one and only checkout implementation. Used by the POS UI, the mobile API and delta sync.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private const string OpType = "sale_create";
    private readonly Db _db;
    private readonly ISettingsService _settings;
    private readonly IAuditLogger _audit;
    private readonly ISseHub _sse;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(
        Db db,
        ISettingsService settings,
        IAuditLogger audit,
        ISseHub sse,
        ILogger<CheckoutService> logger)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
        _sse = sse;
        _logger = logger;
    }

    public async Task<CheckoutResult> CheckoutAsync(
        CheckoutRequest request,
        CheckoutContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.Items.Count == 0) throw new BusinessRuleException("Le panier est vide.");
        if (request.Items.Count > 500) throw new BusinessRuleException("Trop de lignes dans le panier.");

        var opId = string.IsNullOrWhiteSpace(request.OpId) ? Guid.NewGuid().ToString() : request.OpId.Trim();
        if (opId.Length > 64) throw new BusinessRuleException("Identifiant operation invalide.");

        var existing = await TryReadExistingAsync(opId, cancellationToken);
        if (existing is not null) return existing;

        var store = await _settings.GetStoreSettingsAsync(cancellationToken);
        var tvaRate = store.TvaRate;
        var loyaltyDivisor = store.LoyaltyMadPerPoint;

        var customerUuid = InputValidator.OptionalUuid(request.CustomerUuid, "Client");
        InputValidator.ValidatePercentage(request.DiscountPct);

        var requestedLines = new List<(string Uuid, decimal Qty, decimal UnitPrice, decimal DiscountPct, decimal LineTotal)>();
        foreach (var item in request.Items)
        {
            var uuid = InputValidator.RequireUuid(item.ProductUuid, "Produit");
            InputValidator.ValidateQuantity(item.Qty);
            if (item.UnitPrice < 0) throw new BusinessRuleException("Prix unitaire invalide.");
            InputValidator.ValidatePercentage(item.DiscountPct);
            var lineTotal = MoneyHelper.Round(item.Qty * item.UnitPrice * (1m - item.DiscountPct / 100m));
            requestedLines.Add((uuid, item.Qty, item.UnitPrice, item.DiscountPct, lineTotal));
        }

        var subtotal = MoneyHelper.Round(requestedLines.Sum(l => l.LineTotal));
        var discountAmount = MoneyHelper.Round(subtotal * request.DiscountPct / 100m);
        var total = MoneyHelper.Round(subtotal - discountAmount);
        if (total <= 0) throw new BusinessRuleException("Le total de la vente doit etre superieur a zero.");

        var payments = NormalizePayments(request.Payments);
        var cash = MoneyHelper.Round(payments.Where(p => p.Method == "cash").Sum(p => p.Amount));
        var card = MoneyHelper.Round(payments.Where(p => p.Method == "card").Sum(p => p.Amount));
        var credit = MoneyHelper.Round(payments.Where(p => p.Method == "credit").Sum(p => p.Amount));

        if (card > total + 0.009m)
            throw new BusinessRuleException("Le paiement par carte ne peut pas depasser le total.");
        var expectedDue = MoneyHelper.Round(Math.Max(0m, total - cash - card));
        if (credit > expectedDue + 0.009m)
            throw new BusinessRuleException("Le montant a credit depasse le reste a payer.");
        if (credit < expectedDue - 0.009m)
            throw new BusinessRuleException("Le montant encaisse est insuffisant.");
        var due = expectedDue;
        var change = MoneyHelper.Round(Math.Max(0m, cash - Math.Max(0m, total - card - due)));
        var paid = MoneyHelper.Round(total - due);
        if (paid < 0) throw new BusinessRuleException("Paiement invalide.");

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

            var shift = await connection.QueryFirstOrDefaultAsync<Shift>(new CommandDefinition(
                "SELECT * FROM cash_register_shifts WHERE ClosedAt IS NULL ORDER BY Id DESC LIMIT 1 FOR UPDATE",
                transaction: transaction, cancellationToken: cancellationToken));
            if (shift is null)
                throw new BusinessRuleException("Aucun shift ouvert. Ouvrez la caisse avant de vendre.");

            Customer? customer = null;
            if (customerUuid is not null)
            {
                customer = await connection.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(
                    "SELECT * FROM customers WHERE Uuid = @customerUuid AND DeletedAt IS NULL FOR UPDATE",
                    new { customerUuid }, transaction, cancellationToken: cancellationToken))
                    ?? throw new BusinessRuleException("Client introuvable.");
            }

            if (due > 0 && customer is null)
                throw new BusinessRuleException("Une vente a credit necessite un client.");
            if (due > 0 && customer!.CreditLimit > 0 && customer.DueBalance + due > customer.CreditLimit + 0.009m)
                throw new BusinessRuleException(
                    $"Limite de credit depassee (disponible: {customer.CreditLimit - customer.DueBalance:0.00} DH).");

            var lines = new List<(Product Product, decimal Qty, decimal UnitPrice, decimal DiscountPct, decimal LineTotal)>();
            foreach (var requested in requestedLines)
            {
                var product = await connection.QueryFirstOrDefaultAsync<Product>(new CommandDefinition(
                    "SELECT * FROM products WHERE Uuid = @uuid AND DeletedAt IS NULL LIMIT 1",
                    new { uuid = requested.Uuid }, transaction, cancellationToken: cancellationToken))
                    ?? throw new BusinessRuleException("Produit introuvable dans le panier.");

                var affected = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE products
                    SET StockQuantity = StockQuantity - @qty
                    WHERE Id = @id AND StockQuantity >= @qty AND DeletedAt IS NULL
                    """,
                    new { qty = requested.Qty, id = product.Id },
                    transaction, cancellationToken: cancellationToken));
                if (affected == 0)
                    throw new BusinessRuleException($"Stock insuffisant pour {product.Name}.");

                lines.Add((product, requested.Qty, requested.UnitPrice, requested.DiscountPct, requested.LineTotal));
            }

            var counterAffected = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE counters SET Value = LAST_INSERT_ID(Value + 1) WHERE Name = 'invoice'",
                transaction: transaction, cancellationToken: cancellationToken));
            if (counterAffected == 0) throw new DomainException("Compteur de facture introuvable.");
            var sequence = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken));
            var invoiceNo = $"INV-{DateTime.Now:yyyy}-{sequence:D6}";

            var saleUuid = Db.NewUuid();
            var cashierName = string.IsNullOrWhiteSpace(context.CashierName) ? "Caisse" : context.CashierName.Trim();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO sales
                  (Uuid, InvoiceNo, CustomerId, UserId, ShiftId, CashierName, Subtotal, DiscountPct,
                   DiscountAmount, TvaAmount, Total, PaidAmount, ChangeAmount, DueAmount, LoyaltyEarned)
                VALUES
                  (@uuid, @invoiceNo, @customerId, @userId, @shiftId, @cashier, @subtotal, @discountPct,
                   @discountAmount, @tva, @total, @paid, @change, @due, @loyalty)
                """,
                new
                {
                    uuid = saleUuid,
                    invoiceNo,
                    customerId = customer?.Id,
                    userId = context.UserId,
                    shiftId = shift.Id,
                    cashier = cashierName,
                    subtotal,
                    discountPct = request.DiscountPct,
                    discountAmount,
                    tva = ComputeTva(total, tvaRate),
                    total,
                    paid,
                    change,
                    due,
                    loyalty = 0
                },
                transaction, cancellationToken: cancellationToken));

            var saleId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken));

            var resultLines = new List<CheckoutLineResult>();
            foreach (var line in lines)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO sale_items (Uuid, SaleId, ProductId, ProductName, Qty, UnitPrice, DiscountPct, LineTotal)
                    VALUES (@uuid, @saleId, @productId, @name, @qty, @unitPrice, @discountPct, @lineTotal)
                    """,
                    new
                    {
                        uuid = Db.NewUuid(),
                        saleId,
                        productId = line.Product.Id,
                        name = line.Product.Name,
                        qty = line.Qty,
                        unitPrice = line.UnitPrice,
                        discountPct = line.DiscountPct,
                        lineTotal = line.LineTotal
                    },
                    transaction, cancellationToken: cancellationToken));

                resultLines.Add(new CheckoutLineResult
                {
                    Name = line.Product.Name,
                    Qty = line.Qty,
                    UnitPrice = line.UnitPrice,
                    DiscountPct = line.DiscountPct,
                    LineTotal = line.LineTotal
                });
            }

            foreach (var payment in payments)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO sale_payments (SaleId, Method, Amount) VALUES (@saleId, @method, @amount)",
                    new { saleId, method = payment.Method, amount = payment.Amount },
                    transaction, cancellationToken: cancellationToken));
            }

            var loyaltyEarned = customer is null
                ? 0
                : (int)Math.Min(int.MaxValue, Math.Floor(paid / loyaltyDivisor));

            if (customer is not null && (due > 0 || loyaltyEarned > 0))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE customers
                    SET DueBalance = DueBalance + @due, LoyaltyPoints = LoyaltyPoints + @loyalty
                    WHERE Id = @id
                    """,
                    new { due, loyalty = loyaltyEarned, id = customer.Id },
                    transaction, cancellationToken: cancellationToken));
            }

            if (loyaltyEarned > 0 || due > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE sales SET LoyaltyEarned = @loyalty WHERE Id = @id",
                    new { loyalty = loyaltyEarned, id = saleId },
                    transaction, cancellationToken: cancellationToken));
            }

            var result = new CheckoutResult
            {
                SaleUuid = saleUuid,
                InvoiceNo = invoiceNo,
                Total = total,
                Tva = ComputeTva(total, tvaRate),
                Paid = paid,
                Change = change,
                Due = due,
                LoyaltyEarned = loyaltyEarned,
                CreatedAt = DateTime.Now,
                SaleId = saleId,
                CustomerId = customer?.Id,
                CustomerName = customer?.FullName,
                DiscountAmount = discountAmount,
                Lines = resultLines,
                Payments = payments
            };

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE processed_operations SET ResultJson = @json, CompletedAt = NOW(3) WHERE OpId = @opId",
                new { json = JsonSerializer.Serialize(result, JsonDefaults.Options), opId },
                transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await _audit.LogAsync("sale_created", "sales", saleUuid,
                $"Facture {invoiceNo}, total {total:0.00} DH, du {due:0.00} DH",
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
                    _logger.LogError(rollbackError, "Checkout rollback failed for operation {OpId}", opId);
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

    private static decimal ComputeTva(decimal totalTtc, decimal tvaRate)
    {
        if (tvaRate <= 0) return 0m;
        return MoneyHelper.Round(totalTtc - totalTtc / (1m + tvaRate / 100m));
    }

    private static List<CheckoutPayment> NormalizePayments(List<CheckoutPayment>? payments)
    {
        var normalized = new List<CheckoutPayment>();
        foreach (var payment in payments ?? [])
        {
            if (payment.Amount < 0) throw new BusinessRuleException("Montant de paiement invalide.");
            var method = InputValidator.RequireMethod(payment.Method);
            var amount = MoneyHelper.Round(payment.Amount);
            if (amount == 0) continue;
            normalized.Add(new CheckoutPayment { Method = method, Amount = amount });
        }

        if (normalized.Count == 0) throw new BusinessRuleException("Aucun paiement fourni.");
        return normalized;
    }

    private async Task<CheckoutResult?> TryReadExistingAsync(string opId, CancellationToken cancellationToken)
    {
        var json = await _db.ExecuteScalarAsync<string>(
            "SELECT ResultJson FROM processed_operations WHERE OpId = @opId AND OpType = @opType LIMIT 1",
            new { opId, opType = OpType }, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CheckoutResult>(json, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Corrupt idempotency payload for operation {OpId}", opId);
            throw new DomainException("Operation deja traitee mais resultat illisible.", ex);
        }
    }

    private static async Task<CheckoutResult?> BeginOperationAsync(
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
            return JsonSerializer.Deserialize<CheckoutResult>(json, JsonDefaults.Options);
        }
    }
}
