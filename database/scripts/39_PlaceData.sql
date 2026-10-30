/* =====================================================================================
   CallInBell - 39_PlaceData.sql
   Place data imported from open sources, kept within each source's licence.

   * dbo.PostOffices: India Post's All India Pincode Directory (data.gov.in, Government Open
     Data License - India), loaded by the PIN code directory agent (PincodeDirectory:ApiKey
     or PincodeDirectory:CsvPath). One row per post office: PIN code, district, state.
   * dbo.Areas.District: the district of the area's PIN code, filled from PostOffices by the
     same agent (the most common district among that PIN code's post offices).
   * dbo.OsmPlaces (+ OsmPlaceSubCategories, OsmCityImports): named places from OpenStreetMap
     (© OpenStreetMap contributors, ODbL: attribution required), imported per city and
     sub-category by the OpenStreetMap catalogue agent from each sub-category's OsmTags. The
     "AI recommended" search reads them instead of waiting on Overpass.
   * dbo.GooglePlaceRefs: Google Maps place ids seen in search results, with the city and
     sub-category searched. Google's terms allow storing place ids indefinitely and nothing
     else from Places (names, addresses, ratings, photos, hours are always fetched live).

   The agents fill the tables; this script only creates them. Idempotent.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.PostOffices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PostOffices
    (
        Id         uniqueidentifier NOT NULL CONSTRAINT PK_PostOffices PRIMARY KEY CONSTRAINT DF_PostOffices_Id DEFAULT NEWSEQUENTIALID(),
        OfficeName nvarchar(150)    NOT NULL,
        Pincode    nvarchar(10)     NOT NULL,
        OfficeType nvarchar(20)     NULL,
        Delivery   nvarchar(30)     NULL,
        Division   nvarchar(150)    NULL,
        Region     nvarchar(150)    NULL,
        Circle     nvarchar(150)    NULL,
        District   nvarchar(100)    NOT NULL,
        StateName  nvarchar(100)    NOT NULL,
        Latitude   decimal(9, 6)    NULL,
        Longitude  decimal(9, 6)    NULL,
        CreatedBy  nvarchar(450)    NULL,
        CreatedOn  datetimeoffset   NOT NULL CONSTRAINT DF_PostOffices_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy nvarchar(450)    NULL,
        ModifiedOn datetimeoffset   NULL,
        IsDeleted  bit              NOT NULL CONSTRAINT DF_PostOffices_IsDeleted DEFAULT 0
    );
END
GO
-- Area districts and PIN code look-ups.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PostOffices_Pincode' AND object_id = OBJECT_ID(N'dbo.PostOffices'))
    CREATE INDEX IX_PostOffices_Pincode ON dbo.PostOffices (Pincode) INCLUDE (District, StateName) WHERE IsDeleted = 0;
GO

IF COL_LENGTH('dbo.Areas', 'District') IS NULL
    ALTER TABLE dbo.Areas ADD District nvarchar(100) NULL;
GO

IF OBJECT_ID(N'dbo.OsmPlaces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OsmPlaces
    (
        Id           uniqueidentifier NOT NULL CONSTRAINT PK_OsmPlaces PRIMARY KEY CONSTRAINT DF_OsmPlaces_Id DEFAULT NEWSEQUENTIALID(),
        OsmRef       nvarchar(40)     NOT NULL,
        Name         nvarchar(200)    NOT NULL,
        Kind         nvarchar(80)     NULL,
        Address      nvarchar(400)    NULL,
        Phone        nvarchar(60)     NULL,
        Website      nvarchar(400)    NULL,
        OpeningHours nvarchar(400)    NULL,
        Latitude     decimal(9, 6)    NOT NULL,
        Longitude    decimal(9, 6)    NOT NULL,
        CityId       uniqueidentifier NULL CONSTRAINT FK_OsmPlaces_Cities REFERENCES dbo.Cities (Id),
        LastSeenOn   datetimeoffset   NOT NULL,
        CreatedBy    nvarchar(450)    NULL,
        CreatedOn    datetimeoffset   NOT NULL CONSTRAINT DF_OsmPlaces_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy   nvarchar(450)    NULL,
        ModifiedOn   datetimeoffset   NULL,
        IsDeleted    bit              NOT NULL CONSTRAINT DF_OsmPlaces_IsDeleted DEFAULT 0,
        CONSTRAINT UQ_OsmPlaces_OsmRef UNIQUE (OsmRef)
    );
END
GO
-- Searches: places in a box around a point.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OsmPlaces_Location' AND object_id = OBJECT_ID(N'dbo.OsmPlaces'))
    CREATE INDEX IX_OsmPlaces_Location ON dbo.OsmPlaces (Latitude, Longitude) INCLUDE (LastSeenOn) WHERE IsDeleted = 0;
GO

IF OBJECT_ID(N'dbo.OsmPlaceSubCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OsmPlaceSubCategories
    (
        OsmPlaceId    uniqueidentifier NOT NULL CONSTRAINT FK_OsmPlaceSubCategories_OsmPlaces REFERENCES dbo.OsmPlaces (Id),
        SubCategoryId uniqueidentifier NOT NULL CONSTRAINT FK_OsmPlaceSubCategories_SubCategories REFERENCES dbo.SubCategories (Id),
        CONSTRAINT PK_OsmPlaceSubCategories PRIMARY KEY (OsmPlaceId, SubCategoryId)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OsmPlaceSubCategories_SubCategoryId' AND object_id = OBJECT_ID(N'dbo.OsmPlaceSubCategories'))
    CREATE INDEX IX_OsmPlaceSubCategories_SubCategoryId ON dbo.OsmPlaceSubCategories (SubCategoryId, OsmPlaceId);
GO

IF OBJECT_ID(N'dbo.OsmCityImports', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OsmCityImports
    (
        CityId        uniqueidentifier NOT NULL CONSTRAINT FK_OsmCityImports_Cities REFERENCES dbo.Cities (Id),
        SubCategoryId uniqueidentifier NOT NULL CONSTRAINT FK_OsmCityImports_SubCategories REFERENCES dbo.SubCategories (Id),
        ImportedOn    datetimeoffset   NOT NULL,
        PlaceCount    int              NOT NULL CONSTRAINT DF_OsmCityImports_PlaceCount DEFAULT 0,
        Note          nvarchar(400)    NULL,
        CONSTRAINT PK_OsmCityImports PRIMARY KEY (CityId, SubCategoryId)
    );
END
GO

IF OBJECT_ID(N'dbo.GooglePlaceRefs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GooglePlaceRefs
    (
        PlaceId       nvarchar(300)    NOT NULL CONSTRAINT PK_GooglePlaceRefs PRIMARY KEY,
        CityId        uniqueidentifier NULL CONSTRAINT FK_GooglePlaceRefs_Cities REFERENCES dbo.Cities (Id),
        SubCategoryId uniqueidentifier NULL CONSTRAINT FK_GooglePlaceRefs_SubCategories REFERENCES dbo.SubCategories (Id),
        FirstSeenOn   datetimeoffset   NOT NULL,
        LastSeenOn    datetimeoffset   NOT NULL,
        SeenCount     int              NOT NULL CONSTRAINT DF_GooglePlaceRefs_SeenCount DEFAULT 1
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GooglePlaceRefs_City_SubCategory' AND object_id = OBJECT_ID(N'dbo.GooglePlaceRefs'))
    CREATE INDEX IX_GooglePlaceRefs_City_SubCategory ON dbo.GooglePlaceRefs (CityId, SubCategoryId);
GO
