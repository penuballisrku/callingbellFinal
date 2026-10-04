/* =====================================================================================
   Calling Bell - 24_ContactSupportContent.sql
   Content for the "Contact support" page (/support): help topics, contact channels, what to
   include when you contact us, support for business owners, FAQs and a closing call to action.
   Contact channels (section "Channel") are shown only when rows exist: fill in the real support
   email, phone and hours in the commented template below and re-run this script.
   Idempotent: MERGE on MarketingContent.Code. Body text uses "\n" for paragraph breaks.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Page nvarchar(40) = N'ContactSupport';

IF OBJECT_ID('tempdb..#Support') IS NOT NULL DROP TABLE #Support;
CREATE TABLE #Support (
    Code nvarchar(60) PRIMARY KEY, SectionKey nvarchar(40), SortOrder int, Eyebrow nvarchar(80) NULL, Title nvarchar(200), Subtitle nvarchar(400) NULL,
    Body nvarchar(max) NULL, IconKey nvarchar(40) NULL, CtaText nvarchar(60) NULL, LinkUrl nvarchar(300) NULL);

INSERT INTO #Support (Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, Body, IconKey, CtaText, LinkUrl) VALUES
/* ---------- Hero ---------- */
(N'CS-HERO', N'Hero', 1, N'Contact support',
 N'How can we help?',
 N'Whether you are booking a service or running a business on Calling Bell, our support team is here to help. Find quick answers below, or get in touch and we will take it from there.',
 NULL, NULL, N'Go to my account', N'/account'),

/* ---------- Help topics ---------- */
(N'CS-TOPIC-BOOKINGS', N'Topic', 1, NULL, N'Bookings & appointments',
 N'View upcoming and past bookings, cancel a booking, or check the status of an appointment.', NULL, N'event', N'View my bookings', N'/account'),
(N'CS-TOPIC-ENQUIRIES', N'Topic', 2, NULL, N'Enquiries & quotations',
 N'Track enquiries, callback requests and quotation requests you have sent to businesses.', NULL, N'inbox', N'View my requests', N'/account'),
(N'CS-TOPIC-ACCOUNT', N'Topic', 3, NULL, N'Account & sign-in',
 N'Help with signing in, your profile details and keeping your account secure.', NULL, N'shield', N'Sign in', N'/login'),
(N'CS-TOPIC-LISTING', N'Topic', 4, NULL, N'Business listings & verification',
 N'Create or update your listing, add photos and services, and understand how verification works.', NULL, N'storefront', N'List your business', N'/list-your-business'),
(N'CS-TOPIC-PLANS', N'Topic', 5, NULL, N'Plans, billing & payments',
 N'Compare plans, upgrade or change your subscription, and understand what each plan includes.', NULL, N'wallet', N'Plans & pricing', N'/pricing'),
(N'CS-TOPIC-SAFETY', N'Topic', 6, NULL, N'Reviews, safety & reporting',
 N'Learn about our review standards and how to report a fake listing, an abusive review or suspicious behaviour.', NULL, N'verified', N'Trust & safety', N'/trust-and-safety'),

/* ---------- Contact channels: uncomment, fill in the real details and re-run ----------
(N'CS-CHANNEL-EMAIL', N'Channel', 1, N'Email', N'Email support', N'<support email>', N'<support hours, e.g. Monday to Saturday, 9:00 AM to 7:00 PM IST>', N'chat', N'Send an email', N'mailto:<support email>'),
(N'CS-CHANNEL-PHONE', N'Channel', 2, N'Phone', N'Call us', N'<support phone>', N'<calling hours>', N'notifications', N'Call now', N'tel:<support phone, digits only with +91>'),
(N'CS-CHANNEL-OFFICE', N'Channel', 3, N'Registered office', N'TekOrtus Pvt. Ltd.', N'<registered office address>', NULL, N'place', NULL, NULL),
------------------------------------------------------------------------------------------ */

/* ---------- What to include ---------- */
(N'CS-PREP-ACCOUNT', N'Prepare', 1, NULL, N'Your account details',
 N'The email address or mobile number you use to sign in to Calling Bell.', NULL, N'groups', NULL, NULL),
(N'CS-PREP-REFERENCE', N'Prepare', 2, NULL, N'Booking or enquiry details',
 N'The business name, the date of the booking or enquiry, and the service involved.', NULL, N'event', NULL, NULL),
(N'CS-PREP-DESCRIBE', N'Prepare', 3, NULL, N'A clear description',
 N'What happened, what you expected, and what you would like us to do.', NULL, N'chat', NULL, NULL),
(N'CS-PREP-EVIDENCE', N'Prepare', 4, NULL, N'Screenshots or photos',
 N'Screenshots of error messages, chats or listings help us resolve your request faster.', NULL, N'analytics', NULL, NULL),

/* ---------- Business owners ---------- */
(N'CS-BUSINESS', N'Business', 1, N'For business owners',
 N'Support for your business on Calling Bell',
 N'Most day-to-day tasks can be done straight from your business dashboard.',
 N'From your dashboard you can update your profile, services and working hours, add photos and videos, set your live availability, reply to leads, manage bookings and review your analytics.\nIf your new listing is not yet visible in search, it is most likely still being reviewed by our team. Listings go live with a verified badge once they pass verification. For help with plans, verification or anything else, contact our support team with your business name.',
 N'storefront', N'Go to business dashboard', N'/business'),

/* ---------- FAQs ---------- */
(N'CS-FAQ-CANCEL', N'Faq', 1, NULL, N'How do I cancel a booking?', NULL,
 N'Sign in, open My account and go to Upcoming bookings. Choose the booking and select Cancel booking. To change the time instead, message the business through Calling Bell or cancel and book a new slot.', NULL, NULL, NULL),
(N'CS-FAQ-REQUESTS', N'Faq', 2, NULL, N'Where can I see my enquiries and quotation requests?', NULL,
 N'Sign in and open My account, then go to My requests. You will see every enquiry, callback request and quotation request you have sent, with its current status.', NULL, NULL, NULL),
(N'CS-FAQ-LISTING', N'Faq', 3, NULL, N'Why is my business not showing in search yet?', NULL,
 N'New listings are reviewed by our team before they appear in search. Once your listing is verified it goes live automatically. Completing your profile with services, photos and working hours helps the review go smoothly.', NULL, NULL, NULL),
(N'CS-FAQ-PLAN', N'Faq', 4, NULL, N'How do I change my subscription plan?', NULL,
 N'Sign in as a business owner and open the Plan section of your business dashboard. You can compare all plans on the Plans & pricing page before you decide.', NULL, NULL, NULL),
(N'CS-FAQ-REPORT', N'Faq', 5, NULL, N'How do I report a fake listing or an abusive review?', NULL,
 N'Contact our support team with the business name or profile link and a short description of the problem, plus screenshots if you have them. Our Trust & safety page explains our standards and what happens after you report.', NULL, NULL, NULL),

/* ---------- Closing call to action ---------- */
(N'CS-CTA', N'Cta', 1, NULL,
 N'Ready to find the right professional?',
 N'Search verified local businesses, compare reviews and book in minutes.',
 NULL, NULL, N'List your business free', N'/list-your-business');

MERGE dbo.MarketingContent AS t
USING (
    SELECT Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, REPLACE(Body, N'\n', NCHAR(10)) AS Body, IconKey, CtaText, LinkUrl
    FROM #Support
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.PageKey <> @Page OR t.Title <> s.Title OR ISNULL(t.Subtitle, N'') <> ISNULL(s.Subtitle, N'') OR ISNULL(t.Body, N'') <> ISNULL(s.Body, N'')
                  OR ISNULL(t.Eyebrow, N'') <> ISNULL(s.Eyebrow, N'') OR ISNULL(t.IconKey, N'') <> ISNULL(s.IconKey, N'')
                  OR ISNULL(t.CtaText, N'') <> ISNULL(s.CtaText, N'') OR ISNULL(t.LinkUrl, N'') <> ISNULL(s.LinkUrl, N'')
                  OR t.SectionKey <> s.SectionKey OR t.SortOrder <> s.SortOrder OR t.IsActive = 0 OR t.IsDeleted = 1)
    THEN UPDATE SET PageKey = @Page, SectionKey = s.SectionKey, SortOrder = s.SortOrder, Eyebrow = s.Eyebrow, Title = s.Title, Subtitle = s.Subtitle,
                    Body = s.Body, IconKey = s.IconKey, CtaText = s.CtaText, LinkUrl = s.LinkUrl, IsActive = 1, IsDeleted = 0,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Code, PageKey, SectionKey, SortOrder, Eyebrow, Title, Subtitle, Body, IconKey, CtaText, LinkUrl, IsActive, CreatedBy, CreatedOn)
         VALUES (s.Code, @Page, s.SectionKey, s.SortOrder, s.Eyebrow, s.Title, s.Subtitle, s.Body, s.IconKey, s.CtaText, s.LinkUrl, 1, N'seed', SYSDATETIMEOFFSET());

COMMIT TRANSACTION;
PRINT '24_ContactSupportContent.sql completed';
DROP TABLE IF EXISTS #Support;
GO
