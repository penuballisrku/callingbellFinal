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

internal sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> b)
    {
        b.ToTable("OtpCodes");
        b.Property(o => o.PhoneNumber).HasMaxLength(20);
        b.Property(o => o.Purpose).HasMaxLength(16);
        b.Property(o => o.CodeHash).HasMaxLength(64);
        b.Property(o => o.VerificationTokenHash).HasMaxLength(64);
        b.Property(o => o.IpAddress).HasMaxLength(64);
        b.HasIndex(o => new { o.PhoneNumber, o.CreatedAt });
        b.HasIndex(o => o.VerificationTokenHash);
    }
}

internal sealed class NotificationConfiguration :
    IEntityTypeConfiguration<Notification>, IEntityTypeConfiguration<NotificationDevice>, IEntityTypeConfiguration<NotificationDelivery>,
    IEntityTypeConfiguration<NotificationProvider>, IEntityTypeConfiguration<NotificationTemplate>, IEntityTypeConfiguration<NotificationRoutingRule>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.Property(n => n.RouteCode).HasMaxLength(32);
        b.Property(n => n.ReferenceType).HasMaxLength(32);
        b.Property(n => n.IdempotencyKey).HasMaxLength(150);
        b.Property(n => n.DispatchStatus).HasMaxLength(16);
        b.HasIndex(n => n.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
    }

    public void Configure(EntityTypeBuilder<NotificationDevice> b)
    {
        b.ToTable("NotificationDevices");
        b.Property(d => d.Token).HasMaxLength(1024);
        b.Property(d => d.TokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        b.Property(d => d.DeviceType).HasMaxLength(16);
        b.Property(d => d.Browser).HasMaxLength(40);
        b.Property(d => d.Platform).HasMaxLength(40);
        b.Property(d => d.DeactivatedReason).HasMaxLength(40);
        b.HasIndex(d => d.TokenHash).IsUnique();
        b.HasIndex(d => new { d.UserId, d.IsActive });
    }

    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("NotificationDeliveries");
        b.Property(d => d.RouteCode).HasMaxLength(32);
        b.Property(d => d.Channel).HasMaxLength(32);
        b.Property(d => d.Provider).HasMaxLength(40);
        b.Property(d => d.Status).HasMaxLength(16);
        b.Property(d => d.Recipient).HasMaxLength(40);
        b.Property(d => d.ProviderMessageId).HasMaxLength(200);
        b.Property(d => d.ErrorCode).HasMaxLength(60);
        b.Property(d => d.ErrorMessage).HasMaxLength(500);
        b.HasOne(d => d.Notification).WithMany().HasForeignKey(d => d.NotificationId);
        b.HasIndex(d => d.NotificationId);
        b.HasIndex(d => new { d.Provider, d.ProviderMessageId });
    }

    public void Configure(EntityTypeBuilder<NotificationProvider> b)
    {
        b.ToTable("NotificationProviders");
        b.Property(p => p.ProviderName).HasMaxLength(40);
        b.Property(p => p.Channel).HasMaxLength(32);
        b.Property(p => p.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(p => p.ConfigurationKey).HasMaxLength(100);
    }

    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("NotificationTemplates");
        b.Property(t => t.TemplateCode).HasMaxLength(32);
        b.Property(t => t.Channel).HasMaxLength(32);
        b.Property(t => t.LanguageCode).HasMaxLength(10);
        b.Property(t => t.TemplateName).HasMaxLength(120);
        b.Property(t => t.TemplateContent).HasMaxLength(1000);
    }

    public void Configure(EntityTypeBuilder<NotificationRoutingRule> b)
    {
        b.ToTable("NotificationRoutingRules");
        b.Property(r => r.RouteCode).HasMaxLength(32);
        b.Property(r => r.Channel).HasMaxLength(32);
    }
}

internal sealed class BusinessClaimRequestConfiguration : IEntityTypeConfiguration<BusinessClaimRequest>
{
    public void Configure(EntityTypeBuilder<BusinessClaimRequest> b)
    {
        b.ToTable("BusinessClaimRequests");
        b.Property(c => c.RequestNumber).HasMaxLength(30);
        b.Property(c => c.ClaimantName).HasMaxLength(120);
        b.Property(c => c.ClaimantPhone).HasMaxLength(20);
        b.Property(c => c.ClaimantEmail).HasMaxLength(256);
        b.Property(c => c.Message).HasMaxLength(1000);
        b.Property(c => c.SourceProvider).HasMaxLength(20);
        b.Property(c => c.SourceExternalId).HasMaxLength(300);
        b.Property(c => c.Status).HasMaxLength(16);
        b.Property(c => c.IpAddress).HasMaxLength(64);
        b.HasOne(c => c.Business).WithMany().HasForeignKey(c => c.BusinessId);
    }
}

internal sealed class CollaborationConfiguration :
    IEntityTypeConfiguration<Conversation>, IEntityTypeConfiguration<ChatMessage>, IEntityTypeConfiguration<BusinessStaff>,
    IEntityTypeConfiguration<BusinessStaffService>, IEntityTypeConfiguration<BusinessStaffHour>, IEntityTypeConfiguration<VideoRoom>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("Conversations");
        b.Property(c => c.LastMessagePreview).HasMaxLength(200);
        b.Property(c => c.LastSenderRole).HasMaxLength(16);
        b.HasOne(c => c.Business).WithMany().HasForeignKey(c => c.BusinessId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(c => new { c.BusinessId, c.CustomerUserId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }

    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("ChatMessages");
        b.Property(m => m.SenderRole).HasMaxLength(16);
        b.Property(m => m.Body).HasMaxLength(2000);
        b.Property(m => m.AttachmentName).HasMaxLength(200);
        b.Property(m => m.AttachmentContentType).HasMaxLength(100);
        b.HasQueryFilter(m => !m.IsDeleted);
        b.HasOne(m => m.Conversation).WithMany().HasForeignKey(m => m.ConversationId);
        b.HasIndex(m => new { m.ConversationId, m.SentAt });
    }

    public void Configure(EntityTypeBuilder<BusinessStaff> b)
    {
        b.ToTable("BusinessStaff");
        b.Property(s => s.FullName).HasMaxLength(120);
        b.Property(s => s.Title).HasMaxLength(80);
        b.Property(s => s.Phone).HasMaxLength(20);
        b.Property(s => s.Email).HasMaxLength(256);
        b.Property(s => s.Bio).HasMaxLength(500);
        b.Property(s => s.Languages).HasMaxLength(200);
        b.HasOne(s => s.Business).WithMany().HasForeignKey(s => s.BusinessId).OnDelete(DeleteBehavior.NoAction);
    }

    public void Configure(EntityTypeBuilder<BusinessStaffService> b)
    {
        b.ToTable("BusinessStaffServices");
        b.HasKey(x => new { x.StaffId, x.ServiceId });
        b.HasOne(x => x.Staff).WithMany(s => s.Services).HasForeignKey(x => x.StaffId);
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.NoAction);
    }

    public void Configure(EntityTypeBuilder<BusinessStaffHour> b)
    {
        b.ToTable("BusinessStaffHours");
        b.HasOne(h => h.Staff).WithMany(s => s.Hours).HasForeignKey(h => h.StaffId);
        b.HasIndex(h => new { h.StaffId, h.DayOfWeek }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<VideoRoom> b)
    {
        b.ToTable("VideoRooms");
        b.Property(v => v.Status).HasMaxLength(16);
        b.HasOne(v => v.Business).WithMany().HasForeignKey(v => v.BusinessId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(v => v.Booking).WithMany().HasForeignKey(v => v.BookingId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CountryPricingConfiguration : IEntityTypeConfiguration<CountryPricing>
{
    public void Configure(EntityTypeBuilder<CountryPricing> b)
    {
        b.ToTable("CountryPricing");
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.CountryName).HasMaxLength(80);
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Locale).HasMaxLength(20);
        b.Property(x => x.PriceMultiplier).HasPrecision(18, 6);
        b.Property(x => x.RoundingStep).HasPrecision(12, 2);
        b.Property(x => x.TaxName).HasMaxLength(20);
        b.Property(x => x.TaxRate).HasPrecision(5, 4);
        b.HasIndex(x => x.CountryCode).IsUnique();
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
        b.HasOne(a => a.ParentArea).WithMany().HasForeignKey(a => a.ParentAreaId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CatalogConfiguration :
    IEntityTypeConfiguration<SubCategory>, IEntityTypeConfiguration<PopularService>, IEntityTypeConfiguration<Media>, IEntityTypeConfiguration<CountryCatalog>,
    IEntityTypeConfiguration<PopularSearch>
{
    public void Configure(EntityTypeBuilder<PopularSearch> b)
    {
        b.ToTable("PopularSearches");
        b.Property(p => p.CountryCode).HasMaxLength(2).IsFixedLength();
        b.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId);
        b.HasOne(p => p.SubCategory).WithMany().HasForeignKey(p => p.SubCategoryId);
    }

    public void Configure(EntityTypeBuilder<SubCategory> b) =>
        b.HasOne(s => s.Category).WithMany(c => c.SubCategories).HasForeignKey(s => s.CategoryId);

    public void Configure(EntityTypeBuilder<PopularService> b)
    {
        b.ToTable("PopularServices");
        b.HasOne(p => p.SubCategory).WithMany().HasForeignKey(p => p.SubCategoryId);
    }

    public void Configure(EntityTypeBuilder<Media> b)
    {
        b.ToTable("Media");
        b.HasKey(m => m.MediaId);
    }

    public void Configure(EntityTypeBuilder<CountryCatalog> b)
    {
        b.ToTable("CountryCatalogs");
        b.HasKey(c => c.CountryCode);
    }
}

internal sealed class BusinessSlugHistoryConfiguration : IEntityTypeConfiguration<BusinessSlugHistory>
{
    public void Configure(EntityTypeBuilder<BusinessSlugHistory> b)
    {
        b.ToTable("BusinessSlugHistory");
        b.Property(h => h.Slug).HasMaxLength(180);
        b.HasIndex(h => h.Slug).IsUnique();
        b.HasOne(h => h.Business).WithMany().HasForeignKey(h => h.BusinessId);
    }
}

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        // TR_Businesses_SlugHistory (27_Seo.sql) records old slugs. SQL Server rejects EF's OUTPUT clause on a table with a trigger,
        // so EF must know about it to save Businesses another way.
        b.ToTable(t => t.HasTrigger("TR_Businesses_SlugHistory"));
        b.Property(x => x.SourceProvider).HasMaxLength(20);
        b.Property(x => x.SourceExternalId).HasMaxLength(300);
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
        b.HasMany(x => x.Videos).WithOne(v => v.Business).HasForeignKey(v => v.BusinessId);
        b.HasMany(x => x.SocialLinks).WithOne(l => l.Business).HasForeignKey(l => l.BusinessId);
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
        b.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.NoAction);
    }

    public void Configure(EntityTypeBuilder<Favorite> b) =>
        b.HasOne(f => f.Business).WithMany().HasForeignKey(f => f.BusinessId);
}

internal sealed class MonetizationConfiguration :
    IEntityTypeConfiguration<BusinessSubscription>, IEntityTypeConfiguration<Advertisement>, IEntityTypeConfiguration<Payment>,
    IEntityTypeConfiguration<MarketingContent>, IEntityTypeConfiguration<MarketingContentImage>, IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<MarketingContentImage> b)
    {
        b.ToTable("MarketingContentImages");
        b.Property(i => i.CountryCode).HasMaxLength(2).IsFixedLength();
        b.HasOne(i => i.MarketingContent).WithMany(c => c.CountryImages).HasForeignKey(i => i.MarketingContentId);
        b.HasIndex(i => new { i.MarketingContentId, i.CountryCode }).IsUnique().HasFilter("[IsDeleted] = 0");
    }

    public void Configure(EntityTypeBuilder<PaymentOrder> b)
    {
        b.Property(o => o.RowVersion).IsRowVersion();
        b.HasOne(o => o.Business).WithMany().HasForeignKey(o => o.BusinessId);
        b.HasOne(o => o.Plan).WithMany().HasForeignKey(o => o.PlanId);
    }

    public void Configure(EntityTypeBuilder<MarketingContent> b)
    {
        b.ToTable("MarketingContent");
        b.HasOne(c => c.Business).WithMany().HasForeignKey(c => c.BusinessId);
    }

    public void Configure(EntityTypeBuilder<BusinessSubscription> b)
    {
        b.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId);
        b.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
    }

    public void Configure(EntityTypeBuilder<Advertisement> b)
    {
        b.HasOne(a => a.TargetCity).WithMany().HasForeignKey(a => a.TargetCityId);
        b.HasOne(a => a.TargetCategory).WithMany().HasForeignKey(a => a.TargetCategoryId);
    }

    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.HasOne(p => p.Business).WithMany().HasForeignKey(p => p.BusinessId);
        b.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
    }
}

internal sealed class AnalyticsConfiguration :
    IEntityTypeConfiguration<BusinessDailyStat>, IEntityTypeConfiguration<BusinessHour>
{
    public void Configure(EntityTypeBuilder<BusinessDailyStat> b) =>
        b.HasOne(s => s.Business).WithMany().HasForeignKey(s => s.BusinessId);

    public void Configure(EntityTypeBuilder<BusinessHour> b) =>
        b.ToTable("BusinessHours");
}
