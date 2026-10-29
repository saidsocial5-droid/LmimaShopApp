using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Infrastructure.Data;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class ShiftService : IShiftService
{
    private readonly Db _db;
    private readonly IReportService _reports;
    private readonly IAuditLogger _audit;
    private readonly ISseHub _sse;

    public ShiftService(Db db, IReportService reports, IAuditLogger audit, ISseHub sse)
    {
        _db = db;
        _reports = reports;
        _audit = audit;
        _sse = sse;
    }

    public async Task<Shift?> GetOpenShiftAsync(CancellationToken cancellationToken = default) =>
        await _db.QueryFirstOrDefaultAsync<Shift>(
            """
            SELECT s.*, u.FullName AS CashierName
            FROM cash_register_shifts s JOIN users u ON u.Id = s.UserId
            WHERE s.ClosedAt IS NULL AND s.DeletedAt IS NULL
            ORDER BY s.Id DESC LIMIT 1
            """,
            cancellationToken: cancellationToken);

    public async Task<Shift?> GetShiftAsync(long shiftId, CancellationToken cancellationToken = default) =>
        await _db.QueryFirstOrDefaultAsync<Shift>(
            """
            SELECT s.*, u.FullName AS CashierName
            FROM cash_register_shifts s JOIN users u ON u.Id = s.UserId
            WHERE s.Id = @shiftId AND s.DeletedAt IS NULL
            """,
            new { shiftId }, cancellationToken: cancellationToken);

    public async Task<List<Shift>> GetRecentAsync(int limit = 30, CancellationToken cancellationToken = default) =>
        await _db.QueryAsync<Shift>(
            """
            SELECT s.*, u.FullName AS CashierName
            FROM cash_register_shifts s JOIN users u ON u.Id = s.UserId
            WHERE s.DeletedAt IS NULL
            ORDER BY s.OpenedAt DESC
            LIMIT @limit
            """,
            new { limit = Math.Clamp(limit, 1, 200) }, cancellationToken: cancellationToken);

    public async Task<Shift> OpenAsync(
        long userId,
        string cashierName,
        decimal openingFloat,
        CancellationToken cancellationToken = default)
    {
        if (openingFloat < 0) throw new BusinessRuleException("Le fond de caisse ne peut pas etre negatif.");
        var uuid = Db.NewUuid();
        try
        {
            await _db.ExecuteAsync(
                """
                INSERT INTO cash_register_shifts (Uuid, UserId, OpeningFloat)
                VALUES (@uuid, @userId, @openingFloat)
                """,
                new { uuid, userId, openingFloat },
                cancellationToken: cancellationToken);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new BusinessRuleException("Un shift est deja ouvert. Fermez-le avant d en ouvrir un autre.");
        }

        var shift = await _db.QueryFirstOrDefaultAsync<Shift>(
            "SELECT * FROM cash_register_shifts WHERE Uuid = @uuid",
            new { uuid }, cancellationToken: cancellationToken)
            ?? throw new DomainException("Shift cree mais introuvable.");

        await _audit.LogAsync("shift_opened", "cash_register_shifts", uuid,
            $"Fond de caisse: {openingFloat:0.00} DH", userId, cashierName, cancellationToken);
        _sse.BroadcastDataChanged();
        return shift;
    }

    public async Task<ShiftReport> CloseAsync(
        long shiftId,
        decimal countedCash,
        string? notes,
        long userId,
        string cashierName,
        CancellationToken cancellationToken = default)
    {
        if (countedCash < 0) throw new BusinessRuleException("Le montant compte ne peut pas etre negatif.");

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var shift = await connection.QueryFirstOrDefaultAsync<Shift>(new CommandDefinition(
                "SELECT * FROM cash_register_shifts WHERE Id = @shiftId AND DeletedAt IS NULL FOR UPDATE",
                new { shiftId }, transaction, cancellationToken: cancellationToken))
                ?? throw new BusinessRuleException("Shift introuvable.");
            if (shift.ClosedAt is not null) throw new BusinessRuleException("Ce shift est deja ferme.");

            var report = await _reports.GetZReportAsync(shiftId, cancellationToken);
            var difference = countedCash - report.ExpectedCash;
            var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE cash_register_shifts SET
                  ClosedAt = NOW(3), CashSales = @cash, CardSales = @card, CreditSales = @credit,
                  DebtPaymentsCash = @debt, ReturnsRefund = @refunds, ExpectedCash = @expected,
                  CountedCash = @counted, Difference = @difference, TvaCollected = @tva,
                  LoyaltyGranted = @loyalty, Notes = @notes
                WHERE Id = @shiftId
                """,
                new
                {
                    cash = report.CashSales,
                    card = report.CardSales,
                    credit = report.CreditSales,
                    debt = report.DebtPaymentsCash,
                    refunds = report.ReturnsRefund,
                    expected = report.ExpectedCash,
                    counted = countedCash,
                    difference,
                    tva = report.TvaCollected,
                    loyalty = report.LoyaltyGranted,
                    notes = cleanNotes,
                    shiftId
                },
                transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            report.ClosedAt = DateTime.Now;
            report.CountedCash = countedCash;
            report.Difference = difference;
            report.Notes = cleanNotes;

            await _audit.LogAsync("shift_closed", "cash_register_shifts", shift.Uuid,
                $"Compte: {countedCash:0.00} DH, attendu: {report.ExpectedCash:0.00} DH, ecart: {difference:0.00} DH",
                userId, cashierName, cancellationToken);
            _sse.BroadcastDataChanged();
            return report;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<ShiftReport> GetReportAsync(long shiftId, CancellationToken cancellationToken = default) =>
        _reports.GetZReportAsync(shiftId, cancellationToken);
}
