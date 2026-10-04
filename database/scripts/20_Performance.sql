/* =====================================================================================
   Calling Bell - 20_Performance.sql
   Database-level performance settings and indexes for the portal's hot read paths.

   * Indexes cover the queries every page makes (taken from SQL Server's missing-index
     suggestions for this workload): active cities by state, areas of a city, recent
     published reviews, booking/enquiry counts, and listed businesses by city.
   * READ_COMMITTED_SNAPSHOT: page reads use row versions instead of waiting for the
     background agents' writes (city import, area discovery) to commit.
   * Statistics are refreshed so the new indexes are used straight away.

   * Requires 00_Schema.sql. Idempotent: every change is guarded.
   * Switching READ_COMMITTED_SNAPSHOT briefly closes other connections to this database
     (WITH ROLLBACK IMMEDIATE); running APIs simply reconnect.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------- Indexes ---------- */
-- Active cities of a country (joined through States), with what the city lists and location lookups read.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cities_Active_State' AND object_id = OBJECT_ID('dbo.Cities'))
    CREATE INDEX IX_Cities_Active_State ON dbo.Cities (IsActive, IsDeleted, StateId)
        INCLUDE (Name, Slug, Latitude, Longitude, Source, SortOrder, Population, IsPopular, ImageUrl, AltNames);

-- Areas of a city (city/area dropdowns, search parsing, nearest-area lookups).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Areas_City_Active' AND object_id = OBJECT_ID('dbo.Areas'))
    CREATE INDEX IX_Areas_City_Active ON dbo.Areas (CityId, IsActive, IsDeleted)
        INCLUDE (Name, Slug, Pincode, AreaType, ParentAreaId, AltNames, Latitude, Longitude);

-- Most recent published reviews (home page reviews, ratings summaries).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Reviews_Status_CreatedOn' AND object_id = OBJECT_ID('dbo.Reviews'))
    CREATE INDEX IX_Reviews_Status_CreatedOn ON dbo.Reviews (Status, IsDeleted, CreatedOn DESC)
        INCLUDE (BusinessId, Rating, CustomerUserId, IsVerifiedVisit);

-- Booking counts by status (popular services, dashboards).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Status' AND object_id = OBJECT_ID('dbo.Bookings'))
    CREATE INDEX IX_Bookings_Status ON dbo.Bookings (Status, IsDeleted) INCLUDE (BusinessId, ServiceId, CreatedOn);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_CreatedOn' AND object_id = OBJECT_ID('dbo.Bookings'))
    CREATE INDEX IX_Bookings_CreatedOn ON dbo.Bookings (IsDeleted, CreatedOn) INCLUDE (BusinessId, ServiceId, Status);

-- Recent enquiries (dashboards, business growth stats).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Enquiries_CreatedOn' AND object_id = OBJECT_ID('dbo.Enquiries'))
    CREATE INDEX IX_Enquiries_CreatedOn ON dbo.Enquiries (IsDeleted, CreatedOn) INCLUDE (BusinessId, Status);

-- Listed businesses per city / category (business counts on city and category lists).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Businesses_Status_City' AND object_id = OBJECT_ID('dbo.Businesses'))
    CREATE INDEX IX_Businesses_Status_City ON dbo.Businesses (Status, IsDeleted, CityId) INCLUDE (CategoryId, SubCategoryId, AreaId);
GO

/* ---------- Readers don't wait for writers ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 1)
BEGIN
    DECLARE @sql nvarchar(400) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;';
    EXEC sys.sp_executesql @sql;
END
GO

/* ---------- Fresh statistics ---------- */
UPDATE STATISTICS dbo.Cities;
UPDATE STATISTICS dbo.Areas;
UPDATE STATISTICS dbo.Businesses;
UPDATE STATISTICS dbo.Reviews;
UPDATE STATISTICS dbo.Bookings;
UPDATE STATISTICS dbo.Enquiries;
GO

PRINT 'Performance indexes and settings applied.';
GO
