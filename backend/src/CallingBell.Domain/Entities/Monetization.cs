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
