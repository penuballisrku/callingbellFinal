using System.Text.Json;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Common;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CallingBell.Infrastructure.Persistence;

/// <summary>
/// Populates audit columns, converts deletes into soft deletes and writes an AuditLogs trail
/// for business-relevant entities.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditedTypes =
    [
        typeof(Business), typeof(BusinessService), typeof(Review), typeof(Booking), typeof(Enquiry), typeof(Advertisement),
        typeof(Category), typeof(SubCategory), typeof(City), typeof(ApplicationUser), typeof(BusinessSubscription)
    ];

    private static readonly HashSet<string> IgnoredProperties =
    [
        nameof(AuditableEntity.ModifiedOn), nameof(AuditableEntity.ModifiedBy), nameof(Business.LastSeenOn), nameof(ApplicationUser.LastLoginOn),
        nameof(ApplicationUser.SecurityStamp), nameof(ApplicationUser.ConcurrencyStamp), nameof(ApplicationUser.PasswordHash),
        nameof(ApplicationUser.AccessFailedCount), nameof(ApplicationUser.LockoutEnd)
    ];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is { } context) Apply(context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context) Apply(context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = currentUser.UserId;
        var logs = new List<AuditLog>();
        var suppress = context is ApplicationDbContext { SuppressAuditLog: true };

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            switch (entry.Entity)
            {
                case AuditableEntity auditable:
                    if (entry.State == EntityState.Added) { auditable.CreatedOn = now; auditable.CreatedBy ??= userId; }
                    if (entry.State == EntityState.Modified) { auditable.ModifiedOn = now; auditable.ModifiedBy = suppress ? auditable.ModifiedBy ?? userId : userId; }
                    if (entry.State == EntityState.Deleted)
                    {
                        entry.State = EntityState.Modified;
                        auditable.IsDeleted = true;
                        auditable.ModifiedOn = now;
                        auditable.ModifiedBy = userId;
                        logs.Add(Log(entry, "Deleted", userId, now, null));
                        continue;
                    }
                    break;
                case ApplicationUser user:
                    if (entry.State == EntityState.Added) user.CreatedOn = now;
                    if (entry.State == EntityState.Modified) { user.ModifiedOn = now; user.ModifiedBy = userId; }
                    break;
            }

            if (suppress || !AuditedTypes.Contains(entry.Entity.GetType()) || entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var changes = entry.State == EntityState.Modified
                ? entry.Properties
                    .Where(p => p.IsModified && !IgnoredProperties.Contains(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue))
                    .ToDictionary(p => p.Metadata.Name, p => new { old = p.OriginalValue, @new = p.CurrentValue })
                : null;
            if (entry.State == EntityState.Modified && changes!.Count == 0) continue;

            logs.Add(Log(entry, entry.State.ToString(), userId, now, changes is null ? null : JsonSerializer.Serialize(changes)));
        }

        if (logs.Count > 0) context.Set<AuditLog>().AddRange(logs);
    }

    private AuditLog Log(EntityEntry entry, string action, string? userId, DateTimeOffset now, string? changes) => new()
    {
        UserId = userId,
        Action = action,
        EntityName = entry.Entity.GetType().Name,
        EntityId = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString(),
        Changes = changes,
        IpAddress = currentUser.IpAddress,
        CreatedOn = now
    };
}
