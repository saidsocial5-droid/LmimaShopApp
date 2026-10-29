using System.Globalization;
using System.IO;
using System.Text;
using LmimaShopV2.Desktop.Config;
using Microsoft.Extensions.Logging;

namespace LmimaShopV2.Desktop.Infrastructure.Data;

/// <summary>Creates a portable logical SQL backup (schema + data) in %APPDATA%\LmimaShopV2\backups.</summary>
public sealed class BackupService
{
    private readonly Db _db;
    private readonly AppConfigService _config;
    private readonly ILogger<BackupService> _logger;

    public BackupService(Db db, AppConfigService config, ILogger<BackupService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_config.BackupFolder);
        var file = Path.Combine(_config.BackupFolder, $"LmimaShopV2DB-{DateTime.Now:yyyyMMdd-HHmmss}.sql");

        await using var connection = await _db.OpenAsync(cancellationToken);
        var tables = new List<(string Name, string CreateSql)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var names = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
            await reader.CloseAsync();

            foreach (var name in names)
            {
                await using var create = connection.CreateCommand();
                create.CommandText = $"SHOW CREATE TABLE `{name.Replace("`", "``")}`";
                await using var createReader = await create.ExecuteReaderAsync(cancellationToken);
                if (!await createReader.ReadAsync(cancellationToken)) continue;
                tables.Add((name, createReader.GetString(1)));
                await createReader.CloseAsync();
            }
        }

        await using var writer = new StreamWriter(file, false, new UTF8Encoding(false));
        await writer.WriteLineAsync("-- LmimaShopV2 database backup");
        await writer.WriteLineAsync($"-- Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS=0;");
        await writer.WriteLineAsync();

        foreach (var (name, createSql) in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync($"DROP TABLE IF EXISTS `{name}`;");
            await writer.WriteLineAsync(createSql + ";");
            await WriteRowsAsync(connection, writer, name, cancellationToken);
            await writer.WriteLineAsync();
        }

        await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS=1;");
        await writer.FlushAsync(cancellationToken);
        _logger.LogInformation("Database backup written to {Path}", file);
        return file;
    }

    private static async Task WriteRowsAsync(
        MySqlConnector.MySqlConnection connection,
        TextWriter writer,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM `{table.Replace("`", "``")}`";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++) columns[i] = reader.GetName(i);
        var columnList = string.Join(", ", columns.Select(c => $"`{c.Replace("`", "``")}`"));

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                values[i] = reader.IsDBNull(i) ? "NULL" : FormatValue(reader.GetValue(i));
            await writer.WriteLineAsync(
                $"INSERT INTO `{table.Replace("`", "``")}` ({columnList}) VALUES ({string.Join(", ", values)});");
        }
    }

    private static string FormatValue(object value) => value switch
    {
        string s => $"'{Escape(s)}'",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
        DateOnly d => $"'{d:yyyy-MM-dd}'",
        TimeSpan t => $"'{t}'",
        bool b => b ? "1" : "0",
        byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => $"'{Escape(value.ToString() ?? string.Empty)}'"
    };

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal)
        .Replace("\0", "\\0", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\u001a", "\\Z", StringComparison.Ordinal);
}
