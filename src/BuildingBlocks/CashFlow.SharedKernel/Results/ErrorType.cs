namespace CashFlow.SharedKernel.Results;

/// <summary>
/// Classifies a failure so transport layers can map it (HTTP status, message NACK)
/// without the application layer knowing anything about HTTP or AMQP.
/// </summary>
public enum ErrorType
{
    Validation = 0,
    NotFound = 1,
    Conflict = 2,
    Unprocessable = 3,
    Unavailable = 4,
    Unexpected = 5,
}
