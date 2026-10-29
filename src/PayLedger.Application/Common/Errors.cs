using PayLedger.Domain.Common;

namespace PayLedger.Application.Common;

public sealed class NotFoundException(string what) : Exception($"{what} was not found.");

public sealed class ConflictException(string code, string message) : DomainException(code, message);

public sealed class ExchangeRateUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
