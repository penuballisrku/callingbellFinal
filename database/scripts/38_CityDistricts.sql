/* =====================================================================
   38_CityDistricts.sql
   Cities.District: the district (second-level division) a city lies in, shown with areas and PIN codes in the location box
   ("Mansoorabad · Hyderabad district · PIN 500068"). The city catalogue agent fills it from GeoNames (admin2Codes.txt) for every
   city it imports or links, curated cities included.

   Countries imported before this column existed are marked for re-import (ImportedOn cleared), so the agent runs again on the
   next visit and fills the districts. Re-importing never removes cities or changes curated ones beyond their GeoNames link
   and district.

   Idempotent. Safe to run on its own against any database, e.g.
     sqlcmd -S .\SQLEXPRESS -E -C -I -d CallingBell_Prod -i 38_CityDistricts.sql
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.Cities', 'District') IS NULL
    ALTER TABLE dbo.Cities ADD District nvarchar(100) NULL;
GO

-- Countries with imported cities still missing their districts: import again.
UPDATE cc
SET ImportedOn = NULL
FROM dbo.CountryCatalogs cc
WHERE cc.ImportedOn IS NOT NULL
  AND EXISTS (SELECT 1 FROM dbo.Cities c JOIN dbo.States s ON s.Id = c.StateId
              WHERE s.CountryCode = cc.CountryCode AND c.ExternalRef LIKE N'geonames:%' AND c.District IS NULL);
GO
