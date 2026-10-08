/* =====================================================================================
   Calling Bell - 27_Seo.sql
   Database support for SEO:
   * dbo.BusinessSlugHistory: a business's former slugs. /business/{old-slug} answers with a
     301 redirect to the current address, so links and search results keep working.
   * TR_Businesses_SlugHistory: records the old slug whenever Businesses.Slug changes, by any
     code path (owner, admin, script). A slug that is in use again is taken off the history.
   * IX_Businesses_Status_Area_SubCategory: area and area + category landing pages and the
     sitemap's location grouping (city-level lookups already have IX_Businesses_City_SubCategory_Status).
   * Idempotent: safe to run repeatedly.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.BusinessSlugHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BusinessSlugHistory
    (
        Id         uniqueidentifier NOT NULL CONSTRAINT PK_BusinessSlugHistory PRIMARY KEY CONSTRAINT DF_BusinessSlugHistory_Id DEFAULT NEWSEQUENTIALID(),
        BusinessId uniqueidentifier NOT NULL CONSTRAINT FK_BusinessSlugHistory_Businesses REFERENCES dbo.Businesses (Id) ON DELETE CASCADE,
        Slug       nvarchar(180)    NOT NULL,
        CreatedOn  datetimeoffset   NOT NULL CONSTRAINT DF_BusinessSlugHistory_CreatedOn DEFAULT SYSDATETIMEOFFSET()
    );
    CREATE UNIQUE INDEX IX_BusinessSlugHistory_Slug ON dbo.BusinessSlugHistory (Slug);
    CREATE INDEX IX_BusinessSlugHistory_BusinessId ON dbo.BusinessSlugHistory (BusinessId);
END
GO

CREATE OR ALTER TRIGGER dbo.TR_Businesses_SlugHistory ON dbo.Businesses
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Slug) RETURN;

    -- A slug now in use again (by its own business or another) is no longer a redirect.
    DELETE h
    FROM dbo.BusinessSlugHistory h
    JOIN inserted i ON i.Slug = h.Slug;

    -- The previous slug of every renamed business now redirects to it.
    MERGE dbo.BusinessSlugHistory AS t
    USING (SELECT d.Id AS BusinessId, d.Slug
           FROM deleted d JOIN inserted i ON i.Id = d.Id
           WHERE d.Slug <> i.Slug) AS s
    ON t.Slug = s.Slug
    WHEN MATCHED THEN UPDATE SET BusinessId = s.BusinessId, CreatedOn = SYSDATETIMEOFFSET()
    WHEN NOT MATCHED THEN INSERT (BusinessId, Slug) VALUES (s.BusinessId, s.Slug);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Businesses_Status_Area_SubCategory' AND object_id = OBJECT_ID('dbo.Businesses'))
    CREATE INDEX IX_Businesses_Status_Area_SubCategory ON dbo.Businesses (Status, AreaId, SubCategoryId)
        INCLUDE (CityId, CategoryId, AverageRating, ReviewCount, IsFeatured)
        WHERE IsDeleted = 0;
GO

PRINT '27_Seo.sql completed';
GO
