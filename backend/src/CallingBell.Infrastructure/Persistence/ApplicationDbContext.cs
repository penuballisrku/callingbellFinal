using System.Linq.Expressions;
using CallingBell.Domain.Common;
using CallingBell.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Infrastructure.Persistence;

/// <summary>
/// Maps the domain onto the existing CallingBell SQL Server schema.
/// The schema is owned by the versioned scripts in /database/scripts (no EF migrations).
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    /// <summary>
    /// Set by background agents that write many rows at once (e.g. importing a country's cities): no per-row AuditLog entries, and the
    /// agent's own ModifiedBy is kept. The agent writes one summary AuditLog entry itself.
    /// </summary>
    public bool SuppressAuditLog { get; set; }

    public DbSet<State> States => Set<State>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<PopularService> PopularServices => Set<PopularService>();
    public DbSet<LookupValue> LookupValues => Set<LookupValue>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<CountryCatalog> CountryCatalogs => Set<CountryCatalog>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessService> BusinessServices => Set<BusinessService>();
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<BusinessImage> BusinessImages => Set<BusinessImage>();
    public DbSet<BusinessVideo> BusinessVideos => Set<BusinessVideo>();
    public DbSet<BusinessSocialLink> BusinessSocialLinks => Set<BusinessSocialLink>();
    public DbSet<BusinessDailyStat> BusinessDailyStats => Set<BusinessDailyStat>();
    public DbSet<PlatformDailyStat> PlatformDailyStats => Set<PlatformDailyStat>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<BusinessSubscription> BusinessSubscriptions => Set<BusinessSubscription>();
    public DbSet<Advertisement> Advertisements => Set<Advertisement>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<MarketingContent> MarketingContent => Set<MarketingContent>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(12, 2);
        builder.Properties<DateTime>().HaveColumnType("date");
        builder.Properties<TimeSpan>().HaveColumnType("time(0)");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // The database has no cascading deletes for domain tables; deletes are soft.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys())
                     .Where(fk => !fk.DeclaringEntityType.ClrType.Namespace!.StartsWith("Microsoft.AspNetCore.Identity")))
        {
            fk.DeleteBehavior = DeleteBehavior.NoAction;
        }

        // Global soft-delete filter for every auditable entity.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType) && t.ClrType != typeof(ApplicationUser)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
