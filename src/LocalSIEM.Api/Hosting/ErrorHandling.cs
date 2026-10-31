using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using LocalSIEM.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LocalSIEM.Api.Hosting;

public sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case RequestValidationException validation:
                problem = new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = validation.Message
                };
                break;
            case NotFoundException:
                problem = Create(StatusCodes.Status404NotFound, exception.Message);
                break;
            case ConflictException:
                problem = Create(StatusCodes.Status409Conflict, exception.Message);
                break;
            case ForbiddenException:
                problem = Create(StatusCodes.Status403Forbidden, exception.Message);
                break;
            case AuthenticationFailedException:
                problem = Create(StatusCodes.Status401Unauthorized, exception.Message);
                break;
            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                problem = Create(499, "Запрос отменён клиентом");
                break;
            default:
                logger.LogError(exception, "Необработанная ошибка {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = Create(StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера");
                break;
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Create(int status, string title) => new() { Status = status, Title = title };
}

public sealed class IngestRateLimiter(IConfiguration configuration) : IDisposable
{
    private readonly int eventsPerSecond = Math.Max(1, configuration.GetValue("RateLimits:IngestEventsPerSecond", 1000));
    private readonly ConcurrentDictionary<Guid, TokenBucketRateLimiter> limiters = new();

    public bool TryAcquire(Guid sourceId, int events)
    {
        var limiter = limiters.GetOrAdd(sourceId, _ => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = eventsPerSecond * 2,
            TokensPerPeriod = eventsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
        using var lease = limiter.AttemptAcquire(Math.Min(Math.Max(events, 1), eventsPerSecond * 2));
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        foreach (var limiter in limiters.Values)
        {
            limiter.Dispose();
        }
    }
}
