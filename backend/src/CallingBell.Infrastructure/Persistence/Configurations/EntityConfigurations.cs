using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CallingBell.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(u => u.DisplayName).HasMaxLength(120);
        b.Property(u => u.UserType).HasMaxLength(32);
        b.HasOne(u => u.City).WithMany().HasForeignKey(u => u.CityId);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(t => t.TokenHash).HasMaxLength(64);
        b.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
        b.HasIndex(t => t.TokenHash);
    }
}

internal sealed class GeoConfiguration :
    IEntityTypeConfiguration<City>, IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<City> b)
    {
        b.Property(c => c.Latitude).HasPrecision(9, 6);
        b.Property(c => c.Longitude).HasPrecision(9, 6);
        b.HasOne(c => c.State).WithMany(s => s.Cities).HasForeignKey(c => c.StateId);
    }

    public void Configure(EntityTypeBuilder<Area> b)
    {
        b.Property(a => a.Latitude).HasPrecision(9, 6);
        b.Property(a => a.Longitude).HasPrecision(9, 6);
        b.HasOne(a => a.City).WithMany(c => c.Areas).HasForeignKey(a => a.CityId);
    }
}

internal sealed class CatalogConfiguration :
    IEntityTypeConfiguration<SubCategory>, IEntityTypeConfiguration<Media>
{
    public void Configure(EntityTypeBuilder<SubCategory> b) =>
        b.HasOne(s => s.Category).WithMany(c => c.SubCategories).HasForeignKey(s => s.CategoryId);

    public void Configure(EntityTypeBuilder<Media> b)
    {
        b.ToTable("Media");
        b.HasKey(m => m.MediaId);
    }
}

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.AverageRating).HasPrecision(3, 2);

        b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId);
        b.HasOne(x => x.Category).WithMany(c => c.Businesses).HasForeignKey(x => x.CategoryId);
        b.HasOne(x => x.SubCategory).WithMany(s => s.Businesses).HasForeignKey(x => x.SubCategoryId);
        b.HasOne(x => x.CityRef).WithMany().HasForeignKey(x => x.CityId);
        b.HasOne(x => x.AreaRef).WithMany().HasForeignKey(x => x.AreaId);

        b.HasMany(x => x.Services).WithOne(s => s.Business).HasForeignKey(s => s.BusinessId);
        b.HasMany(x => x.Hours).WithOne(h => h.Business).HasForeignKey(h => h.BusinessId);
        b.HasMany(x => x.Images).WithOne(i => i.Business).HasForeignKey(i => i.BusinessId);
        b.HasMany(x => x.Reviews).WithOne(r => r.Business).HasForeignKey(r => r.BusinessId);
        b.HasMany(x => x.Enquiries).WithOne(e => e.Business).HasForeignKey(e => e.BusinessId);
        b.HasMany(x => x.Bookings).WithOne(bk => bk.Business).HasForeignKey(bk => bk.BusinessId);
        b.HasMany(x => x.Advertisements).WithOne(a => a.Business).HasForeignKey(a => a.BusinessId);
        b.HasMany(x => x.Subscriptions).WithOne(s => s.Business).HasForeignKey(s => s.BusinessId);
    }
}

internal sealed class EngagementConfiguration :
    IEntityTypeConfiguration<Review>, IEntityTypeConfiguration<Enquiry>, IEntityTypeConfiguration<Booking>, IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Review> b) =>
        b.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerUserId);

    public void Configure(EntityTypeBuilder<Enquiry> b) =>
        b.HasOne(e => e.Service).WithMany().HasForeignKey(e => e.ServiceId);

    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerUserId);
    }

    public void Configure(EntityTypeBuilder<Favorite> b) =>
        b.HasOne(f => f.Business).WithMany().HasForeignKey(f => f.BusinessId);
}

internal sealed class MonetizationConfiguration :
    IEntityTypeConfiguration<BusinessSubscription>, IEntityTypeConfiguration<Advertisement>, IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<BusinessSubscription> b) =>
        b.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId);

    public void Configure(EntityTypeBuilder<Advertisement> b)
    {
        b.HasOne(a => a.TargetCity).WithMany().HasForeignKey(a => a.TargetCityId);
        b.HasOne(a => a.TargetCategory).WithMany().HasForeignKey(a => a.TargetCategoryId);
    }

    public void Configure(EntityTypeBuilder<Payment> b) =>
        b.HasOne(p => p.Business).WithMany().HasForeignKey(p => p.BusinessId);
}

internal sealed class AnalyticsConfiguration :
    IEntityTypeConfiguration<BusinessDailyStat>, IEntityTypeConfiguration<BusinessHour>
{
    public void Configure(EntityTypeBuilder<BusinessDailyStat> b) =>
        b.HasOne(s => s.Business).WithMany().HasForeignKey(s => s.BusinessId);

    public void Configure(EntityTypeBuilder<BusinessHour> b) =>
        b.ToTable("BusinessHours");
}
