using Dapper;
using LmimaShopV2.Desktop.Config;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Infrastructure.Data;

/// <summary>Connection factory + small async Dapper helpers. No shared/static connection.</summary>
public sealed class Db
{
    private readonly AppConfigService _config;

    public Db(AppConfigService config) => _config = config;

    public string ConnectionString => _config.RequireConnectionString();

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        MySqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = transaction is null ? await OpenAsync(cancellationToken) : null;
        var executor = transaction?.Connection ?? connection!;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        return (await executor.QueryAsync<T>(command)).AsList();
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        MySqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = transaction is null ? await OpenAsync(cancellationToken) : null;
        var executor = transaction?.Connection ?? connection!;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        return await executor.QueryFirstOrDefaultAsync<T>(command);
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        MySqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = transaction is null ? await OpenAsync(cancellationToken) : null;
        var executor = transaction?.Connection ?? connection!;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        return await executor.ExecuteAsync(command);
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? parameters = null,
        MySqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = transaction is null ? await OpenAsync(cancellationToken) : null;
        var executor = transaction?.Connection ?? connection!;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        return await executor.ExecuteScalarAsync<T>(command);
    }

    public static string NewUuid() => Guid.NewGuid().ToString();

    public static string FormatTimestamp(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss.fff");

    public static string? FormatTimestamp(DateTime? value) => value is null ? null : FormatTimestamp(value.Value);

    public static DateTime ParseTimestamp(string? value, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }
}
