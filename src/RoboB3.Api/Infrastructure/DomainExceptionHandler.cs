using Microsoft.AspNetCore.Diagnostics;
using RoboB3.Domain.Common;

namespace RoboB3.Api.Infrastructure;

/// <summary>Traduz violações de regra de negócio em 400 (ProblemDetails) sem vazar detalhes internos.</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Regra de negócio violada",
                Detail = domainException.Message,
            },
        });
    }
}
