using LmimaShopV2.Desktop.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LmimaShopV2.Desktop.Infrastructure.Data;

public sealed class AuditLogger : IAuditLogger
{
    private static readonly Action<ILogger, string, Exception?> AuditFailure =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(4100, "AuditInsertFailed"),
            "Failed to write audit log entry for {Action}");

    private readonly Db _db;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(Db db, ILogger<AuditLogger> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string? entityUuid = null,
        string? details = null,
        long? userId = null,
        string? username = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.ExecuteAsync(
                """
                INSERT INTO audit_logs (UserId, Username, Action, EntityType, EntityUuid, Details)
                VALUES (@userId, @username, @action, @entityType, @entityUuid, @details)
                """,
                new
                {
                    userId,
                    username = Truncate(username, 120),
                    action = Truncate(action, 80) ?? "unknown",
                    entityType = Truncate(entityType, 60) ?? "unknown",
                    entityUuid = Truncate(entityUuid, 64),
                    details = Truncate(details, 4000)
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            AuditFailure(_logger, action, ex);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
