using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Infrastructure.Data;
using Dapper;

namespace LmimaShopV2.Desktop.Infrastructure.Server;

/// <summary>Six digit crypto-random pairing codes, 120 seconds lifetime, one-time use.</summary>
public sealed class PairingService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);
    private readonly Db _db;

    public PairingService(Db db) => _db = db;

    public async Task<PairingCodeInfo> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var expires = DateTime.Now.Add(Lifetime);
        await _db.ExecuteAsync(
            """
            INSERT INTO settings (SettingKey, SettingValue) VALUES ('pairing_code', @code)
            ON DUPLICATE KEY UPDATE SettingValue = VALUES(SettingValue), UpdatedAt = NOW(3)
            """,
            new { code }, cancellationToken: cancellationToken);
        await _db.ExecuteAsync(
            """
            INSERT INTO settings (SettingKey, SettingValue) VALUES ('pairing_code_expires_at', @expires)
            ON DUPLICATE KEY UPDATE SettingValue = VALUES(SettingValue), UpdatedAt = NOW(3)
            """,
            new { expires = expires.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) },
            cancellationToken: cancellationToken);

        return new PairingCodeInfo { Code = code, ExpiresAt = expires, Url = string.Empty };
    }

    /// <summary>Validates and consumes the current code atomically. Invalid input never falls back.</summary>
    public async Task<bool> ValidateAndConsumeAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (code is null || code.Length != 6 || !code.All(char.IsAsciiDigit)) return false;

        await using var connection = await _db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var rows = await connection.QueryAsync<SettingsRow>(new CommandDefinition(
                """
                SELECT * FROM settings
                WHERE SettingKey IN ('pairing_code', 'pairing_code_expires_at')
                FOR UPDATE
                """,
                transaction: transaction, cancellationToken: cancellationToken));
            var storedCode = rows.FirstOrDefault(r => r.SettingKey == "pairing_code")?.SettingValue;
            var expiresRaw = rows.FirstOrDefault(r => r.SettingKey == "pairing_code_expires_at")?.SettingValue;

            if (string.IsNullOrEmpty(storedCode) ||
                storedCode.Length != 6 ||
                !storedCode.All(char.IsAsciiDigit))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (!DateTime.TryParseExact(expiresRaw, "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires) ||
                DateTime.Now > expires)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var provided = Encoding.ASCII.GetBytes(code);
            var expected = Encoding.ASCII.GetBytes(storedCode);
            if (provided.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE settings SET SettingValue = '', UpdatedAt = NOW(3)
                WHERE SettingKey IN ('pairing_code', 'pairing_code_expires_at')
                """,
                transaction: transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
