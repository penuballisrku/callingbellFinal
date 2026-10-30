/* =====================================================================================
   CallInBell - 41_PlaceDetails.sql
   More of what OpenStreetMap / Geoapify know about each stored place, shown on the
   "AI recommended" cards: email, a short description, and facts from its tags
   (Features, '|' separated: "Wheelchair accessible", "Cuisine: South Indian", ...).
   The OpenStreetMap catalogue agent fills them on its next refresh of each city.

   Idempotent. Requires 39_PlaceData.sql.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.OsmPlaces', 'Email') IS NULL
    ALTER TABLE dbo.OsmPlaces ADD Email nvarchar(200) NULL;
IF COL_LENGTH('dbo.OsmPlaces', 'Description') IS NULL
    ALTER TABLE dbo.OsmPlaces ADD Description nvarchar(1000) NULL;
IF COL_LENGTH('dbo.OsmPlaces', 'Features') IS NULL
    ALTER TABLE dbo.OsmPlaces ADD Features nvarchar(1000) NULL;
GO
