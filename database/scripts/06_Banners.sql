/* =====================================================================================
   Calling Bell - 06_Banners.sql
   Marketing banners (home hero, home mid-page, search) with desktop and mobile artwork.
   Headline text lives in the Banners table and is rendered by the UI (accessible, translatable);
   the SVG artwork is decorative.
   Idempotent: MERGE on Banners.Code and media file.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Today date = CAST(SYSDATETIMEOFFSET() AS date);

IF OBJECT_ID('tempdb..#Bn') IS NOT NULL DROP TABLE #Bn;
CREATE TABLE #Bn (Code nvarchar(40) PRIMARY KEY, Title nvarchar(150), Subtitle nvarchar(300), CtaText nvarchar(40), LinkUrl nvarchar(300),
                  Placement nvarchar(32), SortOrder int, StartOffset int, EndOffset int NULL, IsActive bit, IconSub nvarchar(140), ColorHex nvarchar(9), AltText nvarchar(300));
INSERT INTO #Bn VALUES
(N'HOME-HERO-ELECTRICIANS', N'Electricians available right now',
 N'Licensed electricians across 8 cities. See who is online and book a visit in under a minute.',
 N'Find electricians', N'/search?sub=electrical', N'HomeHero', 1, -45, 60, 1, N'electrical', N'#DC6803', N'Electrician tools illustration'),
(N'HOME-HERO-VIDEO-DOCTORS', N'Consult a doctor on video, today',
 N'Experienced physicians and specialists available for video consultation, with prescriptions shared instantly.',
 N'Consult now', N'/search?sub=doctors&availability=AvailableForVideo', N'HomeHero', 2, -30, 90, 1, N'doctors', N'#0E9384', N'Stethoscope illustration'),
(N'HOME-HERO-FESTIVE-HOMES', N'Festive-ready homes start here',
 N'Deep cleaning, painting and pest control from trusted local professionals. Fixed prices, no surprises.',
 N'Explore home services', N'/categories/home-services', N'HomeHero', 3, -10, 40, 1, N'cleaning', N'#F4A62C', N'Home cleaning illustration'),
(N'HOME-MID-LIST-BUSINESS', N'Grow your business with Calling Bell',
 N'Get discovered by customers nearby, receive leads in real time and manage bookings from one dashboard.',
 N'List your business free', N'/list-your-business', N'HomeMid', 1, -120, NULL, 1, N'real-estate-agents', N'#F4A62C', N'Business growth illustration'),
(N'HOME-MID-WEDDING-SEASON', N'Plan the wedding season with confidence',
 N'Event planners, photographers and decorators with verified reviews and transparent packages.',
 N'Plan your celebration', N'/categories/events-weddings', N'HomeMid', 2, -20, 75, 1, N'event-planners', N'#C11574', N'Wedding planning illustration'),
(N'SEARCH-TOP-AC-SERVICE', N'AC servicing from trusted technicians',
 N'Multi-brand AC service, gas refill and installation with transparent rate cards.',
 N'Book AC service', N'/search?sub=ac-repair', N'SearchTop', 1, -15, 45, 1, N'ac-repair', N'#1570EF', N'Air conditioner service illustration'),
-- Expanded categories
(N'HOME-HERO-PET-CARE', N'Expert care for your pets',
 N'Vets, groomers and pet boarding near you, with verified reviews and same-day slots.',
 N'Find pet care', N'/categories/pet-care', N'HomeHero', 5, -6, 75, 1, N'veterinarians', N'#CA8504', N'Pet care illustration'),
(N'HOME-HERO-ROOFTOP-SOLAR', N'Cut your power bill with rooftop solar',
 N'MNRE-empanelled installers with a free site survey and PM Surya Ghar subsidy support.',
 N'Get a free survey', N'/search?sub=solar-installation', N'HomeHero', 6, -4, 90, 1, N'solar-installation', N'#B42318', N'Rooftop solar illustration'),
(N'HOME-HERO-TAX-EXPERTS', N'Taxes and GST, sorted by experts',
 N'Chartered accountants and tax consultants for ITR, GST returns and company compliance.',
 N'Talk to an expert', N'/categories/finance-tax', N'HomeHero', 7, -3, 60, 1, N'tax-consultants', N'#3F621A', N'Tax and accounting illustration'),
(N'HOME-MID-APPLIANCE-REPAIR', N'Appliance broke down? Get it fixed today',
 N'Washing machine, refrigerator, RO and mobile repairs at your doorstep with a service warranty.',
 N'Book a repair', N'/categories/appliance-repair', N'HomeMid', 4, -5, 60, 1, N'washing-machine-repair', N'#475467', N'Appliance repair illustration'),
(N'HOME-MID-GO-DIGITAL', N'Take your business online',
 N'Websites, Google Business Profile and social media management by local digital experts.',
 N'Find digital experts', N'/categories/it-digital', N'HomeMid', 5, -7, 90, 1, N'web-developers', N'#0E7090', N'Website development illustration'),
(N'HOME-MID-WELLNESS', N'Ayurveda and wellness for the season',
 N'Panchakarma, physiotherapy and yoga with experienced, verified practitioners.',
 N'Explore wellness', N'/search?sub=ayurveda-homeopathy', N'HomeMid', 6, -2, 75, 1, N'ayurveda-homeopathy', N'#0E9384', N'Ayurveda wellness illustration'),
(N'SEARCH-TOP-PET-GROOMING', N'A spa day for your pet',
 N'Grooming at the studio or at your gate, by certified groomers.',
 N'Book grooming', N'/search?sub=pet-grooming', N'SearchTop', 2, -5, 60, 1, N'pet-grooming', N'#CA8504', N'Pet grooming illustration'),
(N'SEARCH-TOP-HOME-SHIFTING', N'Moving home? Shift stress-free',
 N'Packing, transport and unpacking by verified movers, with transit insurance.',
 N'Get a moving quote', N'/search?sub=packers-movers', N'SearchTop', 3, -5, 60, 1, N'packers-movers', N'#0086C9', N'Packers and movers illustration'),
(N'SEARCH-TOP-COMPANY-SETUP', N'Start your company the right way',
 N'Incorporation, GST registration and ROC compliance by chartered accountants.',
 N'Talk to a CA', N'/search?sub=chartered-accountants', N'SearchTop', 4, -5, 90, 1, N'chartered-accountants', N'#3F621A', N'Company registration illustration'),
(N'SEARCH-TOP-INTERIORS', N'Interiors delivered in 45 days',
 N'Modular kitchens and wardrobes with 3D designs before work begins.',
 N'See interior designers', N'/search?sub=interior-designers', N'SearchTop', 5, -5, 75, 1, N'interior-designers', N'#4E5BA6', N'Interior design illustration'),
-- Historical / inactive banners (kept for reporting; not shown to customers)
(N'HOME-HERO-MONSOON-READY', N'Monsoon-proof your home',
 N'Waterproofing and plumbing checks before the rains.',
 N'Book a check-up', N'/search?sub=plumbing', N'HomeHero', 4, -150, -60, 1, N'plumbing', N'#0086C9', N'Monsoon home care illustration'),
(N'HOME-MID-NEW-YEAR-FITNESS', N'New year, new fitness goals',
 N'Personal trainers and yoga coaches near you.',
 N'Find a trainer', N'/search?sub=fitness', N'HomeMid', 3, -280, -220, 0, N'fitness', N'#DD2590', N'Fitness illustration');

MERGE dbo.Banners AS t
USING #Bn AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Title <> s.Title OR ISNULL(t.Subtitle, N'') <> s.Subtitle OR ISNULL(t.LinkUrl, N'') <> s.LinkUrl OR t.SortOrder <> s.SortOrder OR t.IsActive <> s.IsActive)
    THEN UPDATE SET Title = s.Title, Subtitle = s.Subtitle, CtaText = s.CtaText, LinkUrl = s.LinkUrl, Placement = s.Placement,
                    SortOrder = s.SortOrder, IsActive = s.IsActive, AltText = s.AltText, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Code, Title, Subtitle, CtaText, LinkUrl, ImageUrl, AltText, Placement, SortOrder, StartsOn, EndsOn, IsActive, CreatedBy, CreatedOn)
         VALUES (s.Code, s.Title, s.Subtitle, s.CtaText, s.LinkUrl, N'', s.AltText, s.Placement, s.SortOrder,
                 DATEADD(DAY, s.StartOffset, @Today), CASE WHEN s.EndOffset IS NULL THEN NULL ELSE DATEADD(DAY, s.EndOffset, @Today) END,
                 s.IsActive, N'seed', DATEADD(DAY, s.StartOffset - 3, SYSDATETIMEOFFSET()));

/* ---------- Artwork ---------- */
IF OBJECT_ID('tempdb..#BnArt') IS NOT NULL DROP TABLE #BnArt;
;WITH info AS (
    SELECT bn.Id, s.Code, s.ColorHex, s.AltText,
           SUBSTRING(icon.Svg, CHARINDEX(N'>', icon.Svg) + 1, LEN(icon.Svg) - CHARINDEX(N'>', icon.Svg) - 6) AS IconMarkup
    FROM #Bn s
    JOIN dbo.Banners bn ON bn.Code = s.Code
    JOIN dbo.SubCategories sc ON sc.Slug = s.IconSub
    CROSS APPLY (SELECT CAST(CAST(m.FileData AS varchar(max)) AS nvarchar(max)) AS Svg
                 FROM dbo.Media m WHERE m.EntityType = N'SubCategoryIcon' AND m.EntityId = sc.Id) icon
)
SELECT * INTO #BnArt FROM (
    SELECT N'Banner' AS EntityType, i.Id AS EntityId, N'desktop.svg' AS FileName, i.AltText,
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="560" viewBox="0 0 1600 560">',
                  N'<rect width="1600" height="560" fill="#0B1220"/>',
                  N'<circle cx="1290" cy="300" r="380" fill="', i.ColorHex, N'" fill-opacity="0.30"/>',
                  N'<circle cx="1540" cy="40" r="170" fill="#F4A62C" fill-opacity="0.22"/>',
                  N'<circle cx="980" cy="560" r="120" fill="#FFFFFF" fill-opacity="0.04"/>',
                  N'<g transform="translate(1140 150) scale(12.5)" fill="none" stroke="#FFFFFF" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g></svg>') AS Svg
    FROM info i
    UNION ALL
    SELECT N'BannerMobile', i.Id, N'mobile.svg', i.AltText,
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="800" height="800" viewBox="0 0 800 800">',
                  N'<rect width="800" height="800" fill="#0B1220"/>',
                  N'<circle cx="620" cy="190" r="260" fill="', i.ColorHex, N'" fill-opacity="0.32"/>',
                  N'<circle cx="760" cy="20" r="110" fill="#F4A62C" fill-opacity="0.22"/>',
                  N'<g transform="translate(500 70) scale(10)" fill="none" stroke="#FFFFFF" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g></svg>')
    FROM info i
) x;

MERGE dbo.Media AS t
USING (SELECT EntityType, EntityId, FileName, AltText,
              CAST(CAST(Svg COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max)) AS varbinary(max)) AS Bytes
       FROM #BnArt) AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = s.FileName
WHEN MATCHED AND (t.FileData <> s.Bytes OR ISNULL(t.AltText, N'') <> s.AltText)
    THEN UPDATE SET FileData = s.Bytes, ThumbnailData = s.Bytes, FileSize = DATALENGTH(s.Bytes), AltText = s.AltText, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, s.FileName, N'image/svg+xml', N'.svg', DATALENGTH(s.Bytes), s.Bytes, s.Bytes, s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

UPDATE bn SET
    ImageUrl        = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    DesktopImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    ThumbnailUrl    = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    MobileImageUrl  = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mm.MediaId))
FROM dbo.Banners bn
JOIN dbo.Media md ON md.EntityType = N'Banner'       AND md.EntityId = bn.Id AND md.FileName = N'desktop.svg'
JOIN dbo.Media mm ON mm.EntityType = N'BannerMobile' AND mm.EntityId = bn.Id AND mm.FileName = N'mobile.svg';

COMMIT TRANSACTION;
PRINT '06_Banners.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Bn, #BnArt;
GO
