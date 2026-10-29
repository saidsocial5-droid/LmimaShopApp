using System.Globalization;
using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Infrastructure.Data;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly Db _db;
    private readonly IAuditLogger _audit;

    public SettingsService(Db db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.QueryAsync<SettingsRow>(
            "SELECT SettingKey, SettingValue, UpdatedAt FROM settings",
            cancellationToken: cancellationToken);
        return rows.ToDictionary(r => r.SettingKey, r => r.SettingValue ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _db.ExecuteScalarAsync<string>(
            "SELECT SettingValue FROM settings WHERE SettingKey = @key",
            new { key },
            cancellationToken: cancellationToken);
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken cancellationToken = default)
    {
        var raw = await GetValueAsync(key, cancellationToken);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public async Task SetValueAsync(
        string key,
        string value,
        bool audit = true,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO settings (SettingKey, SettingValue) VALUES (@key, @value)
            ON DUPLICATE KEY UPDATE SettingValue = VALUES(SettingValue), UpdatedAt = NOW(3)
            """,
            new { key, value },
            cancellationToken: cancellationToken);

        if (audit)
            await _audit.LogAsync("settings_updated", "settings", key, $"Valeur modifiee: {key}", username: actor, cancellationToken: cancellationToken);
    }

    public async Task SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var (key, value) in values)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO settings (SettingKey, SettingValue) VALUES (@key, @value)
                    ON DUPLICATE KEY UPDATE SettingValue = VALUES(SettingValue), UpdatedAt = NOW(3)
                    """,
                    new { key, value },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await _audit.LogAsync(
            "settings_updated",
            "settings",
            null,
            $"Parametres mis a jour: {string.Join(", ", values.Keys)}",
            username: actor,
            cancellationToken: cancellationToken);
    }

    public async Task<StoreSettings> GetStoreSettingsAsync(CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        var settings = new StoreSettings
        {
            TvaRate = ParseDecimal(all, "tva_rate", 20m),
            PricesIncludeTva = ParseBool(all, "prices_include_tva", true),
            LoyaltyMadPerPoint = Math.Max(1m, ParseDecimal(all, "loyalty_mad_per_point", 10m)),
            Shop = new ShopInfo
            {
                Name = Get(all, "shop_name", "LmimaShop"),
                Ice = Get(all, "shop_ice", string.Empty),
                Rc = Get(all, "shop_rc", string.Empty),
                FiscalIf = Get(all, "shop_if", string.Empty),
                Patente = Get(all, "shop_patente", string.Empty),
                Address = Get(all, "shop_address", string.Empty),
                Phone = Get(all, "shop_phone", string.Empty),
                Footer = Get(all, "receipt_footer", string.Empty)
            }
        };
        if (settings.TvaRate is < 0 or > 100) settings.TvaRate = 20m;
        return settings;
    }

    public async Task<bool> IsAdminNoticeShownAsync(CancellationToken cancellationToken = default) =>
        ParseBool(await GetAllAsync(cancellationToken), "default_admin_notice_shown", false);

    public Task MarkAdminNoticeShownAsync(CancellationToken cancellationToken = default) =>
        SetValueAsync("default_admin_notice_shown", "true", audit: false, cancellationToken: cancellationToken);

    private static string Get(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static decimal ParseDecimal(Dictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue(key, out var raw) &&
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static bool ParseBool(Dictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value) ? value : fallback;
}
