/* =====================================================================================
   Calling Bell - 16_PopularServices.sql
   Curated home-page "Popular services" with card artwork.

   * Each row points at a sub-category and a ServiceName that exists in BusinessServices for
     that sub-category; the API computes starting price, providers and bookings live.
   * SortOrder controls display order; IsActive = 0 hides an entry without deleting it.
   * Artwork is SVG stored in dbo.Media (EntityType 'PopularService'), drawn from the
     sub-category icon in the parent category's colour.
   * Requires 03_Categories.sql and 05_BusinessServices.sql.
   * Idempotent: MERGE on PopularServices.Code and (EntityType, EntityId, FileName).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#Ps') IS NOT NULL DROP TABLE #Ps;
CREATE TABLE #Ps (Code nvarchar(60) PRIMARY KEY, SubSlug nvarchar(140), Title nvarchar(160), ServiceName nvarchar(160),
                  Tagline nvarchar(200), BadgeText nvarchar(30) NULL, SortOrder int, IsActive bit);
INSERT INTO #Ps VALUES
(N'PS-ELECTRICIAN-VISIT',   N'electrical',             N'Electrician Visit',            N'Electrician Visit & Inspection',      N'Wiring faults, MCB trips and switchboard repairs',          N'Most booked', 1, 1),
(N'PS-DOCTOR-VIDEO',        N'doctors',                N'Doctor Video Consultation',    N'Video Consultation',                  N'Consult a verified doctor with an e-prescription',          N'Most booked', 2, 1),
(N'PS-AC-SERVICE',          N'ac-repair',              N'AC Service & Jet Cleaning',    N'AC General Service',                  N'Filter, coil and drain cleaning for split and window ACs',   N'Trending',    3, 1),
(N'PS-HOME-TUITION',        N'private-tutors',         N'Home Tuition (Classes 6-10)',  N'Home Tuition - Classes 6 to 10',      N'CBSE, ICSE and State Board tutors at home',                  NULL,           4, 1),
(N'PS-DENTAL-CLEANING',     N'dental',                 N'Dental Check-up & Cleaning',   N'Dental Check-up & Cleaning',          N'Scaling, polishing and an oral health check',                NULL,           5, 1),
(N'PS-ITR-FILING',          N'tax-consultants',        N'Income Tax Return Filing',     N'ITR Filing (Salaried)',               N'Expert-assisted ITR with Form 16 and capital gains review',  N'Trending',    6, 1),
(N'PS-PET-VACCINATION',     N'veterinarians',          N'Pet Vaccination',              N'Annual Vaccination (DHPPi + Rabies)', N'Core vaccines for dogs and cats with a digital record',      N'New',         7, 1),
(N'PS-DEEP-CLEANING',       N'cleaning',               N'Home Deep Cleaning',           N'Full Home Deep Cleaning (2 BHK)',     N'Kitchen, bathrooms, floors and windows for a 2 BHK',         NULL,           8, 1),
(N'PS-WASHING-MACHINE',     N'washing-machine-repair', N'Washing Machine Repair',       N'Washing Machine Inspection',          N'Same-day repairs for front-load and top-load machines',      N'New',         9, 1),
(N'PS-HAIRCUT',             N'beauty-salons',          N'Haircut & Styling',            N'Haircut & Styling',                   N'Precision cuts and blow-dry by senior stylists',             NULL,          10, 1),
(N'PS-BLOOD-TEST-HOME',     N'diagnostics',            N'Blood Test at Home',           N'Home Blood Sample Collection',        N'NABL-accredited labs with reports on WhatsApp',             NULL,          11, 1),
(N'PS-LEGAL-VIDEO',         N'lawyers',                N'Talk to a Lawyer',             N'Video Legal Consultation',            N'Property, family and consumer matters over video',           NULL,          12, 1),
(N'PS-AIRPORT-TRANSFER',    N'taxi-services',          N'Airport Transfer',             N'Airport Transfer (Sedan)',            N'Fixed fares with flight tracking and free waiting',          NULL,          13, 1),
(N'PS-ROOFTOP-SOLAR',       N'solar-installation',     N'Rooftop Solar (3 kW)',         N'3 kW Rooftop Solar System',           N'On-grid solar with net metering and subsidy support',        N'New',        14, 1),
(N'PS-HOME-PHYSIO',         N'physiotherapy',          N'Physiotherapy at Home',        N'Home Physiotherapy',                  N'Back, knee and post-surgery rehab at your home',             NULL,          15, 1),
(N'PS-COCKROACH-CONTROL',   N'pest-control',           N'Cockroach Control',            N'Cockroach Control',                   N'Odourless gel treatment with a 90-day warranty',             NULL,          16, 1),
(N'PS-BUSINESS-WEBSITE',    N'web-developers',         N'Business Website',             N'Business Website (5 pages)',          N'Mobile-first website with WhatsApp chat and SEO basics',     N'New',        17, 1),
(N'PS-DOG-GROOMING',        N'pet-grooming',           N'Dog Grooming',                 N'Full Grooming (Dog)',                 N'Bath, haircut, nail clipping and ear cleaning',              NULL,          18, 1),
(N'PS-MODULAR-KITCHEN',     N'interior-designers',     N'Modular Kitchen',              N'Modular Kitchen',                     N'Design, manufacture and installation in 45 days',            NULL,          19, 1),
(N'PS-TERRACE-WATERPROOF',  N'waterproofing',          N'Terrace Waterproofing',        N'Terrace Waterproofing',               N'Leak-proof terraces before the monsoon, with warranty',      NULL,          20, 1),
(N'PS-PERSONAL-TRAINING',   N'fitness',                N'Personal Training',            N'Personal Training Session',           N'Certified trainers at home, in the gym or online',           NULL,          21, 1),
(N'PS-PHONE-SCREEN',        N'mobile-repair',          N'iPhone Screen Replacement',    N'iPhone Screen Replacement',           N'OLED screen replacement in about an hour',                   NULL,          22, 1),
(N'PS-GRIHA-PRAVESH',       N'pandits',                N'Griha Pravesh Pooja',          N'Griha Pravesh Pooja',                 N'Experienced pandits with complete samagri',                  NULL,          23, 1),
(N'PS-HOME-SHIFTING',       N'packers-movers',         N'Home Shifting (1 BHK)',        N'1 BHK Local Shifting',                N'Packing, loading, transport and unpacking in the city',      NULL,          24, 1),
(N'PS-COMPANY-REGISTRATION',N'chartered-accountants',  N'Company Registration',         N'Private Limited Company Registration', N'Incorporation with PAN, TAN and GST by CAs',                NULL,          25, 1),
(N'PS-WEDDING-PHOTOGRAPHY', N'photographers',          N'Candid Wedding Photography',   N'Candid Wedding Photography',          N'Two photographers, edited photos and a premium album',       NULL,          26, 1),
(N'PS-RO-SERVICE',          N'ro-purifier-service',    N'RO Purifier Service',          N'RO Service & Filter Change',          N'Filter replacement, sanitisation and TDS check',             NULL,          27, 1),
(N'PS-LAUNDRY',             N'laundry',                N'Laundry Pickup',               N'Wash & Iron',                         N'Wash, steam iron and fold with free doorstep pickup',        NULL,          28, 1),
(N'PS-CAR-WASH',            N'car-wash',               N'Foam Car Wash',                N'Foam Car Wash',                       N'Exterior foam wash, tyre polish and vacuum',                 NULL,          29, 1),
(N'PS-BRIDAL-MAKEUP',       N'bridal-makeup',          N'Bridal Makeup',                N'HD Bridal Makeup',                    N'HD makeup, hairstyling and draping for the big day',         NULL,          30, 0);

IF EXISTS (SELECT 1 FROM #Ps p WHERE NOT EXISTS (SELECT 1 FROM dbo.SubCategories sc WHERE sc.Slug = p.SubSlug))
    THROW 50016, 'A popular service references a sub-category slug that does not exist. Run 03_Categories.sql first.', 1;
IF EXISTS (SELECT 1 FROM #Ps p JOIN dbo.SubCategories sc ON sc.Slug = p.SubSlug
           WHERE NOT EXISTS (SELECT 1 FROM dbo.BusinessServices s JOIN dbo.Businesses b ON b.Id = s.BusinessId
                             WHERE b.SubCategoryId = sc.Id AND s.Name = p.ServiceName AND s.IsDeleted = 0))
    THROW 50017, 'A popular service name is not offered by any business in its sub-category. Run 05_BusinessServices.sql first.', 1;

MERGE dbo.PopularServices AS t
USING (SELECT p.*, sc.Id AS SubCategoryId FROM #Ps p JOIN dbo.SubCategories sc ON sc.Slug = p.SubSlug) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.SubCategoryId <> s.SubCategoryId OR t.Title <> s.Title OR t.ServiceName <> s.ServiceName
                  OR ISNULL(t.Tagline, N'') <> s.Tagline OR ISNULL(t.BadgeText, N'') <> ISNULL(s.BadgeText, N'')
                  OR t.SortOrder <> s.SortOrder OR t.IsActive <> s.IsActive)
    THEN UPDATE SET SubCategoryId = s.SubCategoryId, Title = s.Title, ServiceName = s.ServiceName, Tagline = s.Tagline, BadgeText = s.BadgeText,
                    AltText = s.Title, SortOrder = s.SortOrder, IsActive = s.IsActive, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, SubCategoryId, Code, Title, ServiceName, Tagline, BadgeText, AltText, SortOrder, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.SubCategoryId, s.Code, s.Title, s.ServiceName, s.Tagline, s.BadgeText, s.Title, s.SortOrder, s.IsActive, 0, N'seed', DATEADD(DAY, -120, SYSDATETIMEOFFSET()));

/* ---------- Card artwork (800x500) ---------- */
IF OBJECT_ID('tempdb..#PsArt') IS NOT NULL DROP TABLE #PsArt;
;WITH info AS (
    SELECT ps.Id, ps.Code, ps.Title, ps.SortOrder, cat.ColorHex,
           SUBSTRING(icon.Svg, CHARINDEX(N'>', icon.Svg) + 1, LEN(icon.Svg) - CHARINDEX(N'>', icon.Svg) - 6) AS IconMarkup
    FROM dbo.PopularServices ps
    JOIN #Ps src ON src.Code = ps.Code
    JOIN dbo.SubCategories sc ON sc.Id = ps.SubCategoryId
    JOIN dbo.Categories cat ON cat.Id = sc.CategoryId
    CROSS APPLY (SELECT CAST(CAST(m.FileData AS varchar(max)) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS nvarchar(max)) AS Svg
                 FROM dbo.Media m WHERE m.EntityType = N'SubCategoryIcon' AND m.EntityId = sc.Id) icon
)
SELECT N'PopularService' AS EntityType, i.Id AS EntityId, LOWER(i.Code) + N'.svg' AS FileName, i.Title + N' illustration' AS AltText,
       CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="800" height="500" viewBox="0 0 800 500">',
              N'<rect width="800" height="500" fill="#FFFFFF"/>',
              N'<rect width="800" height="500" fill="', i.ColorHex, N'" fill-opacity="0.09"/>',
              -- soft backdrop shapes, shifted per card so neighbours never look identical
              N'<circle cx="', 560 + (i.SortOrder * 47) % 140, N'" cy="', 120 + (i.SortOrder * 29) % 90, N'" r="230" fill="', i.ColorHex, N'" fill-opacity="0.10"/>',
              N'<circle cx="', 60 + (i.SortOrder * 31) % 120, N'" cy="470" r="150" fill="', i.ColorHex, N'" fill-opacity="0.07"/>',
              N'<g fill="', i.ColorHex, N'" fill-opacity="0.18">',
              N'<circle cx="64" cy="64" r="4"/><circle cx="94" cy="64" r="4"/><circle cx="124" cy="64" r="4"/>',
              N'<circle cx="64" cy="94" r="4"/><circle cx="94" cy="94" r="4"/><circle cx="124" cy="94" r="4"/></g>',
              -- icon tile
              N'<rect x="280" y="130" width="240" height="240" rx="56" fill="#FFFFFF" stroke="', i.ColorHex, N'" stroke-opacity="0.22" stroke-width="2"/>',
              N'<g transform="translate(328 178) scale(6)" fill="none" stroke="', i.ColorHex, N'" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g>',
              N'</svg>') AS Svg
INTO #PsArt
FROM info i;

MERGE dbo.Media AS t
USING (SELECT EntityType, EntityId, FileName, AltText,
              CAST(CAST(Svg COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max)) AS varbinary(max)) AS Bytes
       FROM #PsArt) AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = s.FileName
WHEN MATCHED AND (t.FileData <> s.Bytes OR ISNULL(t.AltText, N'') <> s.AltText)
    THEN UPDATE SET FileData = s.Bytes, ThumbnailData = s.Bytes, FileSize = DATALENGTH(s.Bytes), AltText = s.AltText,
                    IsActive = 1, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, s.FileName, N'image/svg+xml', N'.svg', DATALENGTH(s.Bytes), s.Bytes, s.Bytes, s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

UPDATE ps SET
    ImageUrl     = N'/api/media/' + LOWER(CONVERT(nvarchar(36), m.MediaId)),
    ThumbnailUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), m.MediaId)) + N'/thumbnail'
FROM dbo.PopularServices ps
JOIN #Ps src ON src.Code = ps.Code
JOIN dbo.Media m ON m.EntityType = N'PopularService' AND m.EntityId = ps.Id AND m.FileName = LOWER(ps.Code) + N'.svg';

COMMIT TRANSACTION;
PRINT '16_PopularServices.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Ps, #PsArt;
GO
