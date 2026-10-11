/* =====================================================================================
   Calling Bell - 32_PlanComparison.sql
   The plan comparison table on the Pricing page: one value per plan and feature
   (e.g. "Featured placement": Starter = Limited, Growth = Yes). The Price row comes from the
   plans themselves, in the visitor's currency, so it is not stored here.

   * dbo.PlanFeatureValues : FeatureKey groups a row across plans; Label is its name and
     SortOrder its position. Only active (offered) plans are shown.
   * Seed: Starter (SILVER) and Growth (GOLD), plus the Basic listing (FREE, not offered) for reference.
   * Idempotent: MERGE on (PlanId, FeatureKey).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.PlanFeatureValues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlanFeatureValues
    (
        Id          uniqueidentifier NOT NULL CONSTRAINT PK_PlanFeatureValues PRIMARY KEY CONSTRAINT DF_PlanFeatureValues_Id DEFAULT NEWSEQUENTIALID(),
        PlanId      uniqueidentifier NOT NULL CONSTRAINT FK_PlanFeatureValues_SubscriptionPlans REFERENCES dbo.SubscriptionPlans (Id),
        FeatureKey  nvarchar(50)     NOT NULL,
        Label       nvarchar(100)    NOT NULL,
        Value       nvarchar(50)     NOT NULL,
        SortOrder   int              NOT NULL CONSTRAINT DF_PlanFeatureValues_SortOrder DEFAULT 0,
        CreatedBy   nvarchar(450)    NULL,
        CreatedOn   datetimeoffset   NOT NULL CONSTRAINT DF_PlanFeatureValues_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy  nvarchar(450)    NULL,
        ModifiedOn  datetimeoffset   NULL,
        IsDeleted   bit              NOT NULL CONSTRAINT DF_PlanFeatureValues_IsDeleted DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_PlanFeatureValues_Plan_Feature ON dbo.PlanFeatureValues (PlanId, FeatureKey) WHERE IsDeleted = 0;
END
GO

BEGIN TRANSACTION;

MERGE dbo.PlanFeatureValues AS t
USING (
    SELECT p.Id AS PlanId, v.FeatureKey, v.Label, v.Value, v.SortOrder
    FROM (VALUES
        -- FeatureKey,             Label,                          SortOrder, Basic (FREE), Starter (SILVER), Growth (GOLD)
        (N'business-profile',     N'Business profile',            1, N'Yes',   N'Yes',      N'Yes'),
        (N'profile-url',          N'Shareable profile URL',       2, N'Yes',   N'Yes',      N'Yes'),
        (N'photos-services',      N'Photos and services',         3, N'Basic', N'Enhanced', N'Enhanced'),
        (N'featured-placement',   N'Featured placement',          4, N'No',    N'Limited',  N'Yes'),
        (N'lead-notifications',   N'Lead notifications',          5, N'Basic', N'Yes',      N'Yes'),
        (N'booking-offers',       N'Booking and offers',          6, N'Basic', N'Yes',      N'Yes'),
        (N'performance-report',   N'Monthly performance report',  7, N'No',    N'Basic',    N'Yes')
    ) AS f (FeatureKey, Label, SortOrder, BasicValue, StarterValue, GrowthValue)
    CROSS APPLY (VALUES (N'FREE', f.BasicValue), (N'SILVER', f.StarterValue), (N'GOLD', f.GrowthValue)) AS v0 (Code, Value)
    CROSS APPLY (SELECT f.FeatureKey, f.Label, v0.Value, f.SortOrder) AS v
    JOIN dbo.SubscriptionPlans p ON p.Code = v0.Code
) AS s
ON t.PlanId = s.PlanId AND t.FeatureKey = s.FeatureKey AND t.IsDeleted = 0
WHEN MATCHED AND (t.Label <> s.Label OR t.Value <> s.Value OR t.SortOrder <> s.SortOrder)
    THEN UPDATE SET Label = s.Label, Value = s.Value, SortOrder = s.SortOrder, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (PlanId, FeatureKey, Label, Value, SortOrder, CreatedBy) VALUES (s.PlanId, s.FeatureKey, s.Label, s.Value, s.SortOrder, N'seed');

COMMIT TRANSACTION;
GO
