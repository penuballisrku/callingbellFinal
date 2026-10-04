/* =====================================================================================
   Calling Bell - 23_TrustSafetyContent.sql
   Content for the "Trust & safety" page (/trust-and-safety): our commitment, safety pillars,
   how business verification works, review standards, safety tips for customers and
   businesses, how to report a problem, FAQs and a closing call to action.
   Live platform numbers on the page come from the same API (BusinessGrowthStats), not from here.
   Idempotent: MERGE on MarketingContent.Code. Body text uses "\n" for paragraph breaks.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Page nvarchar(40) = N'TrustSafety';

IF OBJECT_ID('tempdb..#Trust') IS NOT NULL DROP TABLE #Trust;
CREATE TABLE #Trust (
    Code nvarchar(60) PRIMARY KEY, SectionKey nvarchar(40), SortOrder int, Eyebrow nvarchar(80) NULL, Title nvarchar(200), Subtitle nvarchar(400) NULL,
    Body nvarchar(max) NULL, IconKey nvarchar(40) NULL, CtaText nvarchar(60) NULL, LinkUrl nvarchar(300) NULL);

INSERT INTO #Trust (Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, Body, IconKey, CtaText, LinkUrl) VALUES
/* ---------- Hero ---------- */
(N'TS-HERO', N'Hero', 1, N'Trust & safety',
 N'A local marketplace you can rely on',
 N'Every listing, review and conversation on Calling Bell is built around one idea: customers and businesses should be able to deal with each other confidently. Here is how we keep the platform safe, fair and trustworthy.',
 NULL, NULL, N'Browse verified businesses', N'/search?verifiedOnly=true'),

/* ---------- Our commitment ---------- */
(N'TS-COMMITMENT', N'Story', 1, N'Our commitment',
 N'Trust is the foundation of every booking',
 N'Verified businesses. Genuine reviews. Protected data.',
 N'When you invite a professional into your home, visit a clinic or hand over an important job, you need to know who you are dealing with. Calling Bell exists to make that decision easier, so trust is built into every part of the platform rather than added as an afterthought.\nWe review businesses before they appear in search, show a verified badge only when a business has passed our checks, moderate reviews so ratings reflect real experiences, and protect personal information with role-based access, secure sign-in and activity logging.\nTrust also depends on everyone who uses Calling Bell. Our community standards apply equally to customers and businesses, and we act on reports of fraud, abuse, harassment or misleading information.',
 NULL, NULL, NULL),

/* ---------- Safety pillars ---------- */
(N'TS-PILLAR-VERIFIED', N'Pillar', 1, N'Verified businesses', N'Checked before they are listed',
 N'New listings are reviewed by our team before they go live. Businesses that pass verification carry a verified badge on their profile and in search results.', NULL, N'verified', NULL, NULL),
(N'TS-PILLAR-REVIEWS', N'Pillar', 2, N'Genuine reviews', N'Ratings you can believe',
 N'Reviews come from customers, are moderated against clear standards, and businesses can respond publicly. Flagged reviews are examined by our moderation team.', NULL, N'star', NULL, NULL),
(N'TS-PILLAR-SECURE', N'Pillar', 3, N'Secure platform', N'Protected accounts and data',
 N'Secure token-based sign-in, role-based permissions, request rate limiting and audit logs help keep accounts and business data safe from misuse.', NULL, N'shield', NULL, NULL),
(N'TS-PILLAR-PRIVACY', N'Pillar', 4, N'Your privacy', N'You decide who contacts you',
 N'Your contact details are shared with a business only when you choose to send an enquiry, request a callback or make a booking.', NULL, N'groups', NULL, NULL),

/* ---------- How verification works ---------- */
(N'TS-STEP-SUBMIT', N'Step', 1, NULL, N'Business submits its details',
 N'The owner provides the business name, category, address, contact information, services and photos during registration.', NULL, N'person_add', NULL, NULL),
(N'TS-STEP-REVIEW', N'Step', 2, NULL, N'Our team reviews the listing',
 N'We check that the details are complete, consistent and genuine, and that the business fits our listing standards.', NULL, N'inbox', NULL, NULL),
(N'TS-STEP-BADGE', N'Step', 3, NULL, N'Verified badge is awarded',
 N'Approved businesses go live in search with a verified badge. Listings that do not meet our standards are not published.', NULL, N'verified', NULL, NULL),
(N'TS-STEP-MONITOR', N'Step', 4, NULL, N'Ongoing monitoring',
 N'Reviews, reports and account activity are monitored continuously. Listings can be suspended or removed if they break our rules.', NULL, N'analytics', NULL, NULL),

/* ---------- Review standards ---------- */
(N'TS-REVIEW-REAL', N'Standard', 1, NULL, N'Based on real experiences',
 N'Reviews must describe a genuine interaction with the business. Fake, paid or incentivised reviews are not allowed.', NULL, N'done', NULL, NULL),
(N'TS-REVIEW-RESPECT', N'Standard', 2, NULL, N'Respectful and relevant',
 N'Abusive language, personal attacks, discrimination and personal contact details are removed.', NULL, N'chat', NULL, NULL),
(N'TS-REVIEW-NOCONFLICT', N'Standard', 3, NULL, N'No conflicts of interest',
 N'Owners and staff may not review their own business or a competitor. Ratings cannot be bought or removed for payment.', NULL, N'shield', NULL, NULL),
(N'TS-REVIEW-REPLY', N'Standard', 4, NULL, N'A fair right of reply',
 N'Businesses can respond publicly to every review, so customers see both sides of the story.', NULL, N'campaign', NULL, NULL),

/* ---------- Safety tips ---------- */
(N'TS-TIP-CUST-VERIFIED', N'TipCustomer', 1, NULL, N'Look for the verified badge',
 N'Prefer verified businesses and read recent reviews before you book.', NULL, N'verified', NULL, NULL),
(N'TS-TIP-CUST-PLATFORM', N'TipCustomer', 2, NULL, N'Keep conversations on Calling Bell',
 N'Chats, quotations and bookings made on the platform leave a clear record if anything goes wrong.', NULL, N'chat', NULL, NULL),
(N'TS-TIP-CUST-QUOTE', N'TipCustomer', 3, NULL, N'Agree the price in writing',
 N'Request a quotation and confirm the scope of work before the job starts.', NULL, N'wallet', NULL, NULL),
(N'TS-TIP-CUST-PAY', N'TipCustomer', 4, NULL, N'Be careful with advance payments',
 N'Avoid paying large amounts in advance, and never share OTPs, card PINs or banking passwords with anyone.', NULL, N'shield', NULL, NULL),
(N'TS-TIP-BIZ-PROFILE', N'TipBusiness', 1, NULL, N'Keep your profile accurate',
 N'Up-to-date prices, hours, services and availability set the right expectations and earn better reviews.', NULL, N'storefront', NULL, NULL),
(N'TS-TIP-BIZ-ACCOUNT', N'TipBusiness', 2, NULL, N'Protect your account',
 N'Use a strong password, do not share your login, and give staff only the access they need.', NULL, N'shield', NULL, NULL),
(N'TS-TIP-BIZ-REPLY', N'TipBusiness', 3, NULL, N'Respond professionally',
 N'Reply to enquiries promptly and to reviews courteously, especially critical ones.', NULL, N'chat', NULL, NULL),
(N'TS-TIP-BIZ-SCAM', N'TipBusiness', 4, NULL, N'Watch for suspicious requests',
 N'Be wary of customers who ask you to move payments off the platform or to share personal or banking details.', NULL, N'notifications', NULL, NULL),

/* ---------- Reporting ---------- */
(N'TS-REPORT', N'Report', 1, N'Something not right?',
 N'Report a problem',
 N'If you see a fake listing, a misleading or abusive review, harassment, fraud or any other misuse of Calling Bell, tell us.',
 N'Contact our support team with the business name or profile link, a short description of what happened, and screenshots if you have them. Our trust and safety team reviews every report, may contact you for more information, and will take action, which can include removing content, suspending a listing or closing an account.\nIf you are in immediate danger or believe a crime has been committed, contact the local police first by dialling 112.',
 N'campaign', NULL, NULL),

/* ---------- FAQs ---------- */
(N'TS-FAQ-BADGE', N'Faq', 1, NULL, N'What does the verified badge mean?', NULL,
 N'It means our team has reviewed the business''s listing details and approved it against our listing standards. It is a strong signal of a genuine business, but we still recommend reading reviews and agreeing prices in writing before you book.', NULL, NULL, NULL),
(N'TS-FAQ-REVIEWS', N'Faq', 2, NULL, N'Can a business remove or pay to hide negative reviews?', NULL,
 N'No. Businesses cannot delete reviews or pay to hide them. They can reply publicly, and they can report a review that breaks our standards for the moderation team to examine.', NULL, NULL, NULL),
(N'TS-FAQ-CONTACT', N'Faq', 3, NULL, N'Who can see my phone number and email?', NULL,
 N'Only the businesses you choose to contact, by sending an enquiry, requesting a callback or making a booking. Your details are not shown publicly on your reviews.', NULL, NULL, NULL),
(N'TS-FAQ-DISPUTE', N'Faq', 4, NULL, N'What happens if I have a problem with a booking?', NULL,
 N'Start by messaging the business through Calling Bell; most issues are resolved quickly that way. If that does not work, contact our support team with your booking details and we will help.', NULL, NULL, NULL),

/* ---------- Closing call to action ---------- */
(N'TS-CTA', N'Cta', 1, NULL,
 N'Book with confidence on Calling Bell',
 N'Find verified local businesses near you, compare genuine reviews and book in minutes.',
 NULL, NULL, N'List your business free', N'/list-your-business');

MERGE dbo.MarketingContent AS t
USING (
    SELECT Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, REPLACE(Body, N'\n', NCHAR(10)) AS Body, IconKey, CtaText, LinkUrl
    FROM #Trust
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
PRINT '23_TrustSafetyContent.sql completed';
DROP TABLE IF EXISTS #Trust;
GO
