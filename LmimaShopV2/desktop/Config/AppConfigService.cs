using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Serilog;

namespace LmimaShopV2.Desktop.Config;

/// <summary>
/// Application preferences + DPAPI encrypted MySQL connection string.
/// Stored in %APPDATA%\LmimaShopV2\settings.json (never in the repository).
/// </summary>
public sealed class AppConfigService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LmimaShopV2.ConnectionString.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    private PersistedSettings _data = new();
    private string? _connectionString;

    public AppConfigService(IConfiguration configuration)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AppFolder = Path.Combine(appData, "LmimaShopV2");
        SettingsPath = Path.Combine(AppFolder, "settings.json");
        BackupFolder = Path.Combine(AppFolder, "backups");
        LogFolder = Path.Combine(AppFolder, "logs");

        DefaultDbHost = configuration["Database:Host"] ?? "localhost";
        DefaultDbPort = int.TryParse(configuration["Database:Port"], out var p) ? p : 3306;
        DefaultDbName = configuration["Database:Name"] ?? "LmimaShopV2DB";
        ServerPort = int.TryParse(configuration["Server:Port"], out var sp) ? sp : 8090;
        MobileFolderName = configuration["Mobile:FolderName"] ?? "mobile";

        Directory.CreateDirectory(AppFolder);
        Directory.CreateDirectory(BackupFolder);
        Load();
    }

    public string AppFolder { get; }
    public string SettingsPath { get; }
    public string BackupFolder { get; }
    public string LogFolder { get; }
    public string DefaultDbHost { get; }
    public int DefaultDbPort { get; }
    public string DefaultDbName { get; }
    public int ServerPort { get; }
    public string MobileFolderName { get; }
    public bool IsConfigured { get; private set; }

    public string? ConnectionString
    {
        get
        {
            lock (_sync)
            {
                return _connectionString;
            }
        }
    }

    public string? DatabaseName => BuildBuilderSafe()?.Database;
    public string? DatabaseHost => BuildBuilderSafe()?.Server;

    public string Theme
    {
        get => _data.Theme;
        set
        {
            _data.Theme = value;
            Save();
        }
    }

    public string PrinterName
    {
        get => _data.PrinterName;
        set
        {
            _data.PrinterName = value;
            Save();
        }
    }

    public string ResolveMobileFolder() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _data.MobileFolderName));

    public string RequireConnectionString() =>
        ConnectionString ?? throw new InvalidOperationException(
            "La connexion MySQL n est pas configuree. Ouvrez la configuration de la base de donnees.");

    public void SaveDatabaseConnection(string host, int port, string user, string password, string database)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Hote requis.", nameof(host));
        if (port is <= 0 or > 65535) throw new ArgumentException("Port invalide.", nameof(port));
        if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("Utilisateur requis.", nameof(user));
        if (string.IsNullOrWhiteSpace(database) || !database.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Nom de base invalide.", nameof(database));

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host.Trim(),
            Port = (uint)port,
            UserID = user.Trim(),
            Password = password,
            Database = database.Trim(),
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 8,
            // Uuid columns are CHAR(36) and mapped to string; never treat them as Guid.
            GuidFormat = MySqlGuidFormat.None
        };
        var plain = builder.ConnectionString;
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        lock (_sync)
        {
            _data.ProtectedConnectionString = Convert.ToBase64String(protectedBytes);
            _connectionString = plain;
            IsConfigured = true;
            Save();
        }
    }

    public void ClearDatabaseConnection()
    {
        lock (_sync)
        {
            _data.ProtectedConnectionString = null;
            _connectionString = null;
            IsConfigured = false;
            Save();
        }
    }

    public string BuildServerConnectionString(string database)
    {
        var builder = BuildBuilderSafe() ?? new MySqlConnectionStringBuilder
        {
            Server = DefaultDbHost,
            Port = (uint)DefaultDbPort
        };
        builder.Database = database;
        return builder.ConnectionString;
    }

    public static string BuildConnectionString(string host, int port, string user, string password, string database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            Database = database,
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 8,
            GuidFormat = MySqlGuidFormat.None
        };
        return builder.ConnectionString;
    }

    public static async Task TestConnectionAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
    }

    private MySqlConnectionStringBuilder? BuildBuilderSafe()
    {
        var cs = ConnectionString;
        if (string.IsNullOrWhiteSpace(cs)) return null;
        try
        {
            return new MySqlConnectionStringBuilder(cs);
        }
        catch (ArgumentException ex)
        {
            Log.Warning(ex, "Stored MySQL connection string is invalid");
            return null;
        }
    }

    private void Load()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            _data = JsonSerializer.Deserialize<PersistedSettings>(json, JsonOptions) ?? new PersistedSettings();
            if (string.IsNullOrWhiteSpace(_data.MobileFolderName))
                _data.MobileFolderName = MobileFolderName;

            if (!string.IsNullOrWhiteSpace(_data.ProtectedConnectionString))
            {
                var bytes = Convert.FromBase64String(_data.ProtectedConnectionString);
                var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                _connectionString = Encoding.UTF8.GetString(plain);
                IsConfigured = true;
            }
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or IOException)
        {
            Log.Error(ex, "Unable to read settings file {Path}; starting unconfigured", SettingsPath);
            _data = new PersistedSettings();
            _connectionString = null;
            IsConfigured = false;
        }
    }

    private void Save()
    {
        lock (_sync)
        {
            try
            {
                var temp = SettingsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_data, JsonOptions));
                File.Move(temp, SettingsPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "Unable to persist settings file {Path}", SettingsPath);
            }
        }
    }

    private sealed class PersistedSettings
    {
        public string? ProtectedConnectionString { get; set; }
        public string Theme { get; set; } = "light";
        public string PrinterName { get; set; } = string.Empty;
        public string MobileFolderName { get; set; } = "mobile";
    }
}
