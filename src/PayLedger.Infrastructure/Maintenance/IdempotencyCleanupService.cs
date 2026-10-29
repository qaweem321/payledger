using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PayLedger.Infrastructure.Persistence;

namespace PayLedger.Infrastructure.Maintenance;

/// <summary>Idempotency keys are kept for 24 hours, like most payment APIs; this removes older ones hourly.</summary>
public sealed partial class IdempotencyCleanupService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
{
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = clock.GetUtcNow() - KeyLifetime;
                var removed = await db.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                LogRemoved(logger, removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed {Count} expired idempotency keys")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency key cleanup failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
