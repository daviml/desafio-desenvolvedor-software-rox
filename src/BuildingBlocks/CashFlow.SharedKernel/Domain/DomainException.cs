namespace CashFlow.SharedKernel.Domain;

/// <summary>
/// Raised when an attempt is made to put an aggregate into an invalid state.
/// Guards invariants that must hold regardless of the caller (API, worker, test).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public DomainException(string message) : this("domain.invariant_violated", message)
    {
    }

    public DomainException() : this("domain.invariant_violated", "A domain invariant was violated.")
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = "domain.invariant_violated";

    public string Code { get; } = "domain.invariant_violated";
}
