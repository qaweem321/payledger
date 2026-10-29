using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Domain.Idempotency;

namespace PayLedger.Application.Payments;

public sealed record IdempotentResult<T>(T Value, bool Replayed);

/// <summary>
/// Runs a money movement at most once per Idempotency-Key. The key is inserted in the same
/// database transaction as the movement: a concurrent retry with the same key waits on the
/// unique index, then replays the stored result instead of moving money twice.
/// </summary>
public sealed class IdempotencyExecutor(IAppDbContext db, TimeProvider clock)
{
    public const int MaxKeyLength = 100;

    public async Task<IdempotentResult<T>?> TryReplayAsync<T>(long merchantId, string? key, string requestHash, CancellationToken ct)
    {
        if (key is null)
        {
            return null;
        }

        var record = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.MerchantId == merchantId && r.Key == key, ct);
        if (record is null)
        {
            return null;
        }

        if (record.RequestHash != requestHash)
        {
            throw new ConflictException(
                "idempotency_key_reused",
                "This Idempotency-Key was already used with a different request.");
        }

        var value = JsonSerializer.Deserialize<T>(record.ResponseJson!, JsonDefaults.Options)!;
        return new IdempotentResult<T>(value, Replayed: true);
    }

    public async Task<IdempotentResult<T>> ExecuteAsync<T>(
        long merchantId,
        string? key,
        string requestHash,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);

        IdempotencyRecord? record = null;
        if (key is not null)
        {
            record = new IdempotencyRecord(merchantId, key, requestHash, clock.GetUtcNow());
            db.IdempotencyRecords.Add(record);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
            {
                // Another request with this key committed while we waited: return its result.
                await tx.RollbackAsync(ct);
                db.ClearChanges();
                return await TryReplayAsync<T>(merchantId, key, requestHash, ct)
                    ?? throw new ConflictException("idempotency_key_in_use", "A request with this Idempotency-Key is still running.");
            }
        }

        var result = await work(ct);

        if (record is not null)
        {
            record.Complete(JsonSerializer.Serialize(result, JsonDefaults.Options));
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new IdempotentResult<T>(result, Replayed: false);
    }

    /// <summary>A fingerprint of the operation and its body, so a key cannot be reused for a different request.</summary>
    public static string Fingerprint(string operation, object request) =>
        Secrets.Hash(operation + ":" + JsonSerializer.Serialize(request, request.GetType(), JsonDefaults.Options));
}
