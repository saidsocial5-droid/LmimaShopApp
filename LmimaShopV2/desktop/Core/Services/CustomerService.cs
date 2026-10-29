using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class CustomerService : ICustomerService
{
    private readonly Db _db;
    private readonly IAuditLogger _audit;
    private readonly ISseHub _sse;

    public CustomerService(Db db, IAuditLogger audit, ISseHub sse)
    {
        _db = db;
        _audit = audit;
        _sse = sse;
    }

    public async Task<List<Customer>> SearchAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return await _db.QueryAsync<Customer>(
            """
            SELECT * FROM customers
            WHERE DeletedAt IS NULL
              AND (@term IS NULL OR FullName LIKE @term OR Phone LIKE @term)
            ORDER BY FullName
            LIMIT 2000
            """,
            new { term },
            cancellationToken: cancellationToken);
    }

    public async Task<Customer?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var id = InputValidator.RequireUuid(uuid, "Client");
        return await _db.QueryFirstOrDefaultAsync<Customer>(
            "SELECT * FROM customers WHERE Uuid = @id AND DeletedAt IS NULL LIMIT 1",
            new { id },
            cancellationToken: cancellationToken);
    }

    public async Task<long> SaveAsync(Customer customer, string? actor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        var uuid = string.IsNullOrWhiteSpace(customer.Uuid) ? Db.NewUuid() : InputValidator.RequireUuid(customer.Uuid, "Client");
        var name = InputValidator.RequireText(customer.FullName, "Nom du client", 160);
        if (customer.CreditLimit < 0) throw new BusinessRuleException("La limite de credit ne peut pas etre negative.");

        await _db.ExecuteAsync(
            """
            INSERT INTO customers (Uuid, FullName, Phone, Address, CreditLimit, Notes)
            VALUES (@uuid, @name, @phone, @address, @creditLimit, @notes)
            ON DUPLICATE KEY UPDATE
              FullName = VALUES(FullName), Phone = VALUES(Phone), Address = VALUES(Address),
              CreditLimit = VALUES(CreditLimit), Notes = VALUES(Notes)
            """,
            new
            {
                uuid,
                name,
                phone = InputValidator.OptionalText(customer.Phone, 40),
                address = InputValidator.OptionalText(customer.Address, 255),
                creditLimit = customer.CreditLimit,
                notes = InputValidator.OptionalText(customer.Notes, 500)
            },
            cancellationToken: cancellationToken);

        var id = await _db.ExecuteScalarAsync<long>(
            "SELECT Id FROM customers WHERE Uuid = @uuid", new { uuid }, cancellationToken: cancellationToken);
        await _audit.LogAsync("customer_saved", "customers", uuid, $"Client: {name}", username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
        return id;
    }

    public async Task DeleteAsync(string uuid, string? actor = null, CancellationToken cancellationToken = default)
    {
        var id = InputValidator.RequireUuid(uuid, "Client");
        var customer = await _db.QueryFirstOrDefaultAsync<Customer>(
            "SELECT * FROM customers WHERE Uuid = @id AND DeletedAt IS NULL FOR UPDATE",
            new { id },
            cancellationToken: cancellationToken) ?? throw new BusinessRuleException("Client introuvable.");
        if (customer.DueBalance > 0.009m)
            throw new BusinessRuleException("Impossible de supprimer un client ayant une dette.");
        await _db.ExecuteAsync(
            "UPDATE customers SET DeletedAt = NOW(3) WHERE Uuid = @id",
            new { id },
            cancellationToken: cancellationToken);
        await _audit.LogAsync("customer_deleted", "customers", id, null, username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
    }

    public async Task<long> AddPaymentAsync(
        string customerUuid,
        decimal amount,
        string method,
        string? notes,
        long? userId = null,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        var uuid = InputValidator.RequireUuid(customerUuid, "Client");
        if (amount <= 0) throw new BusinessRuleException("Le montant doit etre superieur a zero.");
        var normalizedMethod = InputValidator.RequireMethod(method);
        if (normalizedMethod == "credit") throw new BusinessRuleException("Un paiement ne peut pas etre un credit.");

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var customer = await connection.QueryFirstOrDefaultAsync<Customer>(new CommandDefinition(
                "SELECT * FROM customers WHERE Uuid = @uuid AND DeletedAt IS NULL FOR UPDATE",
                new { uuid }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessRuleException("Client introuvable.");

            if (amount > customer.DueBalance + 0.009m)
                throw new BusinessRuleException("Le paiement depasse la dette du client.");

            var shiftId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT Id FROM cash_register_shifts WHERE ClosedAt IS NULL ORDER BY Id DESC LIMIT 1",
                transaction: transaction, cancellationToken: cancellationToken));

            var paymentUuid = Db.NewUuid();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO customer_payments (Uuid, CustomerId, UserId, ShiftId, Amount, Method, Notes)
                VALUES (@paymentUuid, @customerId, @userId, @shiftId, @amount, @method, @notes)
                """,
                new
                {
                    paymentUuid,
                    customerId = customer.Id,
                    userId,
                    shiftId,
                    amount,
                    method = normalizedMethod,
                    notes = InputValidator.OptionalText(notes, 500)
                },
                transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE customers SET DueBalance = GREATEST(0, DueBalance - @amount) WHERE Id = @id",
                new { amount, id = customer.Id }, transaction, cancellationToken: cancellationToken));

            var paymentId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            await _audit.LogAsync("customer_payment_added", "customer_payments", paymentUuid,
                $"Client {customer.FullName}: {amount:0.00} DH ({normalizedMethod})", userId, actor, cancellationToken);
            _sse.BroadcastDataChanged();
            return paymentId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CustomerStatement> GetStatementAsync(string customerUuid, CancellationToken cancellationToken = default)
    {
        var uuid = InputValidator.RequireUuid(customerUuid, "Client");
        var customer = await _db.QueryFirstOrDefaultAsync<Customer>(
            "SELECT * FROM customers WHERE Uuid = @uuid AND DeletedAt IS NULL",
            new { uuid }, cancellationToken: cancellationToken) ?? throw new BusinessRuleException("Client introuvable.");

        var entries = new List<StatementEntry>();
        var sales = await _db.QueryAsync<Sale>(
            """
            SELECT * FROM sales
            WHERE CustomerId = @customerId AND DeletedAt IS NULL AND DueAmount > 0
            ORDER BY CreatedAt
            """,
            new { customerId = customer.Id }, cancellationToken: cancellationToken);
        entries.AddRange(sales.Select(s => new StatementEntry
        {
            Date = s.CreatedAt,
            Type = "sale",
            Ref = s.InvoiceNo,
            Debit = s.DueAmount
        }));

        var payments = await _db.QueryAsync<CustomerPayment>(
            "SELECT * FROM customer_payments WHERE CustomerId = @customerId AND DeletedAt IS NULL ORDER BY CreatedAt",
            new { customerId = customer.Id }, cancellationToken: cancellationToken);
        entries.AddRange(payments.Select(p => new StatementEntry
        {
            Date = p.CreatedAt,
            Type = "payment",
            Ref = p.Uuid[..8].ToUpperInvariant(),
            Credit = p.Amount
        }));

        var returns = await _db.QueryAsync<SaleReturn>(
            """
            SELECT r.* FROM returns r
            JOIN sales s ON s.Id = r.SaleId
            WHERE s.CustomerId = @customerId AND r.DeletedAt IS NULL AND r.DueReduction > 0
            ORDER BY r.CreatedAt
            """,
            new { customerId = customer.Id }, cancellationToken: cancellationToken);
        entries.AddRange(returns.Select(r => new StatementEntry
        {
            Date = r.CreatedAt,
            Type = "return",
            Ref = "RET-" + r.Uuid[..8].ToUpperInvariant(),
            Credit = r.DueReduction
        }));

        var ordered = entries.OrderBy(e => e.Date).ToList();
        var balance = 0m;
        foreach (var entry in ordered)
        {
            balance += entry.Debit - entry.Credit;
            entry.Balance = MoneyHelper.Round(balance);
        }

        return new CustomerStatement
        {
            Customer = customer,
            OpeningBalance = 0m,
            Entries = ordered
        };
    }
}
