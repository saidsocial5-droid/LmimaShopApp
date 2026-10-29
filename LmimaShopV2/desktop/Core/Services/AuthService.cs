using LmimaShopV2.Desktop.Core.Interfaces;
using LmimaShopV2.Desktop.Core.Models;
using LmimaShopV2.Desktop.Helpers;
using LmimaShopV2.Desktop.Infrastructure.Data;
using MySqlConnector;
using BCryptNet = BCrypt.Net.BCrypt;
using BCryptSaltParseException = BCrypt.Net.SaltParseException;

namespace LmimaShopV2.Desktop.Core.Services;

public sealed class AuthService : IAuthService
{
    private const int WorkFactor = 12;
    private readonly Db _db;
    private readonly IAuditLogger _audit;

    public AuthService(Db db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AuthenticatedUser> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var normalized = (username ?? string.Empty).Trim();
        if (normalized.Length == 0 || string.IsNullOrEmpty(password))
            throw new BusinessRuleException("Identifiants invalides.");

        var user = await _db.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE Username = @username AND DeletedAt IS NULL LIMIT 1",
            new { username = normalized },
            cancellationToken: cancellationToken);

        if (user is null || !user.IsActive)
            throw new BusinessRuleException("Identifiants invalides.");

        bool valid;
        try
        {
            valid = BCryptNet.Verify(password, user.PasswordHash);
        }
        catch (BCryptSaltParseException)
        {
            valid = false;
        }

        if (!valid)
        {
            await _audit.LogAsync("login_failed", "users", user.Uuid, "Mot de passe incorrect",
                user.Id, user.Username, cancellationToken);
            throw new BusinessRuleException("Identifiants invalides.");
        }

        await _audit.LogAsync("login_success", "users", user.Uuid, null, user.Id, user.Username, cancellationToken);
        return new AuthenticatedUser
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Role = UserRoles.Normalize(user.Role)
        };
    }

    public async Task<List<User>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await _db.QueryAsync<User>(
            "SELECT * FROM users WHERE DeletedAt IS NULL ORDER BY Username",
            cancellationToken: cancellationToken);

    public async Task<long> CreateUserAsync(
        string username,
        string fullName,
        string password,
        string role,
        string? actor = null,
        CancellationToken cancellationToken = default)
    {
        var name = InputValidator.RequireText(username, "Nom utilisateur", 60);
        if (name.Length < 3 || !name.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new BusinessRuleException("Nom utilisateur invalide (3 caracteres minimum, lettres/chiffres/._-).");
        var displayName = InputValidator.RequireText(fullName, "Nom complet", 120);
        InputValidator.ValidatePassword(password);
        var normalizedRole = InputValidator.RequireRole(role);

        var exists = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE Username = @name AND DeletedAt IS NULL",
            new { name },
            cancellationToken: cancellationToken);
        if (exists > 0) throw new BusinessRuleException("Ce nom utilisateur existe deja.");

        var hash = BCryptNet.HashPassword(password, WorkFactor);
        var uuid = Db.NewUuid();
        var id = await InsertAsync(uuid, name, hash, displayName, normalizedRole, cancellationToken);
        await _audit.LogAsync("user_created", "users", uuid, $"Role: {normalizedRole}", username: actor, cancellationToken: cancellationToken);
        return id;
    }

    public async Task ResetPasswordAsync(long userId, string newPassword, string? actor = null, CancellationToken cancellationToken = default)
    {
        InputValidator.ValidatePassword(newPassword);
        var affected = await _db.ExecuteAsync(
            "UPDATE users SET PasswordHash = @hash WHERE Id = @id AND DeletedAt IS NULL",
            new { hash = BCryptNet.HashPassword(newPassword, WorkFactor), id = userId },
            cancellationToken: cancellationToken);
        if (affected == 0) throw new BusinessRuleException("Utilisateur introuvable.");
        await _audit.LogAsync("password_reset", "users", userId.ToString(), null, username: actor, cancellationToken: cancellationToken);
    }

    public async Task SetActiveAsync(long userId, bool isActive, string? actor = null, CancellationToken cancellationToken = default)
    {
        var user = await _db.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM users WHERE Id = @id AND DeletedAt IS NULL",
            new { id = userId },
            cancellationToken: cancellationToken) ?? throw new BusinessRuleException("Utilisateur introuvable.");

        if (!isActive && UserRoles.Normalize(user.Role) == UserRoles.Admin)
        {
            var admins = await _db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM users WHERE Role = 'Admin' AND IsActive = 1 AND DeletedAt IS NULL");
            if (admins <= 1) throw new BusinessRuleException("Impossible de desactiver le dernier administrateur.");
        }

        await _db.ExecuteAsync(
            "UPDATE users SET IsActive = @isActive WHERE Id = @id",
            new { isActive, id = userId },
            cancellationToken: cancellationToken);
        await _audit.LogAsync(isActive ? "user_enabled" : "user_disabled", "users", user.Uuid, null,
            username: actor, cancellationToken: cancellationToken);
    }

    private async Task<long> InsertAsync(string uuid, string username, string hash, string fullName, string role, CancellationToken ct)
    {
        await using var connection = await _db.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO users (Uuid, Username, PasswordHash, FullName, Role, IsActive)
                VALUES (@uuid, @username, @hash, @fullName, @role, 1)
                """,
                new { uuid, username, hash, fullName, role },
                transaction,
                cancellationToken: ct));
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            return id;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            await transaction.RollbackAsync(ct);
            throw new BusinessRuleException("Ce nom utilisateur existe deja.");
        }
    }
}
