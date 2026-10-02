/* =====================================================================================
   Calling Bell - 01_MasterData.sql
   Lookup values (statuses/types with display metadata), roles and permission claims.
   Idempotent: MERGE on natural keys.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------- Lookup values ---------- */
MERGE dbo.LookupValues AS t
USING (VALUES
    -- Real-time availability (the platform's primary differentiator)
    (N'AvailabilityStatus', N'Online',              N'Online',                           N'Active on Calling Bell right now',                 N'#12B76A', 1),
    (N'AvailabilityStatus', N'AvailableForCall',    N'Available for Call',               N'Ready to take phone calls',                        N'#2E90FA', 2),
    (N'AvailabilityStatus', N'AvailableForChat',    N'Available for Chat',               N'Responding to chat messages',                      N'#7A5AF8', 3),
    (N'AvailabilityStatus', N'AvailableForVideo',   N'Available for Video Consultation', N'Accepting video consultations now',                N'#EE46BC', 4),
    (N'AvailabilityStatus', N'AvailableForBooking', N'Available for Booking',            N'Open slots available for instant booking',         N'#F4A62C', 5),
    (N'AvailabilityStatus', N'Busy',                N'Busy',                             N'Currently attending to customers',                 N'#F04438', 6),
    (N'AvailabilityStatus', N'Offline',             N'Offline',                          N'Not available at the moment',                      N'#98A2B3', 7),

    (N'BusinessStatus', N'Active',          N'Active',           N'Listed and visible to customers',          N'#12B76A', 1),
    (N'BusinessStatus', N'PendingApproval', N'Pending Approval', N'Awaiting review by the Calling Bell team', N'#F79009', 2),
    (N'BusinessStatus', N'Suspended',       N'Suspended',        N'Hidden due to a policy issue',             N'#F04438', 3),
    (N'BusinessStatus', N'Inactive',        N'Inactive',         N'Temporarily closed by the owner',          N'#98A2B3', 4),

    (N'VerificationStatus', N'Verified', N'Verified', N'KYC and business documents verified', N'#12B76A', 1),
    (N'VerificationStatus', N'Pending',  N'Pending',  N'Documents under review',              N'#F79009', 2),
    (N'VerificationStatus', N'Rejected', N'Rejected', N'Documents could not be verified',     N'#F04438', 3),

    (N'BookingStatus', N'Pending',   N'Pending',   N'Waiting for the business to confirm', N'#F79009', 1),
    (N'BookingStatus', N'Confirmed', N'Confirmed', N'Confirmed by the business',           N'#2E90FA', 2),
    (N'BookingStatus', N'Completed', N'Completed', N'Service delivered',                   N'#12B76A', 3),
    (N'BookingStatus', N'Cancelled', N'Cancelled', N'Cancelled by customer or business',   N'#98A2B3', 4),
    (N'BookingStatus', N'NoShow',    N'No-show',   N'Customer did not turn up',            N'#F04438', 5),

    (N'EnquiryStatus', N'New',       N'New',       N'Not yet responded to',     N'#2E90FA', 1),
    (N'EnquiryStatus', N'Contacted', N'Contacted', N'Business has responded',   N'#7A5AF8', 2),
    (N'EnquiryStatus', N'Quoted',    N'Quoted',    N'Quotation shared',         N'#F79009', 3),
    (N'EnquiryStatus', N'Converted', N'Converted', N'Turned into a booking/job',N'#12B76A', 4),
    (N'EnquiryStatus', N'Lost',      N'Lost',      N'Customer did not proceed', N'#98A2B3', 5),

    (N'EnquiryType', N'Enquiry',   N'General Enquiry',   N'Question about services or pricing', N'#2E90FA', 1),
    (N'EnquiryType', N'Quotation', N'Quotation Request', N'Customer wants a written quote',     N'#F79009', 2),
    (N'EnquiryType', N'Callback',  N'Callback Request',  N'Customer wants a call back',         N'#7A5AF8', 3),

    (N'ReviewStatus', N'Published', N'Published', N'Visible on the business profile',  N'#12B76A', 1),
    (N'ReviewStatus', N'Pending',   N'Pending',   N'Awaiting moderation',              N'#F79009', 2),
    (N'ReviewStatus', N'Flagged',   N'Flagged',   N'Reported for review by moderators',N'#F04438', 3),
    (N'ReviewStatus', N'Rejected',  N'Rejected',  N'Removed by moderators',            N'#98A2B3', 4),

    (N'AdType', N'FeaturedListing',  N'Featured Listing',         N'Pinned in the Featured Businesses section', N'#F4A62C', 1),
    (N'AdType', N'SponsoredListing', N'Sponsored Business',       N'Shown in the Sponsored section',            N'#7A5AF8', 2),
    (N'AdType', N'HomepageBanner',   N'Homepage Banner',          N'Banner on the customer home page',          N'#2E90FA', 3),
    (N'AdType', N'SearchPromotion',  N'Search Result Promotion',  N'Boosted to the top of search results',      N'#12B76A', 4),

    (N'AdStatus', N'Active',          N'Active',           NULL, N'#12B76A', 1),
    (N'AdStatus', N'Scheduled',       N'Scheduled',        NULL, N'#2E90FA', 2),
    (N'AdStatus', N'Paused',          N'Paused',           NULL, N'#F79009', 3),
    (N'AdStatus', N'Completed',       N'Completed',        NULL, N'#98A2B3', 4),
    (N'AdStatus', N'PendingApproval', N'Pending Approval', NULL, N'#F79009', 5),
    (N'AdStatus', N'Rejected',        N'Rejected',         NULL, N'#F04438', 6),

    (N'SubscriptionStatus', N'Active',    N'Active',    NULL, N'#12B76A', 1),
    (N'SubscriptionStatus', N'Trial',     N'Trial',     NULL, N'#2E90FA', 2),
    (N'SubscriptionStatus', N'Expired',   N'Expired',   NULL, N'#98A2B3', 3),
    (N'SubscriptionStatus', N'Cancelled', N'Cancelled', NULL, N'#F04438', 4),

    (N'ServiceType', N'AtHome',       N'At your location',     N'Professional visits your home or office', N'#0E9384', 1),
    (N'ServiceType', N'InStore',      N'At business premises', N'Visit the business to avail the service', N'#2E90FA', 2),
    (N'ServiceType', N'Online',       N'Online',               N'Delivered over video or phone',           N'#7A5AF8', 3),
    (N'ServiceType', N'Consultation', N'Consultation',         N'Discovery meeting or expert advice',      N'#F79009', 4),

    (N'PaymentType', N'Subscription',      N'Subscription',       NULL, N'#0B1220', 1),
    (N'PaymentType', N'Advertisement',     N'Advertising',        NULL, N'#F4A62C', 2),
    (N'PaymentType', N'BookingCommission', N'Booking Commission', NULL, N'#2E90FA', 3),
    (N'PaymentType', N'LeadCredits',       N'Lead Credits',       NULL, N'#12B76A', 4),

    (N'PaymentOrderStatus', N'Created',   N'Awaiting payment', N'Checkout started, payment not completed', N'#F79009', 1),
    (N'PaymentOrderStatus', N'Paid',      N'Paid',             N'Payment received and verified',           N'#12B76A', 2),
    (N'PaymentOrderStatus', N'Failed',    N'Failed',           N'The payment was declined or failed',      N'#F04438', 3),
    (N'PaymentOrderStatus', N'Cancelled', N'Cancelled',        N'Checkout was closed before paying',       N'#98A2B3', 4),

    -- Social profiles a business can link. Description = example URL; its host is also the allowed domain.
    (N'SocialPlatform', N'Instagram', N'Instagram',   N'https://www.instagram.com/yourbusiness',        N'#E4405F', 1),
    (N'SocialPlatform', N'Facebook',  N'Facebook',    N'https://www.facebook.com/yourbusiness',         N'#1877F2', 2),
    (N'SocialPlatform', N'YouTube',   N'YouTube',     N'https://www.youtube.com/@yourbusiness',         N'#FF0000', 3),
    (N'SocialPlatform', N'LinkedIn',  N'LinkedIn',    N'https://www.linkedin.com/company/yourbusiness', N'#0A66C2', 4),
    (N'SocialPlatform', N'X',         N'X (Twitter)', N'https://x.com/yourbusiness',                    N'#0F1419', 5),
    (N'SocialPlatform', N'Pinterest', N'Pinterest',   N'https://www.pinterest.com/yourbusiness',        N'#BD081C', 6)
) AS s (LookupType, Code, Name, Description, ColorHex, SortOrder)
ON t.LookupType = s.LookupType AND t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR ISNULL(t.Description, N'') <> ISNULL(s.Description, N'') OR ISNULL(t.ColorHex, N'') <> ISNULL(s.ColorHex, N'') OR t.SortOrder <> s.SortOrder)
    THEN UPDATE SET Name = s.Name, Description = s.Description, ColorHex = s.ColorHex, SortOrder = s.SortOrder,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (LookupType, Code, Name, Description, ColorHex, SortOrder, IsActive, CreatedBy, CreatedOn)
         VALUES (s.LookupType, s.Code, s.Name, s.Description, s.ColorHex, s.SortOrder, 1, N'seed', SYSDATETIMEOFFSET());

/* ---------- Roles ---------- */
MERGE dbo.AspNetRoles AS t
USING (VALUES
    (N'Customer'), (N'BusinessOwner'), (N'ServiceProvider'), (N'Administrator')
) AS s (Name)
ON t.NormalizedName = UPPER(s.Name)
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, Name, NormalizedName, ConcurrencyStamp)
         VALUES (LOWER(CONVERT(nvarchar(36), NEWID())), s.Name, UPPER(s.Name), LOWER(CONVERT(nvarchar(36), NEWID())));

/* ---------- Permission claims per role (RBAC + permission-based authorization) ---------- */
;WITH rp AS (
    SELECT * FROM (VALUES
        (N'Administrator', N'businesses.manage'),
        (N'Administrator', N'categories.manage'),
        (N'Administrator', N'cities.manage'),
        (N'Administrator', N'users.manage'),
        (N'Administrator', N'reviews.moderate'),
        (N'Administrator', N'advertisements.manage'),
        (N'Administrator', N'subscriptions.manage'),
        (N'Administrator', N'analytics.view'),
        (N'BusinessOwner', N'own-business.manage'),
        (N'BusinessOwner', N'leads.manage'),
        (N'BusinessOwner', N'bookings.manage'),
        (N'BusinessOwner', N'availability.manage'),
        (N'ServiceProvider', N'bookings.manage'),
        (N'ServiceProvider', N'availability.manage'),
        (N'Customer', N'bookings.create'),
        (N'Customer', N'reviews.create')
    ) v (RoleName, Permission)
)
INSERT INTO dbo.AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
SELECT r.Id, N'permission', rp.Permission
FROM rp
JOIN dbo.AspNetRoles r ON r.NormalizedName = UPPER(rp.RoleName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetRoleClaims c WHERE c.RoleId = r.Id AND c.ClaimType = N'permission' AND c.ClaimValue = rp.Permission);

COMMIT TRANSACTION;
PRINT '01_MasterData.sql completed';
GO
