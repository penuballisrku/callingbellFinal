/* =====================================================================================
   Calling Bell - 17_HomeContent.sql
   Home page content stored in dbo.MarketingContent (PageKey = 'Home'), served by
   GET /api/content/pages/Home.

   * QuickSearch: the shortcut chips under the hero search box. Title is the chip label and
     LinkUrl the search it runs; the web app appends the visitor's selected city.
   * Requires 03_Categories.sql (validates that every referenced sub-category exists).
   * Idempotent: MERGE on MarketingContent.Code.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Page nvarchar(40) = N'Home';

IF OBJECT_ID('tempdb..#Qs') IS NOT NULL DROP TABLE #Qs;
CREATE TABLE #Qs (Code nvarchar(60) PRIMARY KEY, SortOrder int, Title nvarchar(200), SubSlug nvarchar(140), Extra nvarchar(100), IsActive bit);
INSERT INTO #Qs VALUES
(N'HOME-QS-ELECTRICIANS', 1, N'Electricians near me', N'electrical',     N'&sort=distance',       1),
(N'HOME-QS-DOCTORS',      2, N'Available doctors',    N'doctors',        N'&availability=now',    1),
(N'HOME-QS-TUTORS',       3, N'Online tutors',        N'private-tutors', N'&video=true',          1),
(N'HOME-QS-LAWYERS',      4, N'Lawyers near me',      N'lawyers',        N'&sort=distance',       1),
(N'HOME-QS-AC-REPAIR',    5, N'AC repair nearby',     N'ac-repair',      N'&sort=distance',       1),
(N'HOME-QS-SALONS',       6, N'Salons open now',      N'beauty-salons',  N'&openNow=true',        1);

IF EXISTS (SELECT 1 FROM #Qs q WHERE NOT EXISTS (SELECT 1 FROM dbo.SubCategories sc WHERE sc.Slug = q.SubSlug AND sc.IsActive = 1))
    THROW 50018, 'A home quick search references a sub-category slug that does not exist or is inactive. Run 03_Categories.sql first.', 1;

MERGE dbo.MarketingContent AS t
USING (SELECT Code, SortOrder, Title, N'/search?sub=' + SubSlug + Extra AS LinkUrl, IsActive FROM #Qs) AS s
ON t.Code = s.Code
WHEN MATCHED AND (t.Title <> s.Title OR ISNULL(t.LinkUrl, N'') <> s.LinkUrl OR t.SortOrder <> s.SortOrder OR t.IsActive <> s.IsActive
                  OR t.PageKey <> @Page OR t.SectionKey <> N'QuickSearch')
    THEN UPDATE SET PageKey = @Page, SectionKey = N'QuickSearch', Title = s.Title, LinkUrl = s.LinkUrl, SortOrder = s.SortOrder,
                    IsActive = s.IsActive, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Code, PageKey, SectionKey, SortOrder, Title, LinkUrl, IsActive, CreatedBy, CreatedOn)
         VALUES (s.Code, @Page, N'QuickSearch', s.SortOrder, s.Title, s.LinkUrl, s.IsActive, N'seed', DATEADD(DAY, -30, SYSDATETIMEOFFSET()));

COMMIT TRANSACTION;
PRINT '17_HomeContent.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Qs;
GO
