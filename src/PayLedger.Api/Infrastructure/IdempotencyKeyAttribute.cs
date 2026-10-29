using System.ComponentModel.DataAnnotations;
using PayLedger.Application.Payments;

namespace PayLedger.Api.Infrastructure;

/// <summary>An optional Idempotency-Key: 1-100 visible ASCII characters (a UUID is a good choice).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class IdempotencyKeyAttribute : ValidationAttribute
{
    public IdempotencyKeyAttribute() : base($"Idempotency-Key must be 1-{IdempotencyExecutor.MaxKeyLength} visible ASCII characters.") { }

    public override bool IsValid(object? value) =>
        value is null
        || (value is string { Length: > 0 and <= IdempotencyExecutor.MaxKeyLength } key && key.All(c => c is > ' ' and <= '~'));
}
