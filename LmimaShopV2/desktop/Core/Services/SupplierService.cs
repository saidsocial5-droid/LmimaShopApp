using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class SupplierService : ISupplierService
{
    private readonly Db _db;
    private readonly IAuditLogger _audit;
    private readonly ISseHub _sse;

    public SupplierService(Db db, IAuditLogger audit, ISseHub sse)
    {
        _db = db;
        _audit = audit;
        _sse = sse;
    }

    public async Task<List<Supplier>> SearchAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return await _db.QueryAsync<Supplier>(
            """
            SELECT * FROM suppliers
            WHERE DeletedAt IS NULL AND (@term IS NULL OR Name LIKE @term OR Phone LIKE @term)
            ORDER BY Name
            LIMIT 2000
            """,
            new { term },
            cancellationToken: cancellationToken);
    }

    public async Task<long> SaveAsync(Supplier supplier, string? actor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        var uuid = string.IsNullOrWhiteSpace(supplier.Uuid) ? Db.NewUuid() : InputValidator.RequireUuid(supplier.Uuid, "Fournisseur");
        var name = InputValidator.RequireText(supplier.Name, "Nom du fournisseur", 160);
        await _db.ExecuteAsync(
            """
            INSERT INTO suppliers (Uuid, Name, Phone, Address, Notes)
            VALUES (@uuid, @name, @phone, @address, @notes)
            ON DUPLICATE KEY UPDATE
              Name = VALUES(Name), Phone = VALUES(Phone), Address = VALUES(Address), Notes = VALUES(Notes)
            """,
            new
            {
                uuid,
                name,
                phone = InputValidator.OptionalText(supplier.Phone, 40),
                address = InputValidator.OptionalText(supplier.Address, 255),
                notes = InputValidator.OptionalText(supplier.Notes, 500)
            },
            cancellationToken: cancellationToken);

        var id = await _db.ExecuteScalarAsync<long>("SELECT Id FROM suppliers WHERE Uuid = @uuid", new { uuid }, cancellationToken: cancellationToken);
        await _audit.LogAsync("supplier_saved", "suppliers", uuid, $"Fournisseur: {name}", username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
        return id;
    }

    public async Task DeleteAsync(string uuid, string? actor = null, CancellationToken cancellationToken = default)
    {
        var id = InputValidator.RequireUuid(uuid, "Fournisseur");
        var affected = await _db.ExecuteAsync(
            "UPDATE suppliers SET DeletedAt = NOW(3) WHERE Uuid = @id AND DeletedAt IS NULL",
            new { id }, cancellationToken: cancellationToken);
        if (affected == 0) throw new BusinessRuleException("Fournisseur introuvable.");
        await _audit.LogAsync("supplier_deleted", "suppliers", id, null, username: actor, cancellationToken: cancellationToken);
        _sse.BroadcastDataChanged();
    }
}
