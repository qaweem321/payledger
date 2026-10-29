using Microsoft.EntityFrameworkCore;

namespace PayLedger.Application.Common;

/// <summary>A page of results, newest first. Pass the last item's id as <c>startingAfter</c> to get the next page.</summary>
public sealed record Page<T>(IReadOnlyList<T> Data, bool HasMore);

public sealed record PageRequest(int Limit = 20, long? StartingAfter = null)
{
    public const int MaxLimit = 100;
    public int SafeLimit => Math.Clamp(Limit, 1, MaxLimit);
}

public static class PagingExtensions
{
    /// <summary>Keyset paging on a descending id: one indexed query, no OFFSET scans.</summary>
    public static async Task<Page<TOut>> ToPageAsync<TIn, TOut>(
        this IQueryable<TIn> query,
        Func<TIn, TOut> map,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await query.Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        return new Page<TOut>(rows.Take(limit).Select(map).ToList(), hasMore);
    }
}
