/* =====================================================================================
   Calling Bell - 14_MarketingContent.sql
   Content for the "List your business" page (/list-your-business): company overview,
   objectives, services & key offerings, unique selling points, photo gallery, videos,
   onboarding steps, owner testimonials, roadmap and FAQs.
   Copy is drawn from the Calling Bell business plan; plan-tier labels match SubscriptionPlans.
   Photos and video posters are attached by 15_MarketingMedia.sql.
   Testimonials reference real businesses (FK) so their live leads, bookings, rating and plan are shown.
   Idempotent: MERGE on MarketingContent.Code. Body text uses "\n" for paragraph / line breaks.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Page nvarchar(40) = N'ListYourBusiness';

IF OBJECT_ID('tempdb..#Mc') IS NOT NULL DROP TABLE #Mc;
CREATE TABLE #Mc (
    Code nvarchar(60) PRIMARY KEY, SectionKey nvarchar(40), SortOrder int, Eyebrow nvarchar(80) NULL, Title nvarchar(200), Subtitle nvarchar(400) NULL,
    Body nvarchar(max) NULL, IconKey nvarchar(40) NULL, AltText nvarchar(300) NULL, VideoUrl nvarchar(500) NULL, MediaCredit nvarchar(200) NULL,
    MediaCreditUrl nvarchar(500) NULL, CtaText nvarchar(60) NULL, LinkUrl nvarchar(300) NULL, BusinessSlug nvarchar(160) NULL, IsActive bit NOT NULL DEFAULT 1);

INSERT INTO #Mc (Code, SectionKey, SortOrder, Eyebrow, Title, Subtitle, Body, IconKey, AltText, VideoUrl, MediaCredit, MediaCreditUrl, CtaText, LinkUrl, BusinessSlug) VALUES
/* ---------- Hero ---------- */
(N'LYB-HERO', N'Hero', 1, N'For businesses & service providers',
 N'Put your business on India''s real-time local network',
 N'Customers nearby can see when you''re available, message or call you instantly, request a quote and book in minutes. You get the leads, bookings and insights to grow.',
 NULL, NULL, N'Shopkeeper smiling outside his store on a busy Indian street', NULL,
 N'Photo: Samyuktha Nair on Unsplash', N'https://unsplash.com/photos/r4YUKKh96rM', N'List your business free', N'/register?type=business', NULL),

/* ---------- Company overview ---------- */
(N'LYB-OVERVIEW', N'Overview', 1, N'About Calling Bell',
 N'More than a directory: a growth platform for local businesses',
 N'Discover. Connect. Book. Grow.',
 N'Calling Bell is a real-time local business discovery, lead generation and booking platform built for India''s cities. Customers don''t just find you; they see whether you''re available right now, chat or call instantly, request a quotation, book a slot or start a video consultation.\nFor business owners, Calling Bell brings everything needed to win and serve local customers into one place: a verified profile, live availability, a lead inbox, a booking calendar, reviews, advertising and analytics. Whether you run a one-person repair service or a multi-branch clinic, you get the professional tools that large brands use, at a price that fits a local business.',
 NULL, N'Business team discussing growth plans in a Bengaluru office', NULL,
 N'Photo: Smartworks Coworking on Unsplash', N'https://unsplash.com/photos/cW4lLTavU80', NULL, NULL, NULL),

/* ---------- Business objectives (what we help you achieve) ---------- */
(N'LYB-OBJ-DISCOVER', N'Objective', 1, NULL, N'Get discovered by nearby customers',
 N'Appear in category, location and "near me" searches across your city, with filters for rating, distance and live availability.', NULL, N'search', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OBJ-CONVERT', N'Objective', 2, NULL, N'Turn enquiries into confirmed bookings',
 N'Reply to enquiries, send quotations and accept bookings from one inbox, so fewer leads slip through the cracks.', NULL, N'event', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OBJ-TRUST', N'Objective', 3, NULL, N'Build a reputation customers trust',
 N'Verified badges, genuine reviews and your public replies give new customers the confidence to choose you.', NULL, N'verified', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OBJ-GROW', N'Objective', 4, NULL, N'Grow with measurable results',
 N'Track profile views, leads, bookings, revenue and conversion rate, and see what is working week by week.', NULL, N'trending', NULL, NULL, NULL, NULL, NULL, NULL, NULL),

/* ---------- Services & key offerings (Eyebrow = plan that includes it) ---------- */
(N'LYB-OFF-PROFILE', N'Offering', 1, N'All plans', N'Verified business profile',
 N'Showcase services with prices, photos, working hours, service areas and languages on a profile designed to convert visitors into customers.', NULL, N'storefront', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-LEADS', N'Offering', 2, N'All plans', N'Lead inbox & quotations',
 N'Every enquiry, callback request and quotation request lands in one inbox with the customer''s need, budget and preferred date.', NULL, N'inbox', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-REVIEWS', N'Offering', 3, N'All plans', N'Reviews & reputation',
 N'Collect ratings from real customers, reply publicly and report abuse. Moderation keeps every review genuine.', NULL, N'star', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-AVAILABILITY', N'Offering', 4, N'Silver and above', N'Real-time availability',
 N'Show customers when you''re online, busy or available for a call, chat, video consultation or booking. "Available now" searches surface you first.', NULL, N'bolt', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-BOOKINGS', N'Offering', 5, N'Silver and above', N'Online booking calendar',
 N'Accept instant bookings, reschedule or cancel in a click, and send automatic reminders so customers turn up on time.', NULL, N'calendar', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-CHAT', N'Offering', 6, N'Silver and above', N'Chat, call & WhatsApp',
 N'Customers reach you with one tap. Chat with file and image sharing, read receipts and instant notifications.', NULL, N'chat', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-VIDEO', N'Offering', 7, N'Gold and above', N'Video consultations',
 N'Consult, advise or quote remotely. Ideal for doctors, lawyers, tutors, designers and consultants.', NULL, N'videocam', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-ADS', N'Offering', 8, N'Gold and above', N'Featured listings & advertising',
 N'Reach more customers with featured placement on category pages, sponsored search results and homepage banners.', NULL, N'campaign', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-ANALYTICS', N'Offering', 9, N'Advanced on Gold', N'Analytics dashboard',
 N'Profile views, lead count, bookings, revenue and conversion rate, with trends that show where your growth comes from.', NULL, N'analytics', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-OFF-STAFF', N'Offering', 10, N'Platinum and above', N'Staff & multi-location',
 N'Assign bookings to team members, manage their schedules and run multiple branches from a single account.', NULL, N'groups', NULL, NULL, NULL, NULL, NULL, NULL, NULL),

/* ---------- Unique selling points ---------- */
(N'LYB-USP-REALTIME', N'Highlight', 1, N'Our difference', N'The real-time availability network',
 N'Customers see who is free right now, not just who exists. Being available becomes your competitive advantage.', NULL, N'bolt', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-USP-FREE', N'Highlight', 2, NULL, N'Free to start, pay only to grow',
 N'List free with no time limit. Upgrade to Silver, Gold, Platinum or Enterprise when you want more leads and better placement.', NULL, N'wallet', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-USP-ALERTS', N'Highlight', 3, NULL, N'Instant lead alerts',
 N'Get notified the moment a customer enquires or books, so you can respond in minutes and win the job.', NULL, N'notifications', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-USP-TRUST', N'Highlight', 4, NULL, N'A verified, trusted marketplace',
 N'Document verification and moderated reviews keep Calling Bell credible for customers and fair for businesses.', NULL, N'shield', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-USP-ONE', N'Highlight', 5, NULL, N'One dashboard for everything',
 N'Leads, bookings, staff, availability, reviews, advertising and analytics together. No more juggling notebooks and chat threads.', NULL, N'dashboard', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-USP-LOCAL', N'Highlight', 6, NULL, N'Built for India''s cities',
 N'Search by city, area and pincode, with mobile-first profiles, call and WhatsApp buttons, and UPI payments.', NULL, N'place', NULL, NULL, NULL, NULL, NULL, NULL, NULL),

/* ---------- Photo gallery: businesses that grow on Calling Bell ---------- */
(N'LYB-GAL-RETAIL', N'Gallery', 1, N'Retail', N'Kirana & neighbourhood stores', N'Jodhpur', NULL, NULL,
 N'Elderly shopkeeper sitting in his small, well-stocked store', NULL, N'Photo: Anil Reddy on Unsplash', N'https://unsplash.com/photos/fyB7L3uMUnQ', NULL, N'/categories', NULL),
(N'LYB-GAL-FOOD', N'Gallery', 2, N'Food', N'Restaurants & street food', NULL, NULL, NULL,
 N'Cook preparing fresh snacks at a street food stall', NULL, N'Photo: Akshay Mehta on Unsplash', N'https://unsplash.com/photos/qhsVnWNCOQo', NULL, N'/search?sub=restaurants', NULL),
(N'LYB-GAL-HEALTH', N'Gallery', 3, N'Healthcare', N'Doctors & clinics', NULL, NULL, NULL,
 N'Doctor with a stethoscope standing with his arms crossed', NULL, N'Photo: vaibhav vivian on Unsplash', N'https://unsplash.com/photos/3HIroMoyre8', NULL, N'/search?sub=doctors', NULL),
(N'LYB-GAL-HOME', N'Gallery', 4, N'Home services', N'Electricians & technicians', NULL, NULL, NULL,
 N'Electrician using a screwdriver on an electrical panel', NULL, N'Photo: Raze Solar on Unsplash', N'https://unsplash.com/photos/GXLPLG3_Vf4', NULL, N'/search?sub=electrical', NULL),
(N'LYB-GAL-SHOP', N'Gallery', 5, N'Retail', N'Snacks & general stores', N'Mumbai', NULL, NULL,
 N'Vendor at a small shop stocked with snacks and drinks in Mumbai', NULL, N'Photo: Saad Ahmad on Unsplash', N'https://unsplash.com/photos/BYQrA_uhX2Y', NULL, N'/categories', NULL),
(N'LYB-GAL-CONSULT', N'Gallery', 6, N'Consultations', N'Video consultations', NULL, NULL, NULL,
 N'Smiling young doctor in a white coat with a stethoscope', NULL, N'Photo: Fotos on Unsplash', N'https://unsplash.com/photos/H9lg5Noj660', NULL, N'/search?sub=doctors&video=true', NULL),

/* ---------- Videos (hosted on Mixkit's CDN; posters stored in Media) ---------- */
(N'LYB-VID-MOBILE', N'Video', 1, N'See it in action', N'Run your business from anywhere',
 N'Answer enquiries, confirm bookings and update your availability from your phone, wherever work takes you.', NULL, NULL,
 N'Business owner taking a customer call while working on her laptop at a café', N'https://assets.mixkit.co/videos/49287/49287-720.mp4',
 N'Video: Mixkit', N'https://mixkit.co/free-stock-video/muslim-girl-managing-her-business-from-an-outdoor-caf%C3%A9-49287/', NULL, NULL, NULL),
(N'LYB-VID-ONLINE', N'Video', 2, NULL, N'Your storefront, open online 24×7',
 N'Customers can browse your services, prices and reviews and send an enquiry even after you close for the day.', NULL, NULL,
 N'Entrepreneur typing on a laptop', N'https://assets.mixkit.co/videos/42652/42652-720.mp4',
 N'Video: Mixkit', N'https://mixkit.co/free-stock-video/entrepreneur-woman-typing-on-her-laptop-42652/', NULL, NULL, NULL),
(N'LYB-VID-NUMBERS', N'Video', 3, NULL, N'Know your numbers',
 N'Revenue, leads, bookings and conversion in one dashboard, with no spreadsheets to maintain.', NULL, NULL,
 N'Business owner reviewing accounts with a calculator', N'https://assets.mixkit.co/videos/4533/4533-720.mp4',
 N'Video: Mixkit', N'https://mixkit.co/free-stock-video/woman-doing-accounts-on-a-calculator-4533/', NULL, NULL, NULL),

/* ---------- How it works ---------- */
(N'LYB-STEP-1', N'Step', 1, NULL, N'Create your free account',
 N'Sign up with your mobile number, email or Google in about two minutes.', NULL, N'person_add', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-STEP-2', N'Step', 2, NULL, N'Build your profile',
 N'Add services, prices, photos, working hours and service areas, then upload documents for verification.', NULL, N'storefront', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-STEP-3', N'Step', 3, NULL, N'Go live',
 N'Switch on real-time availability so customers can call, chat, book or consult with you.', NULL, N'bolt', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-STEP-4', N'Step', 4, NULL, N'Grow',
 N'Receive leads and bookings, collect reviews and upgrade your plan when you''re ready to scale.', NULL, N'trending', NULL, NULL, NULL, NULL, NULL, NULL, NULL),

/* ---------- Owner testimonials (Title = owner, Subtitle = role; business data is live via FK) ---------- */
(N'LYB-TST-SPARKLINE', N'Testimonial', 1, NULL, N'Venkatesh Goud', N'Founder',
 N'We used to depend only on word of mouth in our locality. Now customers can see when we''re available and book directly, and most of our weekday jobs come through Calling Bell.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, N'sparkline-electricals'),
(N'LYB-TST-ANITHA', N'Testimonial', 2, NULL, N'Dr. Anitha Rao', N'Family physician',
 N'Patients can see when I''m available for a video consultation and book a slot themselves. My front desk spends far less time on the phone, and follow-ups are much easier.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, N'dr-anitha-rao-family-clinic'),
(N'LYB-TST-ANANDAM', N'Testimonial', 3, NULL, N'Sourav Dutta', N'Principal designer',
 N'Quotation requests arrive with the customer''s budget and timeline already filled in, so our site visits go to serious projects.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, N'anandam-interiors'),
(N'LYB-TST-GLOWGRACE', N'Testimonial', 4, NULL, N'Swathi Kandukuri', N'Owner',
 N'We started on the free plan just to try it. The profile, reviews and booking requests brought us new regular clients within the first month.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, N'glow-and-grace-salon'),

/* ---------- Roadmap (from the business plan) ---------- */
(N'LYB-ROAD-1', N'Roadmap', 1, N'Live', N'Directory, search, leads & bookings',
 N'Verified listings, city-wide search, enquiries, quotations and online bookings.', NULL, N'done', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-ROAD-2', N'Roadmap', 2, N'Live', N'Real-time availability, chat & notifications',
 N'Live presence, instant messaging, booking and lead alerts, and staff management.', NULL, N'done', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-ROAD-3', N'Roadmap', 3, N'In progress', N'Video consultation & smart matching',
 N'Video consultations, AI-powered recommendations and smart lead matching.', NULL, N'progress', NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-ROAD-4', N'Roadmap', 4, N'Planned', N'Mobile apps & expansion',
 N'Android and iOS apps, multi-country support and enterprise programmes.', NULL, N'planned', NULL, NULL, NULL, NULL, NULL, NULL, NULL),

/* ---------- FAQs (Title = question, Body = answer) ---------- */
(N'LYB-FAQ-FREE', N'Faq', 1, NULL, N'Is it really free to list my business?', NULL,
 N'Yes. The Free plan includes a business profile with contact details, up to 5 services and 5 photos, 5 lead credits a month, customer reviews and basic analytics, with no time limit. Paid plans add more lead credits, real-time availability, online bookings, featured placement and advanced analytics.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-FAQ-FIND', N'Faq', 2, NULL, N'How will customers find my business?', NULL,
 N'Customers search by category, service, city, area and availability, for example "electricians near me" or "salons open now". Your rating, distance, responsiveness and plan all affect where you appear, and featured or sponsored placements put you in front of even more customers.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-FAQ-LEADS', N'Faq', 3, NULL, N'What is a lead credit?', NULL,
 N'A lead credit is used when you receive a customer''s enquiry, quotation or callback request with their contact details. Each plan includes a monthly allowance, from 5 on Free to 1,000 on Enterprise.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-FAQ-VERIFY', N'Faq', 4, NULL, N'How does verification work?', NULL,
 N'Upload your business documents from the business dashboard. Our team reviews them, and once they are approved a Verified badge appears on your profile and in search results.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-FAQ-AVAIL', N'Faq', 5, NULL, N'What is real-time availability?', NULL,
 N'It lets you tell customers whether you''re Online, Busy, or available for a call, chat, video consultation or booking right now. Businesses that are available appear in "available now" searches and are contacted more often.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),
(N'LYB-FAQ-PLAN', N'Faq', 6, NULL, N'Can I change or cancel my plan later?', NULL,
 N'Yes. You can upgrade, downgrade or switch between monthly and annual billing from the Subscription section of your business dashboard. Annual billing gives you two months free.',
 NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL),

/* ---------- Closing call to action ---------- */
(N'LYB-CTA', N'Cta', 1, NULL, N'Ready to grow with Calling Bell?',
 N'Create your free business profile today. It takes a few minutes, and customers in your city are searching right now.',
 NULL, NULL, NULL, NULL, NULL, NULL, N'List your business free', N'/register?type=business', NULL);

IF EXISTS (SELECT 1 FROM #Mc s WHERE s.BusinessSlug IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Businesses b WHERE b.Slug = s.BusinessSlug))
    THROW 50014, 'A testimonial references a business slug that does not exist. Run 04_Businesses.sql first.', 1;

MERGE dbo.MarketingContent AS t
USING (
    SELECT s.Code, s.SectionKey, s.SortOrder, s.Eyebrow, s.Title, s.Subtitle, REPLACE(s.Body, N'\n', NCHAR(10)) AS Body, s.IconKey, s.AltText,
           s.VideoUrl, s.MediaCredit, s.MediaCreditUrl, s.CtaText, s.LinkUrl, b.Id AS BusinessId, s.IsActive
    FROM #Mc s
    LEFT JOIN dbo.Businesses b ON b.Slug = s.BusinessSlug
) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Title <> s.Title OR ISNULL(t.Subtitle, N'') <> ISNULL(s.Subtitle, N'') OR ISNULL(t.Body, N'') <> ISNULL(s.Body, N'')
                  OR ISNULL(t.Eyebrow, N'') <> ISNULL(s.Eyebrow, N'') OR ISNULL(t.IconKey, N'') <> ISNULL(s.IconKey, N'')
                  OR ISNULL(t.AltText, N'') <> ISNULL(s.AltText, N'') OR ISNULL(t.VideoUrl, N'') <> ISNULL(s.VideoUrl, N'')
                  OR ISNULL(t.MediaCredit, N'') <> ISNULL(s.MediaCredit, N'') OR ISNULL(t.LinkUrl, N'') <> ISNULL(s.LinkUrl, N'')
                  OR ISNULL(t.CtaText, N'') <> ISNULL(s.CtaText, N'') OR t.SectionKey <> s.SectionKey OR t.SortOrder <> s.SortOrder
                  OR t.IsActive <> s.IsActive OR ISNULL(t.BusinessId, '00000000-0000-0000-0000-000000000000') <> ISNULL(s.BusinessId, '00000000-0000-0000-0000-000000000000'))
    THEN UPDATE SET PageKey = @Page, SectionKey = s.SectionKey, SortOrder = s.SortOrder, Eyebrow = s.Eyebrow, Title = s.Title, Subtitle = s.Subtitle,
                    Body = s.Body, IconKey = s.IconKey, AltText = s.AltText, VideoUrl = s.VideoUrl, MediaCredit = s.MediaCredit,
                    MediaCreditUrl = s.MediaCreditUrl, CtaText = s.CtaText, LinkUrl = s.LinkUrl, BusinessId = s.BusinessId, IsActive = s.IsActive,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Code, PageKey, SectionKey, SortOrder, Eyebrow, Title, Subtitle, Body, IconKey, AltText, VideoUrl, MediaCredit, MediaCreditUrl,
                 CtaText, LinkUrl, BusinessId, IsActive, CreatedBy, CreatedOn)
         VALUES (s.Code, @Page, s.SectionKey, s.SortOrder, s.Eyebrow, s.Title, s.Subtitle, s.Body, s.IconKey, s.AltText, s.VideoUrl, s.MediaCredit,
                 s.MediaCreditUrl, s.CtaText, s.LinkUrl, s.BusinessId, s.IsActive, N'seed', DATEADD(DAY, -21, SYSDATETIMEOFFSET()));

COMMIT TRANSACTION;
PRINT '14_MarketingContent.sql completed';
DROP TABLE IF EXISTS #Mc;
GO
