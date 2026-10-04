/* =====================================================================================
   Calling Bell - 19_CityAltNames.sql
   Other names people type for the listed cities (dbo.Cities.AltNames), '|' separated.

   A search such as "salons in Bangalore" names its place after "in"; the search parser
   (GET /api/search/parse) matches that place to a city by name, slug or one of these
   names, and the location dropdown then selects the city.

   * Requires 00_Schema.sql (AltNames column) and 02_Locations.sql (cities).
   * Idempotent: plain UPDATE per slug, only when the value differs.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @Alt TABLE (Slug nvarchar(140) NOT NULL PRIMARY KEY, AltNames nvarchar(400) NOT NULL);
INSERT INTO @Alt (Slug, AltNames) VALUES
(N'bengaluru', N'Bangalore|Bengaluru Urban|Bangaluru'),
(N'mumbai',    N'Bombay|Navi Mumbai|Greater Mumbai'),
(N'chennai',   N'Madras'),
(N'kolkata',   N'Calcutta'),
(N'kochi',     N'Cochin|Ernakulam'),
(N'delhi',     N'Delhi|Delhi NCR|NCR'),
(N'pune',      N'Poona'),
(N'ahmedabad', N'Amdavad|Ahmadabad'),
(N'hyderabad', N'Secunderabad|Cyberabad'),
(N'jaipur',    N'Pink City'),
(N'lucknow',   N'Lakhnau');

UPDATE c
SET    c.AltNames   = a.AltNames,
       c.ModifiedBy = N'seed',
       c.ModifiedOn = SYSDATETIMEOFFSET()
FROM   dbo.Cities c
JOIN   @Alt a ON a.Slug = c.Slug
WHERE  ISNULL(c.AltNames, N'') <> a.AltNames;

DECLARE @Total int = (SELECT COUNT(*) FROM dbo.Cities WHERE AltNames IS NOT NULL);
PRINT CONCAT('City alternate names: ', @Total, ' cities have search aliases.');

COMMIT TRANSACTION;
GO
