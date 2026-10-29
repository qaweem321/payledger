using Microsoft.AspNetCore.Diagnostics;
using PayLedger.Application.Common;
using PayLedger.Domain.Common;

namespace PayLedger.Api.Infrastructure;

/// <summary>Turns known errors into RFC 9457 problem details with a stable <c>code</c> field.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int Status, string Code, string Title)? mapped = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "not_found", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
            ExchangeRateUnavailableException e => (StatusCodes.Status503ServiceUnavailable, "exchange_rate_unavailable", e.Message),
            _ => null,
        };
        if (mapped is not { } m)
        {
            return false;
        }

        httpContext.Response.StatusCode = m.Status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = m.Status,
                Title = m.Title,
                Extensions = { ["code"] = m.Code },
            },
        });
    }
}
