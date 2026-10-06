/* =====================================================================================
   Calling Bell - 00_Schema.sql
   Extends the existing CallingBell schema (created by the original EF Core migrations
   InitialCreate + AddSubCategoriesAndMedia) to the full platform model.

   * Idempotent: every change is guarded, safe to run repeatedly.
   * Non-destructive: existing tables/rows are preserved; legacy CreatedAt/UpdatedAt
     columns are renamed to the CreatedOn/ModifiedOn audit standard.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------- 1. Audit-column standardisation on pre-existing tables ---------- */
IF COL_LENGTH('dbo.Categories','CreatedAt') IS NOT NULL AND COL_LENGTH('dbo.Categories','CreatedOn') IS NULL
    EXEC sp_rename 'dbo.Categories.CreatedAt', 'CreatedOn', 'COLUMN';
IF COL_LENGTH('dbo.SubCategories','CreatedAt') IS NOT NULL AND COL_LENGTH('dbo.SubCategories','CreatedOn') IS NULL
    EXEC sp_rename 'dbo.SubCategories.CreatedAt', 'CreatedOn', 'COLUMN';
IF COL_LENGTH('dbo.Businesses','CreatedAt') IS NOT NULL AND COL_LENGTH('dbo.Businesses','CreatedOn') IS NULL
    EXEC sp_rename 'dbo.Businesses.CreatedAt', 'CreatedOn', 'COLUMN';
IF COL_LENGTH('dbo.BusinessServices','CreatedAt') IS NOT NULL AND COL_LENGTH('dbo.BusinessServices','CreatedOn') IS NULL
    EXEC sp_rename 'dbo.BusinessServices.CreatedAt', 'CreatedOn', 'COLUMN';
IF COL_LENGTH('dbo.Media','CreatedAt') IS NOT NULL AND COL_LENGTH('dbo.Media','CreatedOn') IS NULL
    EXEC sp_rename 'dbo.Media.CreatedAt', 'CreatedOn', 'COLUMN';
IF COL_LENGTH('dbo.Media','UpdatedAt') IS NOT NULL AND COL_LENGTH('dbo.Media','ModifiedOn') IS NULL
    EXEC sp_rename 'dbo.Media.UpdatedAt', 'ModifiedOn', 'COLUMN';
GO

DECLARE @t sysname;
DECLARE audit_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT v.t FROM (VALUES ('Categories'),('SubCategories'),('Businesses'),('BusinessServices'),('Media')) v(t);
OPEN audit_cursor;
FETCH NEXT FROM audit_cursor INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @sql nvarchar(max) = N'';
    IF COL_LENGTH('dbo.' + @t, 'CreatedBy')  IS NULL SET @sql += N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' ADD CreatedBy nvarchar(450) NULL;';
    IF COL_LENGTH('dbo.' + @t, 'ModifiedBy') IS NULL SET @sql += N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' ADD ModifiedBy nvarchar(450) NULL;';
    IF COL_LENGTH('dbo.' + @t, 'ModifiedOn') IS NULL SET @sql += N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' ADD ModifiedOn datetimeoffset NULL;';
    IF COL_LENGTH('dbo.' + @t, 'IsDeleted')  IS NULL SET @sql += N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' ADD IsDeleted bit NOT NULL CONSTRAINT ' + QUOTENAME('DF_' + @t + '_IsDeleted') + N' DEFAULT (0);';
    IF @sql <> N'' EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM audit_cursor INTO @t;
END
CLOSE audit_cursor;
DEALLOCATE audit_cursor;
GO

/* ---------- 2. Locations ---------- */
IF OBJECT_ID('dbo.States','U') IS NULL
CREATE TABLE dbo.States (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_States PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Name        nvarchar(100)    NOT NULL,
    Code        nvarchar(10)     NOT NULL,
    Slug        nvarchar(120)    NOT NULL,
    SortOrder   int              NOT NULL DEFAULT 0,
    IsActive    bit              NOT NULL DEFAULT 1,
    CreatedBy   nvarchar(450)    NULL,
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy  nvarchar(450)    NULL,
    ModifiedOn  datetimeoffset   NULL,
    IsDeleted   bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_States_Slug UNIQUE (Slug)
);
GO

IF OBJECT_ID('dbo.Cities','U') IS NULL
CREATE TABLE dbo.Cities (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_Cities PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    StateId     uniqueidentifier NOT NULL CONSTRAINT FK_Cities_States REFERENCES dbo.States(Id),
    Name        nvarchar(100)    NOT NULL,
    Slug        nvarchar(120)    NOT NULL,
    Latitude    decimal(9,6)     NULL,
    Longitude   decimal(9,6)     NULL,
    ImageUrl    nvarchar(500)    NULL,
    IsPopular   bit              NOT NULL DEFAULT 0,
    SortOrder   int              NOT NULL DEFAULT 0,
    IsActive    bit              NOT NULL DEFAULT 1,
    CreatedBy   nvarchar(450)    NULL,
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy  nvarchar(450)    NULL,
    ModifiedOn  datetimeoffset   NULL,
    IsDeleted   bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Cities_Slug UNIQUE (Slug)
);
GO

IF OBJECT_ID('dbo.Areas','U') IS NULL
CREATE TABLE dbo.Areas (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_Areas PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    CityId      uniqueidentifier NOT NULL CONSTRAINT FK_Areas_Cities REFERENCES dbo.Cities(Id),
    Name        nvarchar(120)    NOT NULL,
    Slug        nvarchar(140)    NOT NULL,
    Pincode     nvarchar(10)     NOT NULL,
    Latitude    decimal(9,6)     NULL,
    Longitude   decimal(9,6)     NULL,
    IsActive    bit              NOT NULL DEFAULT 1,
    CreatedBy   nvarchar(450)    NULL,
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy  nvarchar(450)    NULL,
    ModifiedOn  datetimeoffset   NULL,
    IsDeleted   bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Areas_City_Slug UNIQUE (CityId, Slug)
);
GO

/* ---------- 3. Lookup values ---------- */
IF OBJECT_ID('dbo.LookupValues','U') IS NULL
CREATE TABLE dbo.LookupValues (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_LookupValues PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    LookupType  nvarchar(50)     NOT NULL,
    Code        nvarchar(50)     NOT NULL,
    Name        nvarchar(100)    NOT NULL,
    Description nvarchar(300)    NULL,
    ColorHex    nvarchar(9)      NULL,
    SortOrder   int              NOT NULL DEFAULT 0,
    IsActive    bit              NOT NULL DEFAULT 1,
    CreatedBy   nvarchar(450)    NULL,
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy  nvarchar(450)    NULL,
    ModifiedOn  datetimeoffset   NULL,
    IsDeleted   bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_LookupValues_Type_Code UNIQUE (LookupType, Code)
);
GO

/* ---------- 4. Identity extensions ----------
   AspNetUsers already carries DisplayName (the person's full name) and CreatedAt from the original model.
   CreatedAt is renamed to the CreatedOn audit standard; DisplayName is reused as-is. */
IF COL_LENGTH('dbo.AspNetUsers','CreatedAt') IS NOT NULL AND COL_LENGTH('dbo.AspNetUsers','CreatedOn') IS NULL
BEGIN
    EXEC sp_rename 'dbo.AspNetUsers.CreatedAt', 'CreatedOn', 'COLUMN';
    ALTER TABLE dbo.AspNetUsers ADD CONSTRAINT DF_AspNetUsers_CreatedOn DEFAULT (SYSDATETIMEOFFSET()) FOR CreatedOn;
END
IF COL_LENGTH('dbo.AspNetUsers','AvatarUrl')   IS NULL ALTER TABLE dbo.AspNetUsers ADD AvatarUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.AspNetUsers','UserType')    IS NULL ALTER TABLE dbo.AspNetUsers ADD UserType nvarchar(32) NOT NULL CONSTRAINT DF_AspNetUsers_UserType DEFAULT (N'Customer');
IF COL_LENGTH('dbo.AspNetUsers','CityId')      IS NULL ALTER TABLE dbo.AspNetUsers ADD CityId uniqueidentifier NULL CONSTRAINT FK_AspNetUsers_Cities REFERENCES dbo.Cities(Id);
IF COL_LENGTH('dbo.AspNetUsers','IsActive')    IS NULL ALTER TABLE dbo.AspNetUsers ADD IsActive bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT (1);
IF COL_LENGTH('dbo.AspNetUsers','LastLoginOn') IS NULL ALTER TABLE dbo.AspNetUsers ADD LastLoginOn datetimeoffset NULL;
IF COL_LENGTH('dbo.AspNetUsers','CreatedBy')   IS NULL ALTER TABLE dbo.AspNetUsers ADD CreatedBy nvarchar(450) NULL;
IF COL_LENGTH('dbo.AspNetUsers','ModifiedBy')  IS NULL ALTER TABLE dbo.AspNetUsers ADD ModifiedBy nvarchar(450) NULL;
IF COL_LENGTH('dbo.AspNetUsers','ModifiedOn')  IS NULL ALTER TABLE dbo.AspNetUsers ADD ModifiedOn datetimeoffset NULL;
IF COL_LENGTH('dbo.AspNetUsers','IsDeleted')   IS NULL ALTER TABLE dbo.AspNetUsers ADD IsDeleted bit NOT NULL CONSTRAINT DF_AspNetUsers_IsDeleted DEFAULT (0);
GO

/* ---------- 5. Category / sub-category presentation ---------- */
IF COL_LENGTH('dbo.Categories','ImageUrl')   IS NULL ALTER TABLE dbo.Categories ADD ImageUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.Categories','IconUrl')    IS NULL ALTER TABLE dbo.Categories ADD IconUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.Categories','BannerUrl')  IS NULL ALTER TABLE dbo.Categories ADD BannerUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.Categories','AltText')    IS NULL ALTER TABLE dbo.Categories ADD AltText nvarchar(300) NULL;
IF COL_LENGTH('dbo.Categories','ColorHex')   IS NULL ALTER TABLE dbo.Categories ADD ColorHex nvarchar(9) NULL;
IF COL_LENGTH('dbo.Categories','SortOrder')  IS NULL ALTER TABLE dbo.Categories ADD SortOrder int NOT NULL CONSTRAINT DF_Categories_SortOrder DEFAULT (0);
IF COL_LENGTH('dbo.Categories','IsFeatured') IS NULL ALTER TABLE dbo.Categories ADD IsFeatured bit NOT NULL CONSTRAINT DF_Categories_IsFeatured DEFAULT (0);

IF COL_LENGTH('dbo.SubCategories','ImageUrl')   IS NULL ALTER TABLE dbo.SubCategories ADD ImageUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.SubCategories','IconUrl')    IS NULL ALTER TABLE dbo.SubCategories ADD IconUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.SubCategories','AltText')    IS NULL ALTER TABLE dbo.SubCategories ADD AltText nvarchar(300) NULL;
IF COL_LENGTH('dbo.SubCategories','SortOrder')  IS NULL ALTER TABLE dbo.SubCategories ADD SortOrder int NOT NULL CONSTRAINT DF_SubCategories_SortOrder DEFAULT (0);
IF COL_LENGTH('dbo.SubCategories','IsFeatured') IS NULL ALTER TABLE dbo.SubCategories ADD IsFeatured bit NOT NULL CONSTRAINT DF_SubCategories_IsFeatured DEFAULT (0);
-- OpenStreetMap tags for finding nearby places outside the platform (search results, see 18_ExternalSearch.sql).
IF COL_LENGTH('dbo.SubCategories','OsmTags')    IS NULL ALTER TABLE dbo.SubCategories ADD OsmTags nvarchar(400) NULL;
GO

/* ---------- 6. Business profile extensions ---------- */
IF COL_LENGTH('dbo.Businesses','SubCategoryId')  IS NULL ALTER TABLE dbo.Businesses ADD SubCategoryId uniqueidentifier NULL CONSTRAINT FK_Businesses_SubCategories REFERENCES dbo.SubCategories(Id);
IF COL_LENGTH('dbo.Businesses','CityId')         IS NULL ALTER TABLE dbo.Businesses ADD CityId uniqueidentifier NULL CONSTRAINT FK_Businesses_Cities REFERENCES dbo.Cities(Id);
IF COL_LENGTH('dbo.Businesses','AreaId')         IS NULL ALTER TABLE dbo.Businesses ADD AreaId uniqueidentifier NULL CONSTRAINT FK_Businesses_Areas REFERENCES dbo.Areas(Id);
IF COL_LENGTH('dbo.Businesses','Tagline')        IS NULL ALTER TABLE dbo.Businesses ADD Tagline nvarchar(200) NULL;
IF COL_LENGTH('dbo.Businesses','AddressLine')    IS NULL ALTER TABLE dbo.Businesses ADD AddressLine nvarchar(300) NULL;
IF COL_LENGTH('dbo.Businesses','Landmark')       IS NULL ALTER TABLE dbo.Businesses ADD Landmark nvarchar(150) NULL;
IF COL_LENGTH('dbo.Businesses','Pincode')        IS NULL ALTER TABLE dbo.Businesses ADD Pincode nvarchar(10) NULL;
IF COL_LENGTH('dbo.Businesses','Latitude')       IS NULL ALTER TABLE dbo.Businesses ADD Latitude decimal(9,6) NULL;
IF COL_LENGTH('dbo.Businesses','Longitude')      IS NULL ALTER TABLE dbo.Businesses ADD Longitude decimal(9,6) NULL;
IF COL_LENGTH('dbo.Businesses','WhatsAppNumber') IS NULL ALTER TABLE dbo.Businesses ADD WhatsAppNumber nvarchar(32) NULL;
IF COL_LENGTH('dbo.Businesses','Email')          IS NULL ALTER TABLE dbo.Businesses ADD Email nvarchar(256) NULL;
IF COL_LENGTH('dbo.Businesses','Website')        IS NULL ALTER TABLE dbo.Businesses ADD Website nvarchar(300) NULL;
IF COL_LENGTH('dbo.Businesses','LogoUrl')        IS NULL ALTER TABLE dbo.Businesses ADD LogoUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.Businesses','CoverImageUrl')  IS NULL ALTER TABLE dbo.Businesses ADD CoverImageUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.Businesses','YearEstablished') IS NULL ALTER TABLE dbo.Businesses ADD YearEstablished int NULL;
IF COL_LENGTH('dbo.Businesses','TeamSize')       IS NULL ALTER TABLE dbo.Businesses ADD TeamSize int NULL;
IF COL_LENGTH('dbo.Businesses','Languages')      IS NULL ALTER TABLE dbo.Businesses ADD Languages nvarchar(200) NULL;
IF COL_LENGTH('dbo.Businesses','ResponseTimeMinutes') IS NULL ALTER TABLE dbo.Businesses ADD ResponseTimeMinutes int NULL;
IF COL_LENGTH('dbo.Businesses','AcceptsOnlineBooking') IS NULL ALTER TABLE dbo.Businesses ADD AcceptsOnlineBooking bit NOT NULL CONSTRAINT DF_Businesses_AcceptsOnlineBooking DEFAULT (0);
IF COL_LENGTH('dbo.Businesses','OffersVideoConsultation') IS NULL ALTER TABLE dbo.Businesses ADD OffersVideoConsultation bit NOT NULL CONSTRAINT DF_Businesses_OffersVideoConsultation DEFAULT (0);
IF COL_LENGTH('dbo.Businesses','OffersHomeService') IS NULL ALTER TABLE dbo.Businesses ADD OffersHomeService bit NOT NULL CONSTRAINT DF_Businesses_OffersHomeService DEFAULT (0);
IF COL_LENGTH('dbo.Businesses','VerificationStatus') IS NULL ALTER TABLE dbo.Businesses ADD VerificationStatus nvarchar(32) NOT NULL CONSTRAINT DF_Businesses_VerificationStatus DEFAULT (N'Pending');
IF COL_LENGTH('dbo.Businesses','VerifiedOn')     IS NULL ALTER TABLE dbo.Businesses ADD VerifiedOn datetimeoffset NULL;
IF COL_LENGTH('dbo.Businesses','IsFeatured')     IS NULL ALTER TABLE dbo.Businesses ADD IsFeatured bit NOT NULL CONSTRAINT DF_Businesses_IsFeatured DEFAULT (0);
IF COL_LENGTH('dbo.Businesses','AvailabilityStatus') IS NULL ALTER TABLE dbo.Businesses ADD AvailabilityStatus nvarchar(40) NOT NULL CONSTRAINT DF_Businesses_AvailabilityStatus DEFAULT (N'Offline');
IF COL_LENGTH('dbo.Businesses','LastSeenOn')     IS NULL ALTER TABLE dbo.Businesses ADD LastSeenOn datetimeoffset NULL;
IF COL_LENGTH('dbo.Businesses','AverageRating')  IS NULL ALTER TABLE dbo.Businesses ADD AverageRating decimal(3,2) NOT NULL CONSTRAINT DF_Businesses_AverageRating DEFAULT (0);
IF COL_LENGTH('dbo.Businesses','ReviewCount')    IS NULL ALTER TABLE dbo.Businesses ADD ReviewCount int NOT NULL CONSTRAINT DF_Businesses_ReviewCount DEFAULT (0);

IF COL_LENGTH('dbo.BusinessServices','PriceUnit') IS NULL ALTER TABLE dbo.BusinessServices ADD PriceUnit nvarchar(40) NULL;
IF COL_LENGTH('dbo.BusinessServices','ImageUrl')  IS NULL ALTER TABLE dbo.BusinessServices ADD ImageUrl nvarchar(500) NULL;
IF COL_LENGTH('dbo.BusinessServices','IsPopular') IS NULL ALTER TABLE dbo.BusinessServices ADD IsPopular bit NOT NULL CONSTRAINT DF_BusinessServices_IsPopular DEFAULT (0);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Businesses_AspNetUsers_OwnerUserId')
    ALTER TABLE dbo.Businesses ADD CONSTRAINT FK_Businesses_AspNetUsers_OwnerUserId FOREIGN KEY (OwnerUserId) REFERENCES dbo.AspNetUsers(Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Businesses_City_SubCategory_Status' AND object_id = OBJECT_ID('dbo.Businesses'))
    CREATE INDEX IX_Businesses_City_SubCategory_Status ON dbo.Businesses (CityId, SubCategoryId, Status) INCLUDE (AverageRating, ReviewCount, AvailabilityStatus);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Businesses_OwnerUserId' AND object_id = OBJECT_ID('dbo.Businesses'))
    CREATE INDEX IX_Businesses_OwnerUserId ON dbo.Businesses (OwnerUserId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_BusinessServices_Business_Name' AND object_id = OBJECT_ID('dbo.BusinessServices'))
    CREATE UNIQUE INDEX UQ_BusinessServices_Business_Name ON dbo.BusinessServices (BusinessId, Name);
GO

IF OBJECT_ID('dbo.BusinessHours','U') IS NULL
CREATE TABLE dbo.BusinessHours (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_BusinessHours PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BusinessId  uniqueidentifier NOT NULL CONSTRAINT FK_BusinessHours_Businesses REFERENCES dbo.Businesses(Id) ON DELETE CASCADE,
    DayOfWeek   tinyint          NOT NULL CONSTRAINT CK_BusinessHours_Day CHECK (DayOfWeek BETWEEN 0 AND 6),
    OpenTime    time(0)          NULL,
    CloseTime   time(0)          NULL,
    IsClosed    bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_BusinessHours_Business_Day UNIQUE (BusinessId, DayOfWeek)
);
GO

IF OBJECT_ID('dbo.BusinessImages','U') IS NULL
CREATE TABLE dbo.BusinessImages (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_BusinessImages PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BusinessId      uniqueidentifier NOT NULL CONSTRAINT FK_BusinessImages_Businesses REFERENCES dbo.Businesses(Id) ON DELETE CASCADE,
    ImageUrl        nvarchar(500)    NOT NULL,
    ThumbnailUrl    nvarchar(500)    NULL,
    MobileImageUrl  nvarchar(500)    NULL,
    DesktopImageUrl nvarchar(500)    NULL,
    AltText         nvarchar(300)    NULL,
    Caption         nvarchar(200)    NULL,
    IsPrimary       bit              NOT NULL DEFAULT 0,
    SortOrder       int              NOT NULL DEFAULT 0,
    CreatedBy       nvarchar(450)    NULL,
    CreatedOn       datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy      nvarchar(450)    NULL,
    ModifiedOn      datetimeoffset   NULL,
    IsDeleted       bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_BusinessImages_Business_Sort UNIQUE (BusinessId, SortOrder)
);
GO

/* Promotional videos uploaded by the business (bytes in dbo.Media, EntityType 'BusinessVideo'; poster 'BusinessVideoPoster'). */
IF OBJECT_ID('dbo.BusinessVideos','U') IS NULL
CREATE TABLE dbo.BusinessVideos (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_BusinessVideos PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BusinessId      uniqueidentifier NOT NULL CONSTRAINT FK_BusinessVideos_Businesses REFERENCES dbo.Businesses(Id),
    Title           nvarchar(150)    NOT NULL,
    VideoUrl        nvarchar(500)    NOT NULL,
    PosterUrl       nvarchar(500)    NULL,
    ContentType     nvarchar(100)    NOT NULL,
    FileSize        bigint           NOT NULL,
    DurationSeconds int              NULL,
    SortOrder       int              NOT NULL DEFAULT 0,
    CreatedBy       nvarchar(450)    NULL,
    CreatedOn       datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy      nvarchar(450)    NULL,
    ModifiedOn      datetimeoffset   NULL,
    IsDeleted       bit              NOT NULL DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BusinessVideos_Business' AND object_id = OBJECT_ID('dbo.BusinessVideos'))
    CREATE INDEX IX_BusinessVideos_Business ON dbo.BusinessVideos (BusinessId, SortOrder) WHERE IsDeleted = 0;
GO

/* Social media profiles (Platform = LookupValues 'SocialPlatform'). */
IF OBJECT_ID('dbo.BusinessSocialLinks','U') IS NULL
CREATE TABLE dbo.BusinessSocialLinks (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_BusinessSocialLinks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BusinessId  uniqueidentifier NOT NULL CONSTRAINT FK_BusinessSocialLinks_Businesses REFERENCES dbo.Businesses(Id),
    Platform    nvarchar(32)     NOT NULL,
    Url         nvarchar(300)    NOT NULL,
    CreatedBy   nvarchar(450)    NULL,
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy  nvarchar(450)    NULL,
    ModifiedOn  datetimeoffset   NULL,
    IsDeleted   bit              NOT NULL DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_BusinessSocialLinks_Platform' AND object_id = OBJECT_ID('dbo.BusinessSocialLinks'))
    CREATE UNIQUE INDEX UX_BusinessSocialLinks_Platform ON dbo.BusinessSocialLinks (BusinessId, Platform) WHERE IsDeleted = 0;
GO

/* ---------- 7. Engagement: reviews, leads, bookings, favourites ---------- */
IF OBJECT_ID('dbo.Reviews','U') IS NULL
CREATE TABLE dbo.Reviews (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_Reviews PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BusinessId      uniqueidentifier NOT NULL CONSTRAINT FK_Reviews_Businesses REFERENCES dbo.Businesses(Id),
    CustomerUserId  nvarchar(450)    NOT NULL CONSTRAINT FK_Reviews_AspNetUsers REFERENCES dbo.AspNetUsers(Id),
    BookingId       uniqueidentifier NULL,
    Rating          tinyint          NOT NULL CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
    Title           nvarchar(150)    NULL,
    Comment         nvarchar(2000)   NOT NULL,
    Status          nvarchar(32)     NOT NULL DEFAULT N'Published',
    OwnerReply      nvarchar(1000)   NULL,
    RepliedOn       datetimeoffset   NULL,
    HelpfulCount    int              NOT NULL DEFAULT 0,
    IsVerifiedVisit bit              NOT NULL DEFAULT 0,
    ReportReason    nvarchar(300)    NULL,
    CreatedBy       nvarchar(450)    NULL,
    CreatedOn       datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy      nvarchar(450)    NULL,
    ModifiedOn      datetimeoffset   NULL,
    IsDeleted       bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Reviews_Business_Customer UNIQUE (BusinessId, CustomerUserId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Reviews_Business_Status' AND object_id = OBJECT_ID('dbo.Reviews'))
    CREATE INDEX IX_Reviews_Business_Status ON dbo.Reviews (BusinessId, Status, CreatedOn DESC);
GO

IF OBJECT_ID('dbo.Enquiries','U') IS NULL
CREATE TABLE dbo.Enquiries (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_Enquiries PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    EnquiryNumber   nvarchar(20)     NOT NULL,
    BusinessId      uniqueidentifier NOT NULL CONSTRAINT FK_Enquiries_Businesses REFERENCES dbo.Businesses(Id),
    ServiceId       uniqueidentifier NULL CONSTRAINT FK_Enquiries_BusinessServices REFERENCES dbo.BusinessServices(Id),
    CustomerUserId  nvarchar(450)    NULL CONSTRAINT FK_Enquiries_AspNetUsers REFERENCES dbo.AspNetUsers(Id),
    CustomerName    nvarchar(150)    NOT NULL,
    CustomerPhone   nvarchar(20)     NOT NULL,
    CustomerEmail   nvarchar(256)    NULL,
    EnquiryType     nvarchar(32)     NOT NULL DEFAULT N'Enquiry',
    Message         nvarchar(2000)   NOT NULL,
    PreferredDate   date             NULL,
    Budget          decimal(12,2)    NULL,
    QuotedAmount    decimal(12,2)    NULL,
    Status          nvarchar(32)     NOT NULL DEFAULT N'New',
    Source          nvarchar(32)     NOT NULL DEFAULT N'Profile',
    RespondedOn     datetimeoffset   NULL,
    CreatedBy       nvarchar(450)    NULL,
    CreatedOn       datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy      nvarchar(450)    NULL,
    ModifiedOn      datetimeoffset   NULL,
    IsDeleted       bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Enquiries_Number UNIQUE (EnquiryNumber)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Enquiries_Business_Created' AND object_id = OBJECT_ID('dbo.Enquiries'))
    CREATE INDEX IX_Enquiries_Business_Created ON dbo.Enquiries (BusinessId, CreatedOn DESC) INCLUDE (Status);
GO

IF OBJECT_ID('dbo.Bookings','U') IS NULL
CREATE TABLE dbo.Bookings (
    Id                 uniqueidentifier NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BookingNumber      nvarchar(20)     NOT NULL,
    BusinessId         uniqueidentifier NOT NULL CONSTRAINT FK_Bookings_Businesses REFERENCES dbo.Businesses(Id),
    ServiceId          uniqueidentifier NOT NULL CONSTRAINT FK_Bookings_BusinessServices REFERENCES dbo.BusinessServices(Id),
    CustomerUserId     nvarchar(450)    NOT NULL CONSTRAINT FK_Bookings_AspNetUsers REFERENCES dbo.AspNetUsers(Id),
    CustomerName       nvarchar(150)    NOT NULL,
    CustomerPhone      nvarchar(20)     NOT NULL,
    ScheduledStart     datetimeoffset   NOT NULL,
    ScheduledEnd       datetimeoffset   NOT NULL,
    Status             nvarchar(32)     NOT NULL DEFAULT N'Pending',
    Amount             decimal(12,2)    NOT NULL,
    CommissionAmount   decimal(12,2)    NOT NULL DEFAULT 0,
    PaymentStatus      nvarchar(32)     NOT NULL DEFAULT N'Pending',
    ServiceAddress     nvarchar(300)    NULL,
    Notes              nvarchar(1000)   NULL,
    CancellationReason nvarchar(300)    NULL,
    CreatedBy          nvarchar(450)    NULL,
    CreatedOn          datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy         nvarchar(450)    NULL,
    ModifiedOn         datetimeoffset   NULL,
    IsDeleted          bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Bookings_Number UNIQUE (BookingNumber),
    CONSTRAINT CK_Bookings_Schedule CHECK (ScheduledEnd > ScheduledStart)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Business_Start' AND object_id = OBJECT_ID('dbo.Bookings'))
    CREATE INDEX IX_Bookings_Business_Start ON dbo.Bookings (BusinessId, ScheduledStart) INCLUDE (Status, Amount);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Customer' AND object_id = OBJECT_ID('dbo.Bookings'))
    CREATE INDEX IX_Bookings_Customer ON dbo.Bookings (CustomerUserId, ScheduledStart DESC);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Reviews_Bookings')
    ALTER TABLE dbo.Reviews ADD CONSTRAINT FK_Reviews_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(Id);
GO

IF OBJECT_ID('dbo.Favorites','U') IS NULL
CREATE TABLE dbo.Favorites (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_Favorites PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    UserId      nvarchar(450)    NOT NULL CONSTRAINT FK_Favorites_AspNetUsers REFERENCES dbo.AspNetUsers(Id),
    BusinessId  uniqueidentifier NOT NULL CONSTRAINT FK_Favorites_Businesses REFERENCES dbo.Businesses(Id),
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT UQ_Favorites_User_Business UNIQUE (UserId, BusinessId)
);
GO

/* ---------- 8. Monetisation ---------- */
IF OBJECT_ID('dbo.SubscriptionPlans','U') IS NULL
CREATE TABLE dbo.SubscriptionPlans (
    Id                      uniqueidentifier NOT NULL CONSTRAINT PK_SubscriptionPlans PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Code                    nvarchar(30)     NOT NULL,
    Name                    nvarchar(60)     NOT NULL,
    Tagline                 nvarchar(200)    NULL,
    MonthlyPrice            decimal(12,2)    NOT NULL,
    AnnualPrice             decimal(12,2)    NOT NULL,
    LeadCredits             int              NOT NULL DEFAULT 0,
    MaxServices             int              NOT NULL DEFAULT 0,
    MaxImages               int              NOT NULL DEFAULT 0,
    IncludesFeaturedListing bit              NOT NULL DEFAULT 0,
    IncludesPrioritySupport bit              NOT NULL DEFAULT 0,
    Features                nvarchar(2000)   NOT NULL,
    ImageUrl                nvarchar(500)    NULL,
    BadgeColor              nvarchar(9)      NULL,
    IsPopular               bit              NOT NULL DEFAULT 0,
    SortOrder               int              NOT NULL DEFAULT 0,
    IsActive                bit              NOT NULL DEFAULT 1,
    CreatedBy               nvarchar(450)    NULL,
    CreatedOn               datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy              nvarchar(450)    NULL,
    ModifiedOn              datetimeoffset   NULL,
    IsDeleted               bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_SubscriptionPlans_Code UNIQUE (Code)
);
GO

IF OBJECT_ID('dbo.BusinessSubscriptions','U') IS NULL
CREATE TABLE dbo.BusinessSubscriptions (
    Id                 uniqueidentifier NOT NULL CONSTRAINT PK_BusinessSubscriptions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    SubscriptionNumber nvarchar(20)     NOT NULL,
    BusinessId         uniqueidentifier NOT NULL CONSTRAINT FK_BusinessSubscriptions_Businesses REFERENCES dbo.Businesses(Id),
    PlanId             uniqueidentifier NOT NULL CONSTRAINT FK_BusinessSubscriptions_Plans REFERENCES dbo.SubscriptionPlans(Id),
    BillingCycle       nvarchar(16)     NOT NULL DEFAULT N'Monthly',
    StartDate          date             NOT NULL,
    EndDate            date             NOT NULL,
    Amount             decimal(12,2)    NOT NULL,
    Status             nvarchar(32)     NOT NULL DEFAULT N'Active',
    AutoRenew          bit              NOT NULL DEFAULT 1,
    CreatedBy          nvarchar(450)    NULL,
    CreatedOn          datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy         nvarchar(450)    NULL,
    ModifiedOn         datetimeoffset   NULL,
    IsDeleted          bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_BusinessSubscriptions_Number UNIQUE (SubscriptionNumber),
    CONSTRAINT CK_BusinessSubscriptions_Dates CHECK (EndDate > StartDate)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BusinessSubscriptions_Business_Status' AND object_id = OBJECT_ID('dbo.BusinessSubscriptions'))
    CREATE INDEX IX_BusinessSubscriptions_Business_Status ON dbo.BusinessSubscriptions (BusinessId, Status, EndDate DESC);
GO

IF OBJECT_ID('dbo.Advertisements','U') IS NULL
CREATE TABLE dbo.Advertisements (
    Id               uniqueidentifier NOT NULL CONSTRAINT PK_Advertisements PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    CampaignCode     nvarchar(20)     NOT NULL,
    BusinessId       uniqueidentifier NOT NULL CONSTRAINT FK_Advertisements_Businesses REFERENCES dbo.Businesses(Id),
    AdType           nvarchar(32)     NOT NULL,
    Title            nvarchar(150)    NOT NULL,
    Description      nvarchar(500)    NULL,
    ImageUrl         nvarchar(500)    NULL,
    MobileImageUrl   nvarchar(500)    NULL,
    DesktopImageUrl  nvarchar(500)    NULL,
    AltText          nvarchar(300)    NULL,
    TargetCityId     uniqueidentifier NULL CONSTRAINT FK_Advertisements_Cities REFERENCES dbo.Cities(Id),
    TargetCategoryId uniqueidentifier NULL CONSTRAINT FK_Advertisements_Categories REFERENCES dbo.Categories(Id),
    StartDate        date             NOT NULL,
    EndDate          date             NOT NULL,
    Budget           decimal(12,2)    NOT NULL,
    AmountSpent      decimal(12,2)    NOT NULL DEFAULT 0,
    Impressions      int              NOT NULL DEFAULT 0,
    Clicks           int              NOT NULL DEFAULT 0,
    Status           nvarchar(32)     NOT NULL DEFAULT N'PendingApproval',
    CreatedBy        nvarchar(450)    NULL,
    CreatedOn        datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy       nvarchar(450)    NULL,
    ModifiedOn       datetimeoffset   NULL,
    IsDeleted        bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Advertisements_Code UNIQUE (CampaignCode),
    CONSTRAINT CK_Advertisements_Dates CHECK (EndDate >= StartDate)
);
GO

IF OBJECT_ID('dbo.Banners','U') IS NULL
CREATE TABLE dbo.Banners (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_Banners PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Code            nvarchar(40)     NOT NULL,
    Title           nvarchar(150)    NOT NULL,
    Subtitle        nvarchar(300)    NULL,
    CtaText         nvarchar(40)     NULL,
    LinkUrl         nvarchar(300)    NULL,
    ImageUrl        nvarchar(500)    NOT NULL,
    ThumbnailUrl    nvarchar(500)    NULL,
    MobileImageUrl  nvarchar(500)    NULL,
    DesktopImageUrl nvarchar(500)    NULL,
    AltText         nvarchar(300)    NULL,
    Placement       nvarchar(32)     NOT NULL DEFAULT N'HomeHero',
    SortOrder       int              NOT NULL DEFAULT 0,
    StartsOn        date             NULL,
    EndsOn          date             NULL,
    IsActive        bit              NOT NULL DEFAULT 1,
    CreatedBy       nvarchar(450)    NULL,
    CreatedOn       datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy      nvarchar(450)    NULL,
    ModifiedOn      datetimeoffset   NULL,
    IsDeleted       bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Banners_Code UNIQUE (Code)
);
GO

/* Online checkout attempts (Razorpay). A paid order activates a BusinessSubscription and writes a Payments ledger row. */
IF OBJECT_ID('dbo.PaymentOrders','U') IS NULL
CREATE TABLE dbo.PaymentOrders (
    Id               uniqueidentifier NOT NULL CONSTRAINT PK_PaymentOrders PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    OrderNumber      nvarchar(40)     NOT NULL,
    BusinessId       uniqueidentifier NOT NULL CONSTRAINT FK_PaymentOrders_Businesses REFERENCES dbo.Businesses(Id),
    PlanId           uniqueidentifier NOT NULL CONSTRAINT FK_PaymentOrders_Plans REFERENCES dbo.SubscriptionPlans(Id),
    BillingCycle     nvarchar(16)     NOT NULL,
    Amount           decimal(12,2)    NOT NULL,
    TaxAmount        decimal(12,2)    NOT NULL,
    TotalAmount      decimal(12,2)    NOT NULL,
    Currency         nvarchar(3)      NOT NULL DEFAULT N'INR',
    Gateway          nvarchar(20)     NOT NULL,
    GatewayOrderId   nvarchar(64)     NULL,
    GatewayPaymentId nvarchar(64)     NULL,
    PaymentMethod    nvarchar(32)     NULL,
    Status           nvarchar(20)     NOT NULL DEFAULT N'Created',
    FailureReason    nvarchar(500)    NULL,
    Gstin            nvarchar(15)     NULL,
    PaidOn           datetimeoffset   NULL,
    SubscriptionId   uniqueidentifier NULL CONSTRAINT FK_PaymentOrders_Subscriptions REFERENCES dbo.BusinessSubscriptions(Id),
    InvoiceNumber    nvarchar(32)     NULL,
    RowVersion       rowversion       NOT NULL,
    CreatedBy        nvarchar(450)    NULL,
    CreatedOn        datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy       nvarchar(450)    NULL,
    ModifiedOn       datetimeoffset   NULL,
    IsDeleted        bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_PaymentOrders_OrderNumber UNIQUE (OrderNumber)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PaymentOrders_GatewayOrder' AND object_id = OBJECT_ID('dbo.PaymentOrders'))
    CREATE UNIQUE INDEX UX_PaymentOrders_GatewayOrder ON dbo.PaymentOrders (GatewayOrderId) WHERE GatewayOrderId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PaymentOrders_Business' AND object_id = OBJECT_ID('dbo.PaymentOrders'))
    CREATE INDEX IX_PaymentOrders_Business ON dbo.PaymentOrders (BusinessId, CreatedOn DESC);
GO

/* Marketing page content (e.g. "List your business"): text blocks, photos, videos and testimonials,
   grouped by page and section so every word and image on marketing pages is database-driven. */
IF OBJECT_ID('dbo.MarketingContent','U') IS NULL
CREATE TABLE dbo.MarketingContent (
    Id              uniqueidentifier NOT NULL CONSTRAINT PK_MarketingContent PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Code            nvarchar(60)     NOT NULL,
    PageKey         nvarchar(40)     NOT NULL,
    SectionKey      nvarchar(40)     NOT NULL,
    Eyebrow         nvarchar(80)     NULL,
    Title           nvarchar(200)    NOT NULL,
    Subtitle        nvarchar(400)    NULL,
    Body            nvarchar(max)    NULL,
    IconKey         nvarchar(40)     NULL,
    ImageUrl        nvarchar(500)    NULL,
    ThumbnailUrl    nvarchar(500)    NULL,
    MobileImageUrl  nvarchar(500)    NULL,
    DesktopImageUrl nvarchar(500)    NULL,
    AltText         nvarchar(300)    NULL,
    VideoUrl        nvarchar(500)    NULL,
    MediaCredit     nvarchar(200)    NULL,
    MediaCreditUrl  nvarchar(500)    NULL,
    CtaText         nvarchar(60)     NULL,
    LinkUrl         nvarchar(300)    NULL,
    BusinessId      uniqueidentifier NULL CONSTRAINT FK_MarketingContent_Businesses REFERENCES dbo.Businesses(Id),
    SortOrder       int              NOT NULL DEFAULT 0,
    IsActive        bit              NOT NULL DEFAULT 1,
    CreatedBy       nvarchar(450)    NULL,
    CreatedOn       datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy      nvarchar(450)    NULL,
    ModifiedOn      datetimeoffset   NULL,
    IsDeleted       bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_MarketingContent_Code UNIQUE (Code)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MarketingContent_Page' AND object_id = OBJECT_ID('dbo.MarketingContent'))
    CREATE INDEX IX_MarketingContent_Page ON dbo.MarketingContent (PageKey, SectionKey, SortOrder) WHERE IsDeleted = 0;
GO

IF OBJECT_ID('dbo.Payments','U') IS NULL
CREATE TABLE dbo.Payments (
    Id            uniqueidentifier NOT NULL CONSTRAINT PK_Payments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    InvoiceNumber nvarchar(24)     NOT NULL,
    BusinessId    uniqueidentifier NOT NULL CONSTRAINT FK_Payments_Businesses REFERENCES dbo.Businesses(Id),
    PaymentType   nvarchar(32)     NOT NULL,
    ReferenceId   uniqueidentifier NULL,
    Amount        decimal(12,2)    NOT NULL,
    TaxAmount     decimal(12,2)    NOT NULL DEFAULT 0,
    TotalAmount   decimal(12,2)    NOT NULL,
    PaymentMode   nvarchar(20)     NOT NULL DEFAULT N'UPI',
    Status        nvarchar(20)     NOT NULL DEFAULT N'Success',
    PaidOn        datetimeoffset   NOT NULL,
    CreatedBy     nvarchar(450)    NULL,
    CreatedOn     datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy    nvarchar(450)    NULL,
    ModifiedOn    datetimeoffset   NULL,
    IsDeleted     bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Payments_Invoice UNIQUE (InvoiceNumber)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_PaidOn' AND object_id = OBJECT_ID('dbo.Payments'))
    CREATE INDEX IX_Payments_PaidOn ON dbo.Payments (PaidOn) INCLUDE (Amount, PaymentType, Status);
GO

/* ---------- 9. Analytics, notifications, audit ---------- */
IF OBJECT_ID('dbo.BusinessDailyStats','U') IS NULL
CREATE TABLE dbo.BusinessDailyStats (
    Id                uniqueidentifier NOT NULL CONSTRAINT PK_BusinessDailyStats PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BusinessId        uniqueidentifier NOT NULL CONSTRAINT FK_BusinessDailyStats_Businesses REFERENCES dbo.Businesses(Id) ON DELETE CASCADE,
    StatDate          date             NOT NULL,
    ProfileViews      int              NOT NULL DEFAULT 0,
    SearchImpressions int              NOT NULL DEFAULT 0,
    CallClicks        int              NOT NULL DEFAULT 0,
    WhatsAppClicks    int              NOT NULL DEFAULT 0,
    DirectionRequests int              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_BusinessDailyStats_Business_Date UNIQUE (BusinessId, StatDate)
);
GO

IF OBJECT_ID('dbo.PlatformDailyStats','U') IS NULL
CREATE TABLE dbo.PlatformDailyStats (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_PlatformDailyStats PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    StatDate    date             NOT NULL,
    ActiveUsers int              NOT NULL DEFAULT 0,
    NewUsers    int              NOT NULL DEFAULT 0,
    Sessions    int              NOT NULL DEFAULT 0,
    Searches    int              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_PlatformDailyStats_Date UNIQUE (StatDate)
);
GO

-- Rolling 30-day distinct users, computed by the analytics pipeline (cannot be derived from daily aggregates).
IF COL_LENGTH('dbo.PlatformDailyStats','MonthlyActiveUsers') IS NULL
    ALTER TABLE dbo.PlatformDailyStats ADD MonthlyActiveUsers int NOT NULL CONSTRAINT DF_PlatformDailyStats_MAU DEFAULT (0);
GO

IF OBJECT_ID('dbo.Notifications','U') IS NULL
CREATE TABLE dbo.Notifications (
    Id               uniqueidentifier NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    UserId           nvarchar(450)    NOT NULL CONSTRAINT FK_Notifications_AspNetUsers REFERENCES dbo.AspNetUsers(Id),
    Title            nvarchar(150)    NOT NULL,
    Message          nvarchar(500)    NOT NULL,
    NotificationType nvarchar(32)     NOT NULL,
    LinkUrl          nvarchar(300)    NULL,
    IsRead           bit              NOT NULL DEFAULT 0,
    CreatedOn        datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Notifications_User' AND object_id = OBJECT_ID('dbo.Notifications'))
    CREATE INDEX IX_Notifications_User ON dbo.Notifications (UserId, IsRead, CreatedOn DESC);
GO

IF OBJECT_ID('dbo.AuditLogs','U') IS NULL
CREATE TABLE dbo.AuditLogs (
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    UserId      nvarchar(450)    NULL,
    Action      nvarchar(20)     NOT NULL,
    EntityName  nvarchar(100)    NOT NULL,
    EntityId    nvarchar(100)    NULL,
    Changes     nvarchar(max)    NULL,
    IpAddress   nvarchar(64)     NULL,
    CreatedOn   datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
GO

/* Curated home-page "Popular services". ServiceName matches BusinessServices.Name inside the sub-category;
   price, provider and booking figures are computed live by the API. */
IF OBJECT_ID('dbo.PopularServices','U') IS NULL
CREATE TABLE dbo.PopularServices (
    Id            uniqueidentifier NOT NULL CONSTRAINT PK_PopularServices PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    SubCategoryId uniqueidentifier NOT NULL CONSTRAINT FK_PopularServices_SubCategories REFERENCES dbo.SubCategories(Id),
    Code          nvarchar(60)     NOT NULL,
    Title         nvarchar(160)    NOT NULL,
    ServiceName   nvarchar(160)    NOT NULL,
    Tagline       nvarchar(200)    NULL,
    BadgeText     nvarchar(30)     NULL,
    ImageUrl      nvarchar(500)    NULL,
    ThumbnailUrl  nvarchar(500)    NULL,
    AltText       nvarchar(300)    NULL,
    SortOrder     int              NOT NULL DEFAULT 0,
    IsActive      bit              NOT NULL DEFAULT 1,
    CreatedBy     nvarchar(450)    NULL,
    CreatedOn     datetimeoffset   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ModifiedBy    nvarchar(450)    NULL,
    ModifiedOn    datetimeoffset   NULL,
    IsDeleted     bit              NOT NULL DEFAULT 0,
    CONSTRAINT UQ_PopularServices_Code UNIQUE (Code)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PopularServices_Sort' AND object_id = OBJECT_ID('dbo.PopularServices'))
    CREATE INDEX IX_PopularServices_Sort ON dbo.PopularServices (SortOrder) INCLUDE (SubCategoryId, IsActive) WHERE IsDeleted = 0;
GO

/* Area discovery (background agent: OpenStreetMap + India Post + local AI normalisation).
   Areas.AreaType   : Area | Locality | Suburb | Town | Village | Neighbourhood (sub-locality)
   Areas.ParentAreaId: sub-localities point at the area they belong to (top-level areas have NULL)
   Areas.AltNames   : alternate / old spellings, '|' separated, used by search
   Areas.Source     : NULL for curated rows, 'osm' for agent-discovered rows; Areas.ExternalRef = OSM element (e.g. node/123) */
IF COL_LENGTH('dbo.Areas', 'AreaType') IS NULL       ALTER TABLE dbo.Areas ADD AreaType nvarchar(24) NULL;
IF COL_LENGTH('dbo.Areas', 'ParentAreaId') IS NULL   ALTER TABLE dbo.Areas ADD ParentAreaId uniqueidentifier NULL CONSTRAINT FK_Areas_ParentArea REFERENCES dbo.Areas(Id);
IF COL_LENGTH('dbo.Areas', 'AltNames') IS NULL       ALTER TABLE dbo.Areas ADD AltNames nvarchar(600) NULL;
IF COL_LENGTH('dbo.Areas', 'Source') IS NULL         ALTER TABLE dbo.Areas ADD Source nvarchar(40) NULL;
IF COL_LENGTH('dbo.Areas', 'ExternalRef') IS NULL    ALTER TABLE dbo.Areas ADD ExternalRef nvarchar(40) NULL;
IF COL_LENGTH('dbo.Areas', 'LastVerifiedOn') IS NULL ALTER TABLE dbo.Areas ADD LastVerifiedOn datetimeoffset NULL;
IF COL_LENGTH('dbo.Cities', 'AreasDiscoveredOn') IS NULL ALTER TABLE dbo.Cities ADD AreasDiscoveredOn datetimeoffset NULL;
IF COL_LENGTH('dbo.Cities', 'AreaDiscoveryNote') IS NULL ALTER TABLE dbo.Cities ADD AreaDiscoveryNote nvarchar(400) NULL;
-- Other names people search with, '|' separated ("Bangalore" for Bengaluru); seeded by 19_CityAltNames.sql.
IF COL_LENGTH('dbo.Cities', 'AltNames') IS NULL          ALTER TABLE dbo.Cities ADD AltNames nvarchar(400) NULL;
GO

/* ---------- Country city catalogue (filled by the city catalogue agent from GeoNames) ----------
   States.CountryCode : ISO 3166-1 alpha-2; existing (curated) states are Indian.
   Cities.Source      : NULL for curated cities, 'geonames' for cities the agent imported.
   Cities.ExternalRef : the GeoNames id ("geonames:1269843"), also set on curated cities the agent matched.
   CountryCatalogs    : one row per country whose cities have been imported (when, how many, notes). */
IF COL_LENGTH('dbo.States', 'CountryCode') IS NULL ALTER TABLE dbo.States ADD CountryCode nvarchar(2) NOT NULL CONSTRAINT DF_States_CountryCode DEFAULT (N'IN');
IF COL_LENGTH('dbo.States', 'ExternalRef') IS NULL ALTER TABLE dbo.States ADD ExternalRef nvarchar(40) NULL;
IF COL_LENGTH('dbo.Cities', 'Source') IS NULL      ALTER TABLE dbo.Cities ADD Source nvarchar(40) NULL;
IF COL_LENGTH('dbo.Cities', 'ExternalRef') IS NULL ALTER TABLE dbo.Cities ADD ExternalRef nvarchar(40) NULL;
IF COL_LENGTH('dbo.Cities', 'Population') IS NULL  ALTER TABLE dbo.Cities ADD Population int NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_States_CountryCode' AND object_id = OBJECT_ID('dbo.States'))
    CREATE INDEX IX_States_CountryCode ON dbo.States (CountryCode) INCLUDE (Name);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cities_ExternalRef' AND object_id = OBJECT_ID('dbo.Cities'))
    CREATE INDEX IX_Cities_ExternalRef ON dbo.Cities (ExternalRef) WHERE ExternalRef IS NOT NULL;
IF OBJECT_ID('dbo.CountryCatalogs', 'U') IS NULL
CREATE TABLE dbo.CountryCatalogs (
    CountryCode nvarchar(2)    NOT NULL CONSTRAINT PK_CountryCatalogs PRIMARY KEY,
    CountryName nvarchar(120)  NOT NULL,
    Source      nvarchar(40)   NOT NULL CONSTRAINT DF_CountryCatalogs_Source DEFAULT (N'geonames'),
    StateCount  int            NOT NULL CONSTRAINT DF_CountryCatalogs_StateCount DEFAULT (0),
    CityCount   int            NOT NULL CONSTRAINT DF_CountryCatalogs_CityCount DEFAULT (0),
    ImportedOn  datetimeoffset NULL,
    Note        nvarchar(400)  NULL,
    CreatedBy   nvarchar(450)  NULL,
    CreatedOn   datetimeoffset NOT NULL CONSTRAINT DF_CountryCatalogs_CreatedOn DEFAULT (SYSDATETIMEOFFSET()),
    ModifiedBy  nvarchar(450)  NULL,
    ModifiedOn  datetimeoffset NULL,
    IsDeleted   bit            NOT NULL CONSTRAINT DF_CountryCatalogs_IsDeleted DEFAULT (0)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Areas_City_Parent' AND object_id = OBJECT_ID('dbo.Areas'))
    CREATE INDEX IX_Areas_City_Parent ON dbo.Areas (CityId, ParentAreaId) INCLUDE (Name, Pincode, IsActive) WHERE IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Areas_ExternalRef' AND object_id = OBJECT_ID('dbo.Areas'))
    CREATE INDEX IX_Areas_ExternalRef ON dbo.Areas (CityId, ExternalRef) WHERE ExternalRef IS NOT NULL;
GO

/* ---------- Mobile OTP sign-in / sign-up ----------
   One row per code sent. Codes and verification tokens are stored as SHA-256 hashes only.
   Purpose: 'SignIn' | 'SignUp'. A verified sign-up code issues a verification token that
   registration must present (VerificationUsedAt is set when an account is created with it). */
IF OBJECT_ID('dbo.OtpCodes', 'U') IS NULL
CREATE TABLE dbo.OtpCodes (
    Id                    uniqueidentifier NOT NULL CONSTRAINT PK_OtpCodes PRIMARY KEY,
    PhoneNumber           nvarchar(20)     NOT NULL,
    Purpose               nvarchar(16)     NOT NULL,
    CodeHash              nvarchar(64)     NOT NULL,
    Attempts              int              NOT NULL CONSTRAINT DF_OtpCodes_Attempts DEFAULT (0),
    ExpiresAt             datetimeoffset   NOT NULL,
    ConsumedAt            datetimeoffset   NULL,
    VerificationTokenHash nvarchar(64)     NULL,
    VerificationExpiresAt datetimeoffset   NULL,
    VerificationUsedAt    datetimeoffset   NULL,
    IpAddress             nvarchar(64)     NULL,
    CreatedAt             datetimeoffset   NOT NULL CONSTRAINT DF_OtpCodes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT CK_OtpCodes_Purpose CHECK (Purpose IN (N'SignIn', N'SignUp'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OtpCodes_PhoneNumber_CreatedAt' AND object_id = OBJECT_ID('dbo.OtpCodes'))
    CREATE INDEX IX_OtpCodes_PhoneNumber_CreatedAt ON dbo.OtpCodes (PhoneNumber, CreatedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OtpCodes_VerificationTokenHash' AND object_id = OBJECT_ID('dbo.OtpCodes'))
    CREATE INDEX IX_OtpCodes_VerificationTokenHash ON dbo.OtpCodes (VerificationTokenHash) WHERE VerificationTokenHash IS NOT NULL;
GO

PRINT '00_Schema.sql completed';
GO
