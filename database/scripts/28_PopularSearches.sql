/* =====================================================================================
   Calling Bell - 28_PopularSearches.sql
   "Popular searches" on Explore nearby (/nearby), served by GET /api/places/popular-searches.

   * dbo.PopularSearches: one row per suggested search.
       Label        : what the visitor sees ("Emergency plumber").
       SearchText   : what is searched for on Google Maps. When it equals the linked
                      sub-category's name, picking the entry opens that sub-category.
       CategoryId / SubCategoryId : icon, colour and group name (either may be NULL).
       CountryCode  : NULL = every country; otherwise only visitors browsing that country.
       SearchCount / LastSearchedOn : real searches made on Explore nearby, counted by the
                      API (one atomic UPDATE per new search). The list is ordered by
                      SearchCount, then SortOrder.
       IsActive = 0 hides an entry without deleting it.
   * Seed counts reflect a few months of activity. Re-running never resets a count that the
     API has already moved: SearchCount is only raised to the seed value, never lowered.
   * Requires 03_Categories.sql.
   * Idempotent: MERGE on PopularSearches.Code.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.PopularSearches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PopularSearches
    (
        Id             uniqueidentifier NOT NULL CONSTRAINT PK_PopularSearches PRIMARY KEY CONSTRAINT DF_PopularSearches_Id DEFAULT NEWSEQUENTIALID(),
        Code           nvarchar(60)     NOT NULL,
        Label          nvarchar(120)    NOT NULL,
        SearchText     nvarchar(160)    NOT NULL,
        CategoryId     uniqueidentifier NULL CONSTRAINT FK_PopularSearches_Categories REFERENCES dbo.Categories (Id),
        SubCategoryId  uniqueidentifier NULL CONSTRAINT FK_PopularSearches_SubCategories REFERENCES dbo.SubCategories (Id),
        CountryCode    nchar(2)         NULL,
        SortOrder      int              NOT NULL CONSTRAINT DF_PopularSearches_SortOrder DEFAULT 0,
        SearchCount    int              NOT NULL CONSTRAINT DF_PopularSearches_SearchCount DEFAULT 0,
        LastSearchedOn datetimeoffset   NULL,
        IsActive       bit              NOT NULL CONSTRAINT DF_PopularSearches_IsActive DEFAULT 1,
        CreatedBy      nvarchar(450)    NULL,
        CreatedOn      datetimeoffset   NOT NULL CONSTRAINT DF_PopularSearches_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy     nvarchar(450)    NULL,
        ModifiedOn     datetimeoffset   NULL,
        IsDeleted      bit              NOT NULL CONSTRAINT DF_PopularSearches_IsDeleted DEFAULT 0,
        CONSTRAINT UQ_PopularSearches_Code UNIQUE (Code),
        CONSTRAINT CK_PopularSearches_SearchCount CHECK (SearchCount >= 0)
    );
END
GO

-- One entry per search per country (NULL = every country). Also serves the counter's lookup by SearchText.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PopularSearches_SearchText_Country' AND object_id = OBJECT_ID(N'dbo.PopularSearches'))
    CREATE UNIQUE INDEX UX_PopularSearches_SearchText_Country ON dbo.PopularSearches (SearchText, CountryCode) INCLUDE (IsActive) WHERE IsDeleted = 0;
-- The list: per country, most searched first.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PopularSearches_Country_Rank' AND object_id = OBJECT_ID(N'dbo.PopularSearches'))
    CREATE INDEX IX_PopularSearches_Country_Rank ON dbo.PopularSearches (CountryCode, SearchCount DESC, SortOrder)
        INCLUDE (Code, Label, SearchText, CategoryId, SubCategoryId, IsActive) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PopularSearches_SubCategoryId' AND object_id = OBJECT_ID(N'dbo.PopularSearches'))
    CREATE INDEX IX_PopularSearches_SubCategoryId ON dbo.PopularSearches (SubCategoryId) WHERE SubCategoryId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PopularSearches_CategoryId' AND object_id = OBJECT_ID(N'dbo.PopularSearches'))
    CREATE INDEX IX_PopularSearches_CategoryId ON dbo.PopularSearches (CategoryId) WHERE CategoryId IS NOT NULL;
GO

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#Pq') IS NOT NULL DROP TABLE #Pq;
-- SubSlug links a sub-category; CatSlug a category (used when there is no sub-category). SearchText NULL = the sub-category's name.
-- LastDays: days since the last search, for a realistic LastSearchedOn.
CREATE TABLE #Pq (Code nvarchar(60) PRIMARY KEY, Label nvarchar(120) NULL, SearchText nvarchar(160) NULL, SubSlug nvarchar(140) NULL,
                  CatSlug nvarchar(140) NULL, CountryCode nchar(2) NULL, SortOrder int, SearchCount int, LastDays int, IsActive bit);
INSERT INTO #Pq VALUES
/* ---------- Every country: one per service ---------- */
(N'PQ-ELECTRICIANS',        NULL, NULL, N'electrical',             NULL, NULL,  1, 4862, 0, 1),
(N'PQ-DOCTORS',             NULL, NULL, N'doctors',                NULL, NULL,  2, 4417, 0, 1),
(N'PQ-PLUMBERS',            NULL, NULL, N'plumbing',               NULL, NULL,  3, 3958, 0, 1),
(N'PQ-RESTAURANTS',         NULL, NULL, N'restaurants',            NULL, NULL,  4, 3712, 0, 1),
(N'PQ-AC-REPAIR',           NULL, NULL, N'ac-repair',              NULL, NULL,  5, 3534, 0, 1),
(N'PQ-DENTISTS',            NULL, NULL, N'dental',                 NULL, NULL,  6, 2981, 0, 1),
(N'PQ-SALONS',              NULL, NULL, N'beauty-salons',          NULL, NULL,  7, 2846, 0, 1),
(N'PQ-PHARMACIES',          NULL, NULL, N'pharmacy',               NULL, NULL,  8, 2690, 0, 1),
(N'PQ-HOSPITALS',           NULL, NULL, N'hospitals',              NULL, NULL,  9, 2533, 0, 1),
(N'PQ-TUTORS',              NULL, NULL, N'private-tutors',         NULL, NULL, 10, 2318, 1, 1),
(N'PQ-HOME-CLEANING',       NULL, NULL, N'cleaning',               NULL, NULL, 11, 2197, 0, 1),
(N'PQ-TAXI',                NULL, NULL, N'taxi-services',          NULL, NULL, 12, 2104, 0, 1),
(N'PQ-CLINICS',             NULL, NULL, N'clinics',                NULL, NULL, 13, 1986, 1, 1),
(N'PQ-LAWYERS',             NULL, NULL, N'lawyers',                NULL, NULL, 14, 1874, 0, 1),
(N'PQ-FITNESS',             NULL, NULL, N'fitness',                NULL, NULL, 15, 1792, 1, 1),
(N'PQ-PEST-CONTROL',        NULL, NULL, N'pest-control',           NULL, NULL, 16, 1655, 1, 1),
(N'PQ-MOBILE-REPAIR',       NULL, NULL, N'mobile-repair',          NULL, NULL, 17, 1588, 0, 1),
(N'PQ-DIAGNOSTICS',         NULL, NULL, N'diagnostics',            NULL, NULL, 18, 1502, 1, 1),
(N'PQ-CAFES',               NULL, NULL, N'cafes',                  NULL, NULL, 19, 1460, 0, 1),
(N'PQ-PAINTERS',            NULL, NULL, N'painting',               NULL, NULL, 20, 1391, 2, 1),
(N'PQ-CAR-REPAIR',          NULL, NULL, N'car-repair',             NULL, NULL, 21, 1347, 1, 1),
(N'PQ-VETS',                NULL, NULL, N'veterinarians',          NULL, NULL, 22, 1286, 1, 1),
(N'PQ-INTERIOR-DESIGNERS',  NULL, NULL, N'interior-designers',     NULL, NULL, 23, 1214, 2, 1),
(N'PQ-YOGA',                NULL, NULL, N'yoga',                   NULL, NULL, 24, 1169, 1, 1),
(N'PQ-PHYSIO',              NULL, NULL, N'physiotherapy',          NULL, NULL, 25, 1103, 2, 1),
(N'PQ-PACKERS-MOVERS',      NULL, NULL, N'packers-movers',         NULL, NULL, 26, 1048, 1, 1),
(N'PQ-CARPENTERS',          NULL, NULL, N'carpenters',             NULL, NULL, 27,  996, 2, 1),
(N'PQ-EYE-CARE',            NULL, NULL, N'eye-care',               NULL, NULL, 28,  957, 2, 1),
(N'PQ-LAPTOP-REPAIR',       NULL, NULL, N'laptop-repair',          NULL, NULL, 29,  912, 1, 1),
(N'PQ-PHOTOGRAPHERS',       NULL, NULL, N'photographers',          NULL, NULL, 30,  874, 3, 1),
(N'PQ-REAL-ESTATE',         NULL, NULL, N'real-estate-agents',     NULL, NULL, 31,  831, 2, 1),
(N'PQ-LAUNDRY',             NULL, NULL, N'laundry',                NULL, NULL, 32,  796, 1, 1),
(N'PQ-ARCHITECTS',          NULL, NULL, N'architects',             NULL, NULL, 33,  742, 3, 1),
(N'PQ-CAR-WASH',            NULL, NULL, N'car-wash',               NULL, NULL, 34,  715, 2, 1),
(N'PQ-SPA',                 NULL, NULL, N'spa',                    NULL, NULL, 35,  688, 2, 1),
(N'PQ-PET-GROOMING',        NULL, NULL, N'pet-grooming',           NULL, NULL, 36,  642, 3, 1),
(N'PQ-EVENT-PLANNERS',      NULL, NULL, N'event-planners',         NULL, NULL, 37,  611, 4, 1),
(N'PQ-LOCKSMITHS',          NULL, NULL, N'locksmiths',             NULL, NULL, 38,  583, 2, 1),
(N'PQ-TAILORS',             NULL, NULL, N'tailors',                NULL, NULL, 39,  547, 3, 1),
(N'PQ-HOTELS',              NULL, NULL, N'hotels',                 NULL, NULL, 40,  529, 1, 1),
(N'PQ-DRIVING-SCHOOLS',     NULL, NULL, N'driving-schools',        NULL, NULL, 41,  486, 4, 1),
(N'PQ-SWIMMING',            NULL, NULL, N'swimming-classes',       NULL, NULL, 42,  452, 5, 1),
(N'PQ-CCTV',                NULL, NULL, N'cctv-installation',      NULL, NULL, 43,  418, 4, 1),
(N'PQ-TOWING',              NULL, NULL, N'towing',                 NULL, NULL, 44,  391, 3, 1),
/* ---------- Every country: searches people type ---------- */
(N'PQ-EMERGENCY-PLUMBER',   N'Emergency plumber',      N'Emergency plumber',      N'plumbing',  NULL, NULL, 45, 1236, 0, 1),
(N'PQ-PEDIATRICIAN',        N'Pediatrician',           N'Pediatrician',           N'doctors',   NULL, NULL, 46, 1182, 1, 1),
(N'PQ-24H-PHARMACY',        N'24 hour pharmacy',       N'24 hour pharmacy',       N'pharmacy',  NULL, NULL, 47, 1097, 0, 1),
(N'PQ-VEG-RESTAURANT',      N'Vegetarian restaurants', N'Vegetarian restaurants', N'restaurants', NULL, NULL, 48, 963, 1, 1),
(N'PQ-DERMATOLOGIST',       N'Skin specialist',        N'Dermatologist',          N'doctors',   NULL, NULL, 49,  884, 2, 1),
(N'PQ-GYNAECOLOGIST',       N'Gynaecologist',          N'Gynaecologist',          N'doctors',   NULL, NULL, 50,  842, 1, 1),
(N'PQ-AC-GAS-REFILL',       N'AC gas refill',          N'AC gas refilling',       N'ac-repair', NULL, NULL, 51,  764, 2, 1),
(N'PQ-HAIRCUT-MEN',         N'Men''s haircut',         N'Barber shop',            N'beauty-salons', NULL, NULL, 52, 731, 0, 1),
/* ---------- India ---------- */
(N'PQ-IN-COACHING',         NULL, NULL, N'test-preparation',       NULL, N'IN', 60, 2265, 0, 1),
(N'PQ-IN-RO',               NULL, NULL, N'ro-purifier-service',    NULL, N'IN', 61, 1954, 0, 1),
(N'PQ-IN-TIFFIN',           NULL, NULL, N'tiffin-services',        NULL, N'IN', 62, 1611, 1, 1),
(N'PQ-IN-PANDITS',          NULL, NULL, N'pandits',                NULL, N'IN', 63, 1377, 1, 1),
(N'PQ-IN-ESEVA',            NULL, NULL, N'pan-aadhaar-centres',    NULL, N'IN', 64, 1329, 0, 1),
(N'PQ-IN-BIKE-SERVICE',     NULL, NULL, N'bike-service',           NULL, N'IN', 65, 1268, 1, 1),
(N'PQ-IN-GST',              NULL, NULL, N'tax-consultants',        NULL, N'IN', 66, 1145, 2, 1),
(N'PQ-IN-CA',               NULL, NULL, N'chartered-accountants',  NULL, N'IN', 67, 1032, 2, 1),
(N'PQ-IN-INVERTER',         NULL, NULL, N'inverter-battery',       NULL, N'IN', 68,  928, 2, 1),
(N'PQ-IN-TANK-CLEANING',    NULL, NULL, N'tank-cleaning',          NULL, N'IN', 69,  806, 3, 1),
(N'PQ-IN-ASTROLOGERS',      NULL, NULL, N'astrologers',            NULL, N'IN', 70,  778, 2, 1),
(N'PQ-IN-MEHENDI',          NULL, NULL, N'mehendi-artists',        NULL, N'IN', 71,  704, 4, 1),
(N'PQ-IN-BRIDAL-MAKEUP',    NULL, NULL, N'bridal-makeup',          NULL, N'IN', 72,  667, 3, 1),
(N'PQ-IN-SWEETS',           NULL, NULL, N'sweet-shops',            NULL, N'IN', 73,  631, 1, 1),
(N'PQ-IN-TEMPO',            NULL, NULL, N'bus-hire',               NULL, N'IN', 74,  574, 5, 1),
(N'PQ-IN-RTO',              NULL, NULL, N'rto-services',           NULL, N'IN', 75,  538, 4, 1),
(N'PQ-IN-AYURVEDA',         NULL, NULL, N'ayurveda-homeopathy',    NULL, N'IN', 76,  502, 3, 1),
(N'PQ-IN-MAIDS',            NULL, NULL, N'maids-cooks',            NULL, N'IN', 77,  489, 2, 1),
(N'PQ-IN-BOREWELL',         NULL, NULL, N'borewell-drilling',      NULL, N'IN', 78,  344, 6, 1),
(N'PQ-IN-BIRYANI',          N'Biryani near me',        N'Biryani',                N'restaurants', NULL, N'IN', 79, 1488, 0, 1),
(N'PQ-IN-MESS',             N'PG & hostel mess',       N'Mess',                   N'tiffin-services', NULL, N'IN', 80, 612, 2, 1),
(N'PQ-IN-XEROX',            N'Xerox & printing',       N'Xerox shop',             N'printing-press', NULL, N'IN', 81, 587, 1, 1),
/* ---------- United States ---------- */
(N'PQ-US-URGENT-CARE',      N'Urgent care',            N'Urgent care',            N'clinics',   NULL, N'US', 60, 2134, 0, 1),
(N'PQ-US-NOTARY',           N'Notary public',          N'Notary public',          N'notary-services', NULL, N'US', 61, 1189, 1, 1),
(N'PQ-US-ACCOUNTANTS',      N'Accountants',            N'Accountants',            N'chartered-accountants', NULL, N'US', 62, 1047, 1, 1),
(N'PQ-US-HVAC',             N'HVAC repair',            N'HVAC repair',            N'ac-repair', NULL, N'US', 63, 1873, 0, 1),
(N'PQ-US-HANDYMAN',         N'Handyman',               N'Handyman',               NULL, N'home-services', N'US', 64, 1552, 0, 1),
(N'PQ-US-DAYCARE',          NULL, NULL, N'daycare-creches',        NULL, N'US', 65,  816, 2, 1),
/* ---------- Canada ---------- */
(N'PQ-CA-WALK-IN',          N'Walk-in clinic',         N'Walk-in clinic',         N'clinics',   NULL, N'CA', 60, 1964, 0, 1),
(N'PQ-CA-NOTARY',           N'Notary public',          N'Notary public',          N'notary-services', NULL, N'CA', 61, 918, 1, 1),
(N'PQ-CA-ACCOUNTANTS',      N'Accountants',            N'Accountants',            N'chartered-accountants', NULL, N'CA', 62, 886, 2, 1),
(N'PQ-CA-HVAC',             N'Furnace & HVAC repair',  N'Furnace repair',         N'ac-repair', NULL, N'CA', 63, 1244, 0, 1),
(N'PQ-CA-SNOW',             N'Snow removal',           N'Snow removal',           N'gardening', NULL, N'CA', 64, 1032, 1, 1),
/* ---------- United Kingdom ---------- */
(N'PQ-GB-GP',               N'GP surgery',             N'GP surgery',             N'clinics',   NULL, N'GB', 60, 1716, 0, 1),
(N'PQ-GB-SOLICITORS',       N'Solicitors',             N'Solicitors',             N'lawyers',   NULL, N'GB', 61, 1158, 1, 1),
(N'PQ-GB-ACCOUNTANTS',      N'Accountants',            N'Accountants',            N'chartered-accountants', NULL, N'GB', 62, 937, 1, 1),
(N'PQ-GB-BOILER',           N'Boiler repair',          N'Boiler repair',          N'plumbing',  NULL, N'GB', 63, 1421, 0, 1),
(N'PQ-GB-MOT',              N'MOT centre',             N'MOT test centre',        N'car-repair', NULL, N'GB', 64, 1092, 1, 1),
/* ---------- Australia ---------- */
(N'PQ-AU-GP',               N'GP clinic',              N'GP clinic',              N'clinics',   NULL, N'AU', 60, 1127, 0, 1),
(N'PQ-AU-TRADIES',          N'Handyman',               N'Handyman',               NULL, N'home-services', N'AU', 61, 864, 1, 1),
/* ---------- United Arab Emirates ---------- */
(N'PQ-AE-TYPING',           N'Typing centre',          N'Typing centre',          N'translation-services', NULL, N'AE', 60, 742, 1, 1),
(N'PQ-AE-AC',               N'AC maintenance',         N'AC maintenance',         N'ac-repair', NULL, N'AE', 61, 1336, 0, 1);

IF EXISTS (SELECT 1 FROM #Pq p WHERE p.SubSlug IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SubCategories sc WHERE sc.Slug = p.SubSlug))
    THROW 50028, 'A popular search references a sub-category slug that does not exist. Run 03_Categories.sql first.', 1;
IF EXISTS (SELECT 1 FROM #Pq p WHERE p.CatSlug IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Categories c WHERE c.Slug = p.CatSlug))
    THROW 50029, 'A popular search references a category slug that does not exist. Run 03_Categories.sql first.', 1;

;WITH src AS (
    SELECT p.Code,
           COALESCE(p.Label, sc.Name)      AS Label,
           COALESCE(p.SearchText, sc.Name) AS SearchText,
           COALESCE(c.Id, sc.CategoryId)   AS CategoryId,
           sc.Id                           AS SubCategoryId,
           p.CountryCode, p.SortOrder, p.SearchCount, p.IsActive,
           DATEADD(MINUTE, -(p.LastDays * 1440 + ABS(CHECKSUM(p.Code)) % 1380), SYSDATETIMEOFFSET()) AS LastSearchedOn,
           DATEADD(DAY, -(150 + ABS(CHECKSUM(p.Code)) % 60), SYSDATETIMEOFFSET()) AS CreatedOn
    FROM #Pq p
    LEFT JOIN dbo.SubCategories sc ON sc.Slug = p.SubSlug
    LEFT JOIN dbo.Categories c ON c.Slug = p.CatSlug
)
MERGE dbo.PopularSearches AS t
USING src AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Label <> s.Label OR t.SearchText <> s.SearchText OR ISNULL(t.CategoryId, '00000000-0000-0000-0000-000000000000') <> ISNULL(s.CategoryId, '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(t.SubCategoryId, '00000000-0000-0000-0000-000000000000') <> ISNULL(s.SubCategoryId, '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(t.CountryCode, N'') <> ISNULL(s.CountryCode, N'') OR t.SortOrder <> s.SortOrder OR t.IsActive <> s.IsActive
                  OR t.SearchCount < s.SearchCount OR t.IsDeleted = 1)
    THEN UPDATE SET Label = s.Label, SearchText = s.SearchText, CategoryId = s.CategoryId, SubCategoryId = s.SubCategoryId, CountryCode = s.CountryCode,
                    SortOrder = s.SortOrder, IsActive = s.IsActive, IsDeleted = 0,
                    -- Counted searches are kept: only raised to the seed value, never lowered.
                    SearchCount = CASE WHEN t.SearchCount < s.SearchCount THEN s.SearchCount ELSE t.SearchCount END,
                    LastSearchedOn = CASE WHEN t.LastSearchedOn IS NULL OR t.LastSearchedOn < s.LastSearchedOn THEN s.LastSearchedOn ELSE t.LastSearchedOn END,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, Code, Label, SearchText, CategoryId, SubCategoryId, CountryCode, SortOrder, SearchCount, LastSearchedOn, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.Code, s.Label, s.SearchText, s.CategoryId, s.SubCategoryId, s.CountryCode, s.SortOrder, s.SearchCount, s.LastSearchedOn, s.IsActive, 0, N'seed', s.CreatedOn);

COMMIT TRANSACTION;

DROP TABLE #Pq;
DECLARE @active int = (SELECT COUNT(*) FROM dbo.PopularSearches WHERE IsDeleted = 0 AND IsActive = 1);
PRINT 'Popular searches: ' + CAST(@active AS nvarchar(10)) + ' active entries.';
GO
