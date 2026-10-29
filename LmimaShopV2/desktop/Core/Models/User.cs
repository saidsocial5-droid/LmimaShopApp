namespace LmimaShopV2.Desktop.Core.Models;

public sealed class User
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.Cashier;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class AuthenticatedUser
{
    public long Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Role { get; init; } = UserRoles.Cashier;

    public bool IsAdmin => string.Equals(Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase);
    public bool IsManager => IsAdmin || string.Equals(Role, UserRoles.Manager, StringComparison.OrdinalIgnoreCase);
    public bool IsCashier => string.Equals(Role, UserRoles.Cashier, StringComparison.OrdinalIgnoreCase);
    public bool CanViewReports => IsManager;
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Cashier = "Cashier";

    public static readonly string[] All = [Admin, Manager, Cashier];

    public static bool IsValid(string? role) =>
        !string.IsNullOrWhiteSpace(role) &&
        All.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? role) =>
        All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) ?? Cashier;
}
