/* =====================================================================================
   Calling Bell - 11_Advertisements.sql
   Advertising campaigns (featured listings, sponsored businesses, homepage banners,
   search promotions) with delivery metrics, plus the corresponding prepaid invoices
   in dbo.Payments (18% GST).
   Idempotent: MERGE on CampaignCode / InvoiceNumber.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Today date = CAST(SWITCHOFFSET(SYSDATETIMEOFFSET(), '+05:30') AS date);

IF OBJECT_ID('tempdb..#Ad') IS NOT NULL DROP TABLE #Ad;
CREATE TABLE #Ad (Code nvarchar(20) PRIMARY KEY, Slug nvarchar(180), AdType nvarchar(32), Title nvarchar(150), Description nvarchar(500),
                  StartOffset int, EndOffset int, Budget decimal(12,2), Status nvarchar(32), TargetCity bit, TargetCategory bit);
INSERT INTO #Ad VALUES
-- Live featured listings
(N'ADV-26001', N'sparkline-electricals',            N'FeaturedListing',  N'Licensed electricians at your door in 60 minutes', N'Wiring, MCB repairs and inverter installation across Madhapur and Kondapur.', -32, 28, 24000, N'Active', 1, 1),
(N'ADV-26002', N'coolbreeze-ac-care',               N'FeaturedListing',  N'AC service from a team rated 4.6 by 300+ homes', N'Gas refill, jet cleaning and installation for all major brands.', -25, 35, 22000, N'Active', 1, 1),
(N'ADV-26003', N'dr-anitha-rao-family-clinic',      N'FeaturedListing',  N'Family physician with same-day video consults', N'Diabetes, thyroid and BP care with home visits for seniors.', -40, 20, 18000, N'Active', 1, 1),
(N'ADV-26004', N'sunrise-multispeciality-hospital', N'FeaturedListing',  N'24x7 emergency and cashless care', N'NABH-accredited 150-bed hospital in Secunderabad.', -55, 125, 90000, N'Active', 1, 1),
(N'ADV-26005', N'rao-and-associates-advocates',     N'FeaturedListing',  N'Property and family law experts', N'Consultations in person or on video with experienced advocates.', -18, 42, 20000, N'Active', 1, 1),
(N'ADV-26006', N'studio-axis-architects',           N'FeaturedListing',  N'Design your villa with an award-winning studio', N'From GHMC approvals to turnkey execution.', -21, 39, 30000, N'Active', 1, 1),
(N'ADV-26007', N'nestcraft-interiors',              N'FeaturedListing',  N'2 BHK interiors delivered in 45 days', N'Modular kitchens and wardrobes with a 10-year warranty.', -12, 48, 35000, N'Active', 1, 1),
(N'ADV-26008', N'voltcare-electrical-solutions',    N'FeaturedListing',  N'EV charger installation by BESCOM-licensed experts', N'Smart switches, rewiring and emergency repairs in South Bengaluru.', -28, 32, 24000, N'Active', 1, 1),
(N'ADV-26009', N'dr-ramesh-iyer-diabetes-care',     N'FeaturedListing',  N'Diabetes reversal programmes that work', N'Consult an endocrinologist in Jayanagar or online.', -35, 25, 20000, N'Active', 1, 1),
(N'ADV-26010', N'the-scholars-den',                 N'FeaturedListing',  N'JEE and NEET 2027 batches now open', N'IIT-alumni faculty and weekly all-India mock tests.', -15, 75, 45000, N'Active', 1, 1),
(N'ADV-26011', N'tress-and-tones-salon',            N'FeaturedListing',  N'Balayage and Olaplex by trained stylists', N'Premium unisex salon on 100 Feet Road.', -9, 51, 18000, N'Active', 1, 1),
(N'ADV-26012', N'namma-ride-cabs',                  N'FeaturedListing',  N'Fixed-fare airport cabs to KIA', N'No surge pricing, 24x7 availability.', -30, 30, 26000, N'Active', 1, 1),
(N'ADV-26013', N'bandra-smile-dental-clinic',       N'FeaturedListing',  N'Smile makeovers on Hill Road', N'Implants, veneers and child-friendly dentistry.', -20, 40, 22000, N'Active', 1, 1),
(N'ADV-26014', N'lakeview-hospital-powai',          N'FeaturedListing',  N'Cardiac and trauma care in Powai', N'Tertiary care with cashless insurance tie-ups.', -60, 120, 95000, N'Active', 1, 1),
(N'ADV-26015', N'coastal-curry-house',              N'FeaturedListing',  N'Malvani seafood, cooked fresh every day', N'Family dining in Dadar West.', -14, 46, 16000, N'Active', 1, 1),
(N'ADV-26016', N'dr-lakshmi-narayanan-child-care',  N'FeaturedListing',  N'Trusted paediatric care in Anna Nagar', N'Vaccinations, growth monitoring and teleconsultation.', -26, 34, 18000, N'Active', 1, 1),
(N'ADV-26017', N'dr-arvind-khurana-ortho-clinic',   N'FeaturedListing',  N'Knee pain? Consult a joint replacement expert', N'Arthroscopy and sports injury care in Saket.', -19, 41, 24000, N'Active', 1, 1),
(N'ADV-26018', N'sharma-kapoor-law-offices',        N'FeaturedListing',  N'Commercial disputes and arbitration', N'Experienced counsel in Connaught Place.', -33, 27, 28000, N'Active', 1, 1),
(N'ADV-26019', N'dilli-darbar-restaurant',          N'FeaturedListing',  N'Butter chicken worth the trip to CP', N'Private dining for 40 guests.', -11, 49, 16000, N'Active', 1, 1),
(N'ADV-26020', N'rangoli-weddings-and-events',      N'FeaturedListing',  N'Destination weddings in Udaipur and Jaipur', N'Planning, décor and artist management.', -24, 66, 40000, N'Active', 1, 1),
(N'ADV-26021', N'rajputana-royal-weddings',         N'FeaturedListing',  N'Royal palace weddings in Rajasthan', N'Haldi, mehendi and sangeet curated end to end.', -16, 74, 40000, N'Active', 1, 1),
(N'ADV-26022', N'sukhayu-ayurveda-spa',             N'FeaturedListing',  N'Authentic Kerala panchakarma', N'Therapies supervised by Ayurveda physicians.', -22, 38, 18000, N'Active', 1, 1),
-- Sponsored businesses
(N'ADV-26031', N'aquafix-plumbing-services',        N'SponsoredListing', N'Same-day plumbing with a 30-day warranty', N'Leaks, blockages and bathroom fittings in Kukatpally.', -10, 20, 9000, N'Active', 1, 0),
(N'ADV-26032', N'healthfirst-clinic',               N'SponsoredListing', N'OPD open 8 am to 10 pm', N'Doctors, vaccinations and sample collection in Indiranagar.', -14, 16, 9000, N'Active', 1, 0),
(N'ADV-26033', N'precision-diagnostics',            N'SponsoredListing', N'Full body check-up with reports in 6 hours', N'Home sample collection across North Bengaluru.', -8, 22, 12000, N'Active', 1, 0),
(N'ADV-26034', N'kinetic-physio-and-rehab',         N'SponsoredListing', N'Physiotherapy at home in Malad', N'Back pain, sports injuries and post-surgery rehab.', -6, 24, 8000, N'Active', 1, 0),
(N'ADV-26035', N'baner-family-dental-care',         N'SponsoredListing', N'Gentle dental care for the whole family', N'EMI options available on implants and braces.', -12, 18, 8000, N'Active', 1, 0),
(N'ADV-26036', N'safehome-pest-control',            N'SponsoredListing', N'Odourless pest control with warranty', N'Termites, cockroaches and bed bugs in Rohini.', -5, 25, 7500, N'Active', 1, 0),
(N'ADV-26037', N'pathshala-home-tutors',            N'SponsoredListing', N'Board-exam tutors in Salt Lake', N'Home and online classes for all boards.', -9, 21, 7000, N'Active', 1, 0),
(N'ADV-26038', N'woodnest-interiors',               N'SponsoredListing', N'See your home in 3D before we build it', N'Space-saving interiors in HSR Layout.', -3, 27, 12000, N'Active', 1, 0),
-- Homepage banners
(N'ADV-26041', N'sunrise-multispeciality-hospital', N'HomepageBanner',   N'Executive health check-up at Sunrise', N'80+ parameters with physician review.', -7, 23, 60000, N'Active', 0, 0),
(N'ADV-26042', N'nestcraft-interiors',              N'HomepageBanner',   N'Festive offer on modular kitchens', N'Free 3D design with every kitchen order.', -5, 25, 55000, N'Active', 0, 0),
(N'ADV-26043', N'coastal-curry-house',              N'HomepageBanner',   N'Seafood festival this month', N'Special Malvani thalis every weekend.', -4, 26, 40000, N'Active', 0, 0),
(N'ADV-26044', N'the-scholars-den',                 N'HomepageBanner',   N'Scholarship test for JEE 2028', N'Register for the free scholarship test.', 6, 36, 50000, N'Scheduled', 0, 0),
-- Search result promotions
(N'ADV-26051', N'arctic-air-services',              N'SearchPromotion',  N'Top result for AC repair in Koramangala', NULL, -13, 17, 10000, N'Active', 1, 1),
(N'ADV-26052', N'powerhouse-electricians',          N'SearchPromotion',  N'Top result for electricians in Andheri', NULL, -10, 20, 10000, N'Active', 1, 1),
(N'ADV-26053', N'shree-ganesh-electricals',         N'SearchPromotion',  N'Top result for electricians in Kothrud', NULL, -7, 23, 8000, N'Active', 1, 1),
(N'ADV-26054', N'murugan-power-electricals',        N'SearchPromotion',  N'Top result for electricians in Velachery', NULL, -15, 15, 8000, N'Active', 1, 1),
(N'ADV-26055', N'glow-and-grace-salon',             N'SearchPromotion',  N'Top result for salons in Kondapur', NULL, -4, 26, 7000, N'Active', 1, 1),
(N'ADV-26056', N'swift-city-cabs',                  N'SearchPromotion',  N'Top result for airport taxis in Hyderabad', NULL, -20, 10, 9000, N'Active', 1, 1),
(N'ADV-26057', N'capital-electric-works',           N'SearchPromotion',  N'Top result for electricians in Lajpat Nagar', NULL, -2, 28, 8000, N'Paused', 1, 1),
(N'ADV-26058', N'chilltech-ac-services',            N'SearchPromotion',  N'Top result for AC repair in Salt Lake', NULL, -18, 12, 7000, N'Paused', 1, 1),
-- Awaiting moderation / rejected
(N'ADV-26061', N'glamup-bridal-studio',             N'SponsoredListing', N'Bridal makeup packages for the wedding season', N'HD and airbrush makeup with trial.', 2, 32, 9000, N'PendingApproval', 1, 0),
(N'ADV-26062', N'framestory-photography',           N'HomepageBanner',   N'Candid wedding films and albums', N'Book your wedding date early.', 5, 35, 45000, N'PendingApproval', 0, 0),
(N'ADV-26063', N'gati-shift-packers-movers',        N'SearchPromotion',  N'Top result for packers in Chembur', NULL, 1, 31, 8000, N'PendingApproval', 1, 1),
(N'ADV-26064', N'skyline-property-advisors',        N'SponsoredListing', N'Guaranteed rental income on Powai flats', N'Claim could not be substantiated.', -3, 27, 10000, N'Rejected', 1, 0),
-- Completed campaigns (history for revenue trends)
(N'ADV-25901', N'sparkline-electricals',            N'FeaturedListing',  N'Monsoon electrical safety check', N'Earthing and wiring audit before the rains.', -150, -90, 20000, N'Completed', 1, 1),
(N'ADV-25902', N'coolbreeze-ac-care',               N'SearchPromotion',  N'Summer AC service rush', NULL, -210, -150, 12000, N'Completed', 1, 1),
(N'ADV-25903', N'sunrise-multispeciality-hospital', N'HomepageBanner',   N'World Heart Day free ECG camp', N'Free ECG and cardiologist consultation.', -120, -90, 50000, N'Completed', 0, 0),
(N'ADV-25904', N'voltcare-electrical-solutions',    N'SponsoredListing', N'Diwali lighting installation', N'Decorative lighting for homes and societies.', -330, -300, 10000, N'Completed', 1, 0),
(N'ADV-25905', N'nestcraft-interiors',              N'FeaturedListing',  N'New-year kitchen makeover', N'Modular kitchens with free installation.', -270, -210, 30000, N'Completed', 1, 1),
(N'ADV-25906', N'the-scholars-den',                 N'FeaturedListing',  N'Crash course for JEE Main session 2', N'Eight-week intensive programme.', -240, -180, 40000, N'Completed', 1, 1),
(N'ADV-25907', N'lakeview-hospital-powai',          N'HomepageBanner',   N'Monsoon fever clinic', N'Dengue and malaria testing with same-day reports.', -100, -60, 55000, N'Completed', 0, 0),
(N'ADV-25908', N'dilli-darbar-restaurant',          N'SponsoredListing', N'Winter tandoor festival', N'Special kebab platters all season.', -300, -240, 12000, N'Completed', 1, 0),
(N'ADV-25909', N'rangoli-weddings-and-events',      N'FeaturedListing',  N'Book your winter wedding early', N'Limited dates for December.', -200, -140, 35000, N'Completed', 1, 1),
(N'ADV-25910', N'namma-ride-cabs',                  N'SearchPromotion',  N'Long-weekend getaways to Coorg', NULL, -180, -150, 9000, N'Completed', 1, 1),
(N'ADV-25911', N'dr-anitha-rao-family-clinic',      N'SponsoredListing', N'Flu season consultations', N'Same-day appointments and home visits.', -90, -60, 8000, N'Completed', 1, 0),
(N'ADV-25912', N'bandra-smile-dental-clinic',       N'SearchPromotion',  N'Teeth whitening offer', NULL, -160, -130, 9000, N'Completed', 1, 1),
(N'ADV-25913', N'tress-and-tones-salon',            N'SponsoredListing', N'Bridal season packages', N'Hair and makeup trials at the salon.', -260, -220, 10000, N'Completed', 1, 0),
(N'ADV-25914', N'rajputana-royal-weddings',         N'HomepageBanner',   N'Palace wedding showcase', N'Venues across Jaipur and Udaipur.', -230, -200, 45000, N'Completed', 0, 0),
(N'ADV-25915', N'precision-diagnostics',            N'FeaturedListing',  N'Senior citizen health package', N'Discounted full-body check for 60+.', -60, -30, 15000, N'Completed', 1, 1);

;WITH src AS (
    SELECT a.*, b.Id AS BusinessId, b.CityId, b.CategoryId, b.CoverImageUrl, b.CreatedOn AS BusinessCreatedOn,
           DATEADD(DAY, a.StartOffset, @Today) AS StartDate, DATEADD(DAY, a.EndOffset, @Today) AS EndDate,
           CAST(CASE a.Status
                    WHEN N'Completed' THEN 0.92 + (ABS(CHECKSUM(a.Code)) % 8) / 100.0
                    WHEN N'Active'    THEN CAST(-a.StartOffset AS decimal(9,4)) / NULLIF(a.EndOffset - a.StartOffset, 0)
                    WHEN N'Paused'    THEN 0.6 * CAST(-a.StartOffset AS decimal(9,4)) / NULLIF(a.EndOffset - a.StartOffset, 0)
                    ELSE 0 END AS decimal(9,4)) AS Progress,
           1.5 + (ABS(CHECKSUM(a.Code, N'ctr')) % 250) / 100.0 AS CtrPct,
           CASE a.AdType WHEN N'HomepageBanner' THEN 180.0 WHEN N'FeaturedListing' THEN 140.0 ELSE 110.0 END AS Cpm
    FROM #Ad a JOIN dbo.Businesses b ON b.Slug = a.Slug
),
calc AS (
    SELECT src.*, CAST(ROUND(src.Budget * src.Progress, 2) AS decimal(12,2)) AS Spent
    FROM src
)
MERGE dbo.Advertisements AS t
USING (
    SELECT calc.*,
           CAST(calc.Spent * 1000 / calc.Cpm AS int) AS Impressions,
           CAST(calc.Spent * 1000 / calc.Cpm * calc.CtrPct / 100 AS int) AS Clicks
    FROM calc
) AS s
ON t.CampaignCode = s.Code
WHEN MATCHED AND (t.Title <> s.Title OR t.Status <> s.Status)
    THEN UPDATE SET Title = s.Title, Description = s.Description, Status = s.Status, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (CampaignCode, BusinessId, AdType, Title, Description, ImageUrl, MobileImageUrl, DesktopImageUrl, AltText,
                 TargetCityId, TargetCategoryId, StartDate, EndDate, Budget, AmountSpent, Impressions, Clicks, Status, CreatedBy, CreatedOn)
         VALUES (s.Code, s.BusinessId, s.AdType, s.Title, s.Description, s.CoverImageUrl, s.CoverImageUrl, s.CoverImageUrl, s.Title,
                 CASE WHEN s.TargetCity = 1 THEN s.CityId END, CASE WHEN s.TargetCategory = 1 THEN s.CategoryId END,
                 s.StartDate, s.EndDate, s.Budget, s.Spent, s.Impressions, s.Clicks, s.Status, N'seed',
                 TODATETIMEOFFSET(DATEADD(HOUR, -30, CAST(s.StartDate AS datetime2(0))), '+05:30'));

/* ---------- Prepaid advertising invoices ---------- */
MERGE dbo.Payments AS t
USING (
    SELECT N'INV-' + REPLACE(a.CampaignCode, N'ADV-', N'AD') AS InvoiceNumber, a.BusinessId, a.Id AS ReferenceId, a.Budget,
           CAST(ROUND(a.Budget * 0.18, 2) AS decimal(12,2)) AS Tax,
           CASE ABS(CHECKSUM(a.CampaignCode)) % 3 WHEN 0 THEN N'UPI' WHEN 1 THEN N'Card' ELSE N'NetBanking' END AS Mode,
           TODATETIMEOFFSET(DATEADD(HOUR, -26, CAST(a.StartDate AS datetime2(0))), '+05:30') AS PaidOn
    FROM dbo.Advertisements a
    WHERE a.Status IN (N'Active', N'Completed', N'Paused', N'Scheduled')
) AS s
ON t.InvoiceNumber = s.InvoiceNumber
WHEN NOT MATCHED BY TARGET
    THEN INSERT (InvoiceNumber, BusinessId, PaymentType, ReferenceId, Amount, TaxAmount, TotalAmount, PaymentMode, Status, PaidOn, CreatedBy, CreatedOn)
         VALUES (s.InvoiceNumber, s.BusinessId, N'Advertisement', s.ReferenceId, s.Budget, s.Tax, s.Budget + s.Tax, s.Mode, N'Success', s.PaidOn, N'seed', s.PaidOn);

COMMIT TRANSACTION;
PRINT '11_Advertisements.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Ad;
GO
