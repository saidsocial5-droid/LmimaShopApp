using LmimaShopV2.Desktop.Core.Models;

namespace LmimaShopV2.Desktop.Helpers;

public static class InputValidator
{
    public static string RequireText(string? value, string field, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new BusinessRuleException($"{field} est obligatoire.");
        if (normalized.Length > maxLength) throw new BusinessRuleException($"{field} ne doit pas depasser {maxLength} caracteres.");
        return normalized;
    }

    public static string? OptionalText(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > maxLength) throw new BusinessRuleException($"Le texte ne doit pas depasser {maxLength} caracteres.");
        return normalized;
    }

    public static string? NormalizeBarcode(string? value)
    {
        var normalized = OptionalText(value, 64);
        if (normalized is null) return null;
        if (normalized.Any(char.IsControl)) throw new BusinessRuleException("Code-barres invalide.");
        return normalized;
    }

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new BusinessRuleException("Le mot de passe doit contenir au moins 6 caracteres.");
        if (password.Length > 72)
            throw new BusinessRuleException("Le mot de passe ne doit pas depasser 72 caracteres.");
    }

    public static string RequireRole(string? role)
    {
        if (!UserRoles.IsValid(role)) throw new BusinessRuleException("Role invalide.");
        return UserRoles.Normalize(role);
    }

    public static void ValidateQuantity(decimal qty)
    {
        if (qty <= 0) throw new BusinessRuleException("La quantite doit etre superieure a zero.");
        if (qty > 1_000_000m) throw new BusinessRuleException("Quantite invalide.");
    }

    public static void ValidateNonNegative(decimal amount, string field)
    {
        if (amount < 0) throw new BusinessRuleException($"{field} ne peut pas etre negatif.");
    }

    public static void ValidatePercentage(decimal pct, string field = "Remise")
    {
        if (pct is < 0 or > 100) throw new BusinessRuleException($"{field} doit etre entre 0 et 100.");
    }

    public static string RequireUuid(string? value, string field)
    {
        if (!Guid.TryParse(value, out var guid))
            throw new BusinessRuleException($"{field} invalide.");
        return guid.ToString("D");
    }

    public static string? OptionalUuid(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireUuid(value, field);

    public static string RequireMethod(string? method)
    {
        var normalized = (method ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "cash" or "card" or "credit" => normalized,
            _ => throw new BusinessRuleException("Mode de paiement invalide.")
        };
    }
}
