using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayLedger.Domain.Webhooks;
using PayLedger.Infrastructure.Persistence;

namespace PayLedger.Infrastructure.Webhooks;

/// <summary>
/// Sends queued webhook events. Several API instances can run this safely: each claims a batch with
/// READPAST, so rows another instance is working on are skipped, and pushes the claimed rows'
/// NextAttemptAt forward as a lease, so a crashed instance's batch is retried later.
/// </summary>
public sealed partial class WebhookDispatcher(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    TimeProvider clock,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "webhooks";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, clock);
        do
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // A full batch means more are waiting: keep going without sleeping.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Sends one batch of due deliveries and returns how many were claimed.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();

        var batch = await db.WebhookDeliveries
            .FromSql($"""
                UPDATE TOP ({options.Value.BatchSize}) d
                SET d.NextAttemptAt = {now + Lease}
                OUTPUT inserted.*
                FROM WebhookDeliveries d WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE d.DeliveredAt IS NULL AND d.NextAttemptAt <= {now} AND d.Attempts < {WebhookDelivery.MaxAttempts}
                """)
            .AsNoTracking()
            .ToListAsync(ct);
        if (batch.Count == 0)
        {
            return 0;
        }

        var merchantIds = batch.Select(d => d.MerchantId).Distinct().ToList();
        var endpoints = await db.Merchants.AsNoTracking()
            .Where(m => merchantIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => (m.WebhookUrl, m.WebhookSecret), ct);

        var results = await Task.WhenAll(batch.Select(d => SendAsync(d, endpoints.GetValueOrDefault(d.MerchantId), ct)));

        var delivered = results.Where(r => r.Error is null).Select(r => r.Delivery.Id).ToList();
        if (delivered.Count > 0)
        {
            var at = clock.GetUtcNow();
            await db.WebhookDeliveries.Where(d => delivered.Contains(d.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.DeliveredAt, at).SetProperty(d => d.LastError, (string?)null), ct);
        }

        // Each failure gets its own backoff, so these updates are per row (failures are rare).
        foreach (var (delivery, error) in results.Where(r => r.Error is not null))
        {
            var attempts = delivery.Attempts + 1;
            var next = clock.GetUtcNow() + WebhookDelivery.Backoff(attempts);
            var reason = error ?? "";
            reason = reason[..Math.Min(reason.Length, 500)];
            await db.WebhookDeliveries.Where(d => d.Id == delivery.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Attempts, attempts)
                    .SetProperty(d => d.NextAttemptAt, next)
                    .SetProperty(d => d.LastError, reason), ct);
        }

        return batch.Count;
    }

    private async Task<(WebhookDelivery Delivery, string? Error)> SendAsync(
        WebhookDelivery delivery,
        (string? Url, string? Secret) endpoint,
        CancellationToken ct)
    {
        if (endpoint.Url is null || endpoint.Secret is null)
        {
            return (delivery, "Webhook was removed before delivery.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(WebhookSigner.HeaderName, WebhookSigner.Sign(endpoint.Secret, clock.GetUtcNow().ToUnixTimeSeconds(), delivery.Payload));
            request.Headers.Add("PayLedger-Event", delivery.EventType);
            request.Headers.Add("PayLedger-Delivery", delivery.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? (delivery, null)
                : (delivery, $"Receiver answered HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LogSendFailed(logger, delivery.Id, ex.Message);
            return (delivery, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook delivery {DeliveryId} failed: {Reason}")]
    private static partial void LogSendFailed(ILogger logger, long deliveryId, string reason);
}
