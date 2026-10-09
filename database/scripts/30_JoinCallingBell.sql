/* =====================================================================================
   Calling Bell - 30_JoinCallingBell.sql
   "Join Calling Bell": a business found on Google Maps or OpenStreetMap signs up with its
   details imported from that source.

   * Businesses.SourceProvider / SourceExternalId: the place the listing was created from
     ("google" + Google place id, or "osm" + "node/123"). Only the identifier is stored:
     Google's terms allow keeping place ids, not the place's content. A unique index stops
     the same place from being registered twice.
   * IX_Businesses_PhoneNumber: duplicate checks by phone number at sign-up.
   * Idempotent: safe to run repeatedly.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'dbo.Businesses', N'SourceProvider') IS NULL   ALTER TABLE dbo.Businesses ADD SourceProvider nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.Businesses', N'SourceExternalId') IS NULL ALTER TABLE dbo.Businesses ADD SourceExternalId nvarchar(300) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Businesses_Source' AND object_id = OBJECT_ID(N'dbo.Businesses'))
    CREATE UNIQUE INDEX UX_Businesses_Source ON dbo.Businesses (SourceProvider, SourceExternalId)
        WHERE SourceExternalId IS NOT NULL AND IsDeleted = 0;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Businesses_PhoneNumber' AND object_id = OBJECT_ID(N'dbo.Businesses'))
    CREATE INDEX IX_Businesses_PhoneNumber ON dbo.Businesses (PhoneNumber) INCLUDE (Name, Slug, Status, CityId) WHERE IsDeleted = 0;
GO

PRINT 'Join Calling Bell: Businesses.SourceProvider / SourceExternalId ready.';
GO

/* ---------- Claim requests: "this business is mine" for a listing that already exists ----------
   Reviewed by an administrator, who verifies the person and transfers the listing. */
IF OBJECT_ID(N'dbo.BusinessClaimRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BusinessClaimRequests
    (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_BusinessClaimRequests PRIMARY KEY CONSTRAINT DF_BusinessClaimRequests_Id DEFAULT NEWSEQUENTIALID(),
        RequestNumber    nvarchar(30)     NOT NULL,
        BusinessId       uniqueidentifier NOT NULL CONSTRAINT FK_BusinessClaimRequests_Businesses REFERENCES dbo.Businesses (Id),
        UserId           nvarchar(450)    NULL CONSTRAINT FK_BusinessClaimRequests_AspNetUsers REFERENCES dbo.AspNetUsers (Id),
        ClaimantName     nvarchar(120)    NOT NULL,
        ClaimantPhone    nvarchar(20)     NOT NULL,
        ClaimantEmail    nvarchar(256)    NULL,
        Message          nvarchar(1000)   NULL,
        SourceProvider   nvarchar(20)     NULL,
        SourceExternalId nvarchar(300)    NULL,
        Status           nvarchar(16)     NOT NULL CONSTRAINT DF_BusinessClaimRequests_Status DEFAULT N'Pending',
        IpAddress        nvarchar(64)     NULL,
        CreatedBy        nvarchar(450)    NULL,
        CreatedOn        datetimeoffset   NOT NULL CONSTRAINT DF_BusinessClaimRequests_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy       nvarchar(450)    NULL,
        ModifiedOn       datetimeoffset   NULL,
        IsDeleted        bit              NOT NULL CONSTRAINT DF_BusinessClaimRequests_IsDeleted DEFAULT 0,
        CONSTRAINT UQ_BusinessClaimRequests_Number UNIQUE (RequestNumber)
    );
    CREATE INDEX IX_BusinessClaimRequests_Business ON dbo.BusinessClaimRequests (BusinessId, Status) WHERE IsDeleted = 0;
    CREATE INDEX IX_BusinessClaimRequests_Status ON dbo.BusinessClaimRequests (Status, CreatedOn DESC) WHERE IsDeleted = 0;
END
GO
