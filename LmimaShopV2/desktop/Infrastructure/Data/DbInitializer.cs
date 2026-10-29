using System.Text;
using LmimaShopV2.Desktop.Config;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace LmimaShopV2.Desktop.Infrastructure.Data;

/// <summary>Creates the database on first run and applies db/schema.sql only when the schema is absent.</summary>
public sealed class DbInitializer
{
    private readonly AppConfigService _config;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppConfigService config, ILogger<DbInitializer> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        var database = _config.DatabaseName
            ?? throw new InvalidOperationException("Nom de base de donnees introuvable dans la configuration.");

        var serverConnectionString = _config.BuildServerConnectionString(string.Empty);
        await using (var server = new MySqlConnection(serverConnectionString))
        {
            await server.OpenAsync(cancellationToken);
            await using var create = server.CreateCommand();
            create.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new MySqlConnection(_config.RequireConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'users'";
        check.Parameters.AddWithValue("@db", database);
        var exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            _logger.LogInformation("Database {Database} already initialised; schema not modified", database);
            return;
        }

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "db", "schema.sql");
        if (!File.Exists(schemaPath))
            throw new FileNotFoundException("Fichier de schema introuvable.", schemaPath);

        var sql = await File.ReadAllTextAsync(schemaPath, Encoding.UTF8, cancellationToken);
        var statements = SplitStatements(sql);
        _logger.LogInformation("Applying schema ({Count} statements) to {Database}", statements.Count, database);

        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Database {Database} initialised successfully", database);
    }

    /// <summary>Splits a MySQL script on semicolons outside of quotes. Line comments are removed.</summary>
    internal static List<string> SplitStatements(string script)
    {
        var cleaned = new StringBuilder();
        foreach (var line in script.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("--", StringComparison.Ordinal)) continue;
            cleaned.AppendLine(line);
        }

        var statements = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var text = cleaned.ToString();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote && (i == 0 || text[i - 1] != '\\')) quote = '\0';
                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                quote = c;
                current.Append(c);
                continue;
            }

            if (c == ';')
            {
                var statement = current.ToString().Trim();
                if (statement.Length > 0) statements.Add(statement);
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0) statements.Add(tail);
        return statements;
    }
}
