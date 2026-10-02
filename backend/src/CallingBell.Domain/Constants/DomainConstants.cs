namespace CallingBell.Domain.Constants;

/// <summary>
/// Codes stored in the database. Display names, colours and ordering come from the LookupValues table;
/// these constants exist only so business rules can refer to a code without magic strings.
/// </summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string BusinessOwner = "BusinessOwner";
    public const string ServiceProvider = "ServiceProvider";
    public const string Administrator = "Administrator";
}

public static class Permissions
{
    public const string ClaimType = "permission";

    public const string BusinessesManage = "businesses.manage";
    public const string CategoriesManage = "categories.manage";
    public const string CitiesManage = "cities.manage";
    public const string UsersManage = "users.manage";
    public const string ReviewsModerate = "reviews.moderate";
    public const string AdvertisementsManage = "advertisements.manage";
    public const string SubscriptionsManage = "subscriptions.manage";
    public const string AnalyticsView = "analytics.view";

    public const string OwnBusinessManage = "own-business.manage";
    public const string LeadsManage = "leads.manage";
    public const string BookingsManage = "bookings.manage";
    public const string AvailabilityManage = "availability.manage";

    public const string BookingsCreate = "bookings.create";
    public const string ReviewsCreate = "reviews.create";
}

public static class LookupTypes
{
    public const string AvailabilityStatus = "AvailabilityStatus";
    public const string BusinessStatus = "BusinessStatus";
    public const string VerificationStatus = "VerificationStatus";
    public const string BookingStatus = "BookingStatus";
    public const string EnquiryStatus = "EnquiryStatus";
    public const string EnquiryType = "EnquiryType";
    public const string ReviewStatus = "ReviewStatus";
    public const string AdType = "AdType";
    public const string AdStatus = "AdStatus";
    public const string SubscriptionStatus = "SubscriptionStatus";
    public const string ServiceType = "ServiceType";
}

public static class BusinessStatuses
{
    public const string Active = "Active";
    public const string PendingApproval = "PendingApproval";
    public const string Suspended = "Suspended";
    public const string Inactive = "Inactive";
}

public static class VerificationStatuses
{
    public const string Verified = "Verified";
    public const string Pending = "Pending";
    public const string Rejected = "Rejected";
}

public static class AvailabilityStatuses
{
    public const string Offline = "Offline";
    public const string Online = "Online";
    public const string Busy = "Busy";
}

public static class BookingStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
}

public static class EnquiryStatuses
{
    public const string New = "New";
    public const string Contacted = "Contacted";
    public const string Quoted = "Quoted";
    public const string Converted = "Converted";
    public const string Lost = "Lost";
}

public static class ReviewStatuses
{
    public const string Published = "Published";
    public const string Pending = "Pending";
    public const string Flagged = "Flagged";
    public const string Rejected = "Rejected";
}

public static class AdStatuses
{
    public const string Active = "Active";
    public const string PendingApproval = "PendingApproval";
    public const string Rejected = "Rejected";
}

public static class AdTypes
{
    public const string FeaturedListing = "FeaturedListing";
    public const string SponsoredListing = "SponsoredListing";
    public const string HomepageBanner = "HomepageBanner";
    public const string SearchPromotion = "SearchPromotion";
}

public static class SubscriptionStatuses
{
    public const string Active = "Active";
    public const string Trial = "Trial";
}
