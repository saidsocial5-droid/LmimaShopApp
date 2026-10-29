namespace LmimaShopV2.Desktop.Core.Models;

public sealed class Customer
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal DueBalance { get; set; }
    public int LoyaltyPoints { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }

    public decimal AvailableCredit => CreditLimit <= 0 ? decimal.MaxValue : Math.Max(0, CreditLimit - DueBalance);
}

public sealed class CustomerPayment
{
    public long Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public long? UserId { get; set; }
    public long? ShiftId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "cash";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int Version { get; set; }
}

public sealed class StatementEntry
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "sale";
    public string Ref { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
}

public sealed class CustomerStatement
{
    public Customer Customer { get; set; } = new();
    public decimal OpeningBalance { get; set; }
    public List<StatementEntry> Entries { get; set; } = [];
}

public sealed class PaymentSelection
{
    public decimal Cash { get; set; }
    public decimal Card { get; set; }
    public decimal Credit { get; set; }
    public decimal Total => Cash + Card + Credit;
}
