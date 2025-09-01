namespace Catalog.Domain.Common;

/// <summary>
/// Exception thrown when a business rule is violated in the domain layer.
/// This represents a violation of domain invariants or business constraints.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException) : base(message, innerException)
    {
    }
}