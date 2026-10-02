using CallingBell.Domain.Common;

namespace CallingBell.Domain.Entities;

public class SubscriptionPlan : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public int LeadCredits { get; set; }
    public int MaxServices { get; set; }
    public int MaxImages { get; set; }
    public bool IncludesFeaturedListing { get; set; }
    public bool IncludesPrioritySupport { get; set; }
    /// <summary>Newline-separated feature bullets shown on pricing cards.</summary>
    public string Features { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? BadgeColor { get; set; }
    public bool IsPopular { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BusinessSubscription : AuditableEntity
{
    public string SubscriptionNumber { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public Guid PlanId { get; set; }
    public string BillingCycle { get; set; } = "Monthly";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Active";
    public bool AutoRenew { get; set; }

    public Business Business { get; set; } = null!;
    public SubscriptionPlan Plan { get; set; } = null!;
}

public class Advertisement : AuditableEntity
{
    public string CampaignCode { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public string AdType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? DesktopImageUrl { get; set; }
    public string? AltText { get; set; }
    public Guid? TargetCityId { get; set; }
    public Guid? TargetCategoryId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal Budget { get; set; }
    public decimal AmountSpent { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
    public string Status { get; set; } = "PendingApproval";

    public Business Business { get; set; } = null!;
    public City? TargetCity { get; set; }
    public Category? TargetCategory { get; set; }
}

public class Banner : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? CtaText { get; set; }
    public string? LinkUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? DesktopImageUrl { get; set; }
    public string? AltText { get; set; }
    public string Placement { get; set; } = "HomeHero";
    public int SortOrder { get; set; }
    public DateTime? StartsOn { get; set; }
    public DateTime? EndsOn { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A block of marketing-page content (hero, overview, offering, highlight, gallery photo, video, testimonial, FAQ...),
/// grouped by <see cref="PageKey"/> and <see cref="SectionKey"/>. Testimonials link to a real <see cref="Business"/>.
/// </summary>
public class MarketingContent : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string SectionKey { get; set; } = string.Empty;
    public string? Eyebrow { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Body { get; set; }
    public string? IconKey { get; set; }
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? DesktopImageUrl { get; set; }
    public string? AltText { get; set; }
    public string? VideoUrl { get; set; }
    public string? MediaCredit { get; set; }
    public string? MediaCreditUrl { get; set; }
    public string? CtaText { get; set; }
    public string? LinkUrl { get; set; }
    public Guid? BusinessId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Business? Business { get; set; }
}

/// <summary>
/// One online checkout attempt for a subscription plan. When the gateway confirms payment it activates a
/// <see cref="BusinessSubscription"/> and writes a <see cref="Payment"/> ledger row (invoice).
/// </summary>
public class PaymentOrder : AuditableEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public Guid PlanId { get; set; }
    public string BillingCycle { get; set; } = "Monthly";
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Gateway { get; set; } = string.Empty;
    public string? GatewayOrderId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public string? PaymentMethod { get; set; }
    public string Status { get; set; } = "Created";
    public string? FailureReason { get; set; }
    public string? Gstin { get; set; }
    public DateTimeOffset? PaidOn { get; set; }
    public Guid? SubscriptionId { get; set; }
    public string? InvoiceNumber { get; set; }
    /// <summary>Optimistic concurrency: the browser callback and the webhook may confirm the same payment at once.</summary>
    public byte[] RowVersion { get; set; } = [];

    public Business Business { get; set; } = null!;
    public SubscriptionPlan Plan { get; set; } = null!;
}

/// <summary>Platform revenue ledger: subscriptions, advertising, booking commission and lead credits.</summary>
public class Payment : AuditableEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMode { get; set; } = "UPI";
    public string Status { get; set; } = "Success";
    public DateTimeOffset PaidOn { get; set; }

    public Business Business { get; set; } = null!;
}
