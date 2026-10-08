using CallingBell.Application.Common.Interfaces;
using CallingBell.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Infrastructure.Search;

/// <summary>
/// One UPDATE in the database (SearchCount = SearchCount + 1), so concurrent searches are all counted and nothing is read first. It
/// bypasses the audit columns on purpose: a search isn't an edit of the entry.
/// </summary>
public sealed class PopularSearchCounter(ApplicationDbContext db) : IPopularSearchCounter
{
    public Task RecordAsync(string searchText, CancellationToken ct)
    {
        var text = searchText.Trim();
        if (text.Length is 0 or > 160) return Task.CompletedTask;
        return db.PopularSearches
            .Where(p => p.SearchText == text && p.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.SearchCount, p => p.SearchCount + 1)
                .SetProperty(p => p.LastSearchedOn, DateTimeOffset.UtcNow), ct);
    }
}
