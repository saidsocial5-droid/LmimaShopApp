namespace LmimaShopV2.Desktop.Core.Models;

/// <summary>Raised for expected business-rule violations (stock, credit limit, closed shift...).</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}
