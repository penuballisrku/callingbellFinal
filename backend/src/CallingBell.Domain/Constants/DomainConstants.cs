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
    public const string SocialPlatform = "SocialPlatform";
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
    public const string Cancelled = "Cancelled";
}

public static class PaymentOrderStatuses
{
    public const string Created = "Created";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class PlanCodes
{
    public const string Free = "FREE";
}

/// <summary>dbo.Media.EntityType values for business media.</summary>
public static class MediaEntityTypes
{
    public const string BusinessLogo = "BusinessLogo";
    public const string BusinessCover = "BusinessCover";
    public const string BusinessGallery = "BusinessGallery";
    public const string BusinessVideo = "BusinessVideo";
    public const string BusinessVideoPoster = "BusinessVideoPoster";
}

/// <summary>Notification routes: what happened, which decides the channels tried (dbo.NotificationRoutingRules).</summary>
public static class NotificationRoutes
{
    public const string Chat = "CHAT";
    public const string Video = "VIDEO";
    public const string NewLead = "NEW_LEAD";
    public const string Booking = "BOOKING";
    public const string Otp = "OTP";
}

public static class NotificationChannels
{
    public const string WebPush = "WEB_PUSH";
    public const string WhatsApp = "WHATSAPP";
    /// <summary>WhatsApp authentication template (one-time codes, with a copy-code button).</summary>
    public const string WhatsAppAuthentication = "WHATSAPP_AUTHENTICATION";
    public const string Rcs = "RCS";
    public const string Sms = "SMS";
}

/// <summary>
/// Where a delivery attempt stands. Accepted means the provider took the message, not that the person received it; Sent, Delivered and
/// Read come from provider webhooks (or, for web push, the browser's acknowledgement). Unavailable = the channel couldn't be used at all
/// (no device, no number, provider not configured).
/// </summary>
public static class DeliveryStatuses
{
    public const string Queued = "Queued";
    public const string Accepted = "Accepted";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Read = "Read";
    public const string Failed = "Failed";
    public const string Expired = "Expired";
    public const string Unavailable = "Unavailable";

    /// <summary>The provider took it: the route stops here.</summary>
    public static bool IsSuccess(string status) => status is Accepted or Sent or Delivered or Read;
}

/// <summary>A notification's progress through its route (in-app notifications without a route have none).</summary>
public static class DispatchStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    /// <summary>A channel accepted it.</summary>
    public const string Completed = "Completed";
    /// <summary>Every channel was tried and none could send it.</summary>
    public const string Exhausted = "Exhausted";
}
