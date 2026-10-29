namespace PayLedger.Domain.Common;

/// <summary>A business rule was broken. <see cref="Code"/> is a stable, machine-readable error code.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
