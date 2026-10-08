/* =====================================================================================
   Calling Bell - 22_AboutContent.sql
   Content for the "About Calling Bell" page (/about): introduction, who we are, mission and
   vision, what we do (Discover, Connect, Book, Grow), who we serve, our values, the company
   behind Calling Bell (TekOrtus Pvt. Ltd.) and a closing call to action.
   Live platform numbers on the page come from the same API (BusinessGrowthStats), not from here.
   Idempotent: MERGE on MarketingContent.Code. Body text uses "\n" for paragraph breaks.
   "{country}" / "{country's}" are filled in by the page with the visitor's country (from their IP), e.g. "Canada's".
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Page nvarchar(40) = N'About';

IF OBJECT_ID('tempdb..#About') IS NOT NULL DROP TABLE #About;
CREATE TABLE #About (
    Code nvarchar(60) PRIMARY KEY, SectionKey nvarchar(40), SortOrder int, Eyebrow nvarchar(80) NULL, Title nvarchar(200), Subtitle nvarchar(400) NULL,
    Body nvarchar(max) NULL, IconKey nvarchar(40) NULL, CtaText nvarchar(60) NULL, LinkUrl nvarchar(300) NULL);

INSERT INTO #About (Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, Body, IconKey, CtaText, LinkUrl) VALUES
/* ---------- Hero ---------- */
(N'ABT-HERO', N'Hero', 1, N'About Calling Bell',
 N'{country''s} real-time network for local businesses',
 N'Calling Bell connects people with trusted local businesses and service providers, shows who is available right now, and makes it simple to chat, call, request a quotation or book in minutes.',
 NULL, NULL, N'Find services near you', N'/search'),

/* ---------- Who we are ---------- */
(N'ABT-STORY', N'Story', 1, N'Who we are',
 N'More than a directory',
 N'Discover. Connect. Book. Grow.',
 N'Calling Bell is a real-time local business discovery, lead generation and booking platform built for {country''s} cities and towns. Traditional directories stop at a name and a phone number. Calling Bell goes further: customers can see whether a business is online, busy, or available for a call, chat, video consultation or booking, and act on it straight away.\nFor customers, that means less time searching and waiting, and more confidence in who they choose. Verified profiles, transparent pricing and genuine reviews make it easy to compare providers and pick the right one.\nFor businesses and service providers, Calling Bell brings everything needed to win and serve local customers into one place: a verified profile, live availability, a lead inbox, quotations, a booking calendar, reviews, advertising and analytics. From electricians, plumbers and AC technicians to doctors, lawyers, tutors, salons and event planners, Calling Bell makes every local service as easy to find and book as shopping online.',
 NULL, NULL, NULL),

/* ---------- Mission and vision ---------- */
(N'ABT-MISSION', N'Purpose', 1, N'Our mission', N'Make every local business discoverable, reachable and bookable in real time',
 N'We give customers a faster, more trustworthy way to get things done, and we give local businesses the professional tools they need to grow, at a price that fits a local business.',
 NULL, N'bolt', NULL, NULL),
(N'ABT-VISION', N'Purpose', 2, N'Our vision', N'A connected local economy in every city in {country}',
 N'We want a world where the nearest qualified professional is always one tap away, and where every neighbourhood business, however small, can compete on service, quality and trust.',
 NULL, N'place', NULL, NULL),

/* ---------- What we do ---------- */
(N'ABT-PILLAR-DISCOVER', N'Pillar', 1, N'Discover', N'Find the right provider, fast',
 N'Search by category, service, location and live availability. Compare ratings, prices, distance and response times before you decide.', NULL, N'search', NULL, NULL),
(N'ABT-PILLAR-CONNECT', N'Pillar', 2, N'Connect', N'Talk to businesses directly',
 N'Chat, call, start a video consultation, request a callback or ask for a quotation, without leaving Calling Bell.', NULL, N'chat', NULL, NULL),
(N'ABT-PILLAR-BOOK', N'Pillar', 3, N'Book', N'Confirm a slot in minutes',
 N'Instant and scheduled bookings with confirmations, reminders, rescheduling and cancellation built in.', NULL, N'event', NULL, NULL),
(N'ABT-PILLAR-GROW', N'Pillar', 4, N'Grow', N'Help businesses grow',
 N'Leads, bookings, reviews, promotions and analytics give business owners a clear view of what is working and where to improve.', NULL, N'trending', NULL, NULL),

/* ---------- Who we serve ---------- */
(N'ABT-SERVE-CUSTOMERS', N'Audience', 1, NULL, N'Customers',
 N'Households and individuals looking for reliable local services: search nearby, check availability, read reviews, compare quotations and book with confidence.', NULL, N'groups', NULL, NULL),
(N'ABT-SERVE-BUSINESSES', N'Audience', 2, NULL, N'Business owners',
 N'Shops, clinics, studios, agencies and service companies that want a professional online presence, a steady flow of qualified leads and simple tools to manage bookings and staff.', NULL, N'storefront', NULL, NULL),
(N'ABT-SERVE-PROVIDERS', N'Audience', 3, NULL, N'Independent professionals',
 N'Electricians, tutors, trainers, consultants and other professionals who want to control their availability, accept bookings, chat with customers and track their earnings.', NULL, N'person_add', NULL, NULL),

/* ---------- Our values ---------- */
(N'ABT-VALUE-TRUST', N'Value', 1, NULL, N'Trust comes first',
 N'Verified listings, moderated reviews and transparent pricing, so customers know who they are dealing with.', NULL, N'verified', NULL, NULL),
(N'ABT-VALUE-REALTIME', N'Value', 2, NULL, N'Real-time by design',
 N'Live availability, instant messaging and timely notifications, because local needs are usually urgent.', NULL, N'notifications', NULL, NULL),
(N'ABT-VALUE-LOCAL', N'Value', 3, NULL, N'Local at heart',
 N'Built around {country''s} cities, areas and PIN codes, and around the way local businesses actually work.', NULL, N'place', NULL, NULL),
(N'ABT-VALUE-PRIVACY', N'Value', 4, NULL, N'Privacy and security',
 N'Customer details are shared only when a customer chooses to contact a business. Access is role-based and every sensitive action is logged.', NULL, N'shield', NULL, NULL),

/* ---------- The company behind Calling Bell ---------- */
(N'ABT-COMPANY', N'Company', 1, N'The company behind Calling Bell',
 N'Built and operated by TekOrtus Pvt. Ltd.',
 N'A technology company focused on products that help businesses grow.',
 N'Calling Bell is designed, developed and operated by TekOrtus Pvt. Ltd., a technology company that builds digital products for businesses. TekOrtus combines product design, software engineering and data expertise to create platforms that are reliable, secure and simple to use.\nWith Calling Bell, TekOrtus is focused on a single goal: helping local businesses compete and grow in a digital-first world, while giving customers a faster and more trustworthy way to find and book the services they need.',
 N'dashboard', NULL, NULL),

/* ---------- Closing call to action ---------- */
(N'ABT-CTA', N'Cta', 1, NULL,
 N'Find a trusted professional near you, or grow your business with Calling Bell',
 N'Join the customers and businesses already using Calling Bell.',
 NULL, NULL, N'List your business free', N'/list-your-business');

MERGE dbo.MarketingContent AS t
USING (
    SELECT Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, REPLACE(Body, N'\n', NCHAR(10)) AS Body, IconKey, CtaText, LinkUrl
    FROM #About
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
PRINT '22_AboutContent.sql completed';
DROP TABLE IF EXISTS #About;
GO
