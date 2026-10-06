/* =====================================================================================
   Calling Bell - 12_Subscriptions.sql
   Subscription plans (Free, Silver, Gold, Platinum, Enterprise) with badge artwork,
   each business's subscription history (including upgrades) and subscription invoices.
   Prices are set for small local businesses (annual = 10 x monthly, two months free).
   Idempotent: MERGE on plan Code / SubscriptionNumber / InvoiceNumber; demo (seed) subscriptions and invoices
   follow plan price changes, payments made through the app keep the amount actually paid.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Today date = CAST(SWITCHOFFSET(SYSDATETIMEOFFSET(), '+05:30') AS date);

MERGE dbo.SubscriptionPlans AS t
USING (VALUES
    (N'FREE',       N'Free',       N'Get listed and start receiving enquiries',          0.00,      0.00,    5,  5,   5, 0, 0, N'#667085', 0, 1,
     N'Business profile with contact details' + CHAR(10) + N'Up to 5 services and 5 photos' + CHAR(10) + N'5 lead credits per month' + CHAR(10) + N'Customer reviews and ratings' + CHAR(10) + N'Basic profile analytics'),
    (N'SILVER',     N'Silver',     N'For growing neighbourhood businesses',             199.00,   1990.00,   30, 15,  15, 0, 0, N'#98A2B3', 0, 2,
     N'Everything in Free' + CHAR(10) + N'30 lead credits per month' + CHAR(10) + N'Real-time availability status' + CHAR(10) + N'Online booking calendar' + CHAR(10) + N'Call and WhatsApp buttons' + CHAR(10) + N'Monthly performance report'),
    (N'GOLD',       N'Gold',       N'Stand out in your category',                       499.00,   4990.00,  100, 40,  40, 1, 0, N'#F4A62C', 1, 3,
     N'Everything in Silver' + CHAR(10) + N'100 lead credits per month' + CHAR(10) + N'Priority verification badge' + CHAR(10) + N'Featured on category pages' + CHAR(10) + N'Video consultations' + CHAR(10) + N'Advanced analytics dashboard'),
    (N'PLATINUM',   N'Platinum',   N'Maximum visibility across your city',              999.00,   9990.00,  250, 999, 999, 1, 1, N'#0B1220', 0, 4,
     N'Everything in Gold' + CHAR(10) + N'250 lead credits per month' + CHAR(10) + N'Top placement in search results' + CHAR(10) + N'Quarterly homepage feature' + CHAR(10) + N'Staff management (up to 10)' + CHAR(10) + N'Priority support'),
    (N'ENTERPRISE', N'Enterprise', N'For hospitals, chains and multi-location brands', 2499.00,  24990.00, 1000, 999, 999, 1, 1, N'#021223', 0, 5,
     N'Everything in Platinum' + CHAR(10) + N'Multi-location management' + CHAR(10) + N'Dedicated account manager' + CHAR(10) + N'API access and custom integrations' + CHAR(10) + N'Unlimited staff accounts' + CHAR(10) + N'SLA-backed support')
) AS s (Code, Name, Tagline, MonthlyPrice, AnnualPrice, LeadCredits, MaxServices, MaxImages, Featured, Priority, BadgeColor, IsPopular, SortOrder, Features)
ON t.Code = s.Code
WHEN MATCHED AND (t.Name <> s.Name OR ISNULL(t.Tagline, N'') <> s.Tagline OR t.MonthlyPrice <> s.MonthlyPrice OR t.AnnualPrice <> s.AnnualPrice OR t.Features <> s.Features OR t.IsPopular <> s.IsPopular)
    THEN UPDATE SET Name = s.Name, Tagline = s.Tagline, MonthlyPrice = s.MonthlyPrice, AnnualPrice = s.AnnualPrice, LeadCredits = s.LeadCredits,
                    MaxServices = s.MaxServices, MaxImages = s.MaxImages, IncludesFeaturedListing = s.Featured, IncludesPrioritySupport = s.Priority,
                    BadgeColor = s.BadgeColor, IsPopular = s.IsPopular, SortOrder = s.SortOrder, Features = s.Features,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Code, Name, Tagline, MonthlyPrice, AnnualPrice, LeadCredits, MaxServices, MaxImages, IncludesFeaturedListing, IncludesPrioritySupport,
                 Features, BadgeColor, IsPopular, SortOrder, IsActive, CreatedBy, CreatedOn)
         VALUES (s.Code, s.Name, s.Tagline, s.MonthlyPrice, s.AnnualPrice, s.LeadCredits, s.MaxServices, s.MaxImages, s.Featured, s.Priority,
                 s.Features, s.BadgeColor, s.IsPopular, s.SortOrder, 1, N'seed', DATEADD(DAY, -540, SYSDATETIMEOFFSET()));

/* ---------- Plan badge artwork ---------- */
MERGE dbo.Media AS t
USING (
    SELECT N'SubscriptionPlan' AS EntityType, p.Id AS EntityId, LOWER(p.Code) + N'.svg' AS FileName, p.Name + N' plan badge' AS AltText,
           CAST(CAST(CONCAT(
               N'<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 96 96">',
               N'<rect width="96" height="96" rx="20" fill="', p.BadgeColor, N'" fill-opacity="', CASE WHEN p.Code IN (N'PLATINUM', N'ENTERPRISE') THEN N'1' ELSE N'0.14' END, N'"/>',
               N'<path d="M48 22l7.6 15.4 17 2.5-12.3 12 2.9 16.9L48 60.8l-15.2 8 2.9-16.9-12.3-12 17-2.5z" fill="',
               CASE WHEN p.Code IN (N'PLATINUM', N'ENTERPRISE') THEN N'#F4A62C' ELSE p.BadgeColor END, N'"/>',
               REPLICATE(N'<circle cx="48" cy="80" r="2" fill="#F4A62C"/>', CASE WHEN p.SortOrder >= 4 THEN 1 ELSE 0 END),
               N'</svg>') COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max)) AS varbinary(max)) AS Bytes
    FROM dbo.SubscriptionPlans p
) AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = s.FileName
WHEN MATCHED AND t.FileData <> s.Bytes
    THEN UPDATE SET FileData = s.Bytes, ThumbnailData = s.Bytes, FileSize = DATALENGTH(s.Bytes), ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, s.FileName, N'image/svg+xml', N'.svg', DATALENGTH(s.Bytes), s.Bytes, s.Bytes, s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

UPDATE p SET ImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), m.MediaId))
FROM dbo.SubscriptionPlans p
JOIN dbo.Media m ON m.EntityType = N'SubscriptionPlan' AND m.EntityId = p.Id AND m.FileName = LOWER(p.Code) + N'.svg';

/* ---------- Subscription history ---------- */
IF OBJECT_ID('tempdb..#Plan') IS NOT NULL DROP TABLE #Plan;
;WITH b AS (
    SELECT b.Id, b.Slug, b.Status, b.IsFeatured, b.TeamSize, sc.Slug AS SubSlug,
           CAST(DATEADD(DAY, 2, CAST(b.CreatedOn AS date)) AS date) AS FirstStart,
           ABS(CHECKSUM(b.Slug, N'plan')) % 100 AS Roll,
           CONVERT(nvarchar(6), HASHBYTES('MD5', b.Slug), 2) AS Code
    FROM dbo.Businesses b JOIN dbo.SubCategories sc ON sc.Id = b.SubCategoryId
)
SELECT b.*,
       CASE WHEN b.SubSlug = N'hospitals' THEN N'ENTERPRISE'
            WHEN b.Status = N'PendingApproval' THEN N'SILVER'
            WHEN b.IsFeatured = 1 THEN CASE WHEN b.Roll < 55 THEN N'PLATINUM' ELSE N'GOLD' END
            WHEN b.TeamSize >= 20 THEN CASE WHEN b.Roll < 70 THEN N'GOLD' ELSE N'SILVER' END
            WHEN b.Roll < 30 THEN N'FREE' WHEN b.Roll < 68 THEN N'SILVER' WHEN b.Roll < 92 THEN N'GOLD' ELSE N'PLATINUM' END AS CurrentPlan,
       CASE WHEN b.SubSlug = N'hospitals' OR (b.IsFeatured = 1 AND b.Roll % 2 = 0) THEN N'Annual' ELSE N'Monthly' END AS Cycle
INTO #Plan FROM b;

IF OBJECT_ID('tempdb..#N') IS NOT NULL DROP TABLE #N;
SELECT TOP (24) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i INTO #N FROM sys.all_objects;

IF OBJECT_ID('tempdb..#Period') IS NOT NULL DROP TABLE #Period;
;WITH periods AS (
    -- Paid plans: consecutive monthly or annual periods from onboarding until the period covering today
    SELECT p.Id AS BusinessId, p.Slug, p.Code, p.Status AS BusinessStatus, p.Cycle, n.i,
           CASE WHEN p.Cycle = N'Annual' THEN DATEADD(YEAR, n.i, p.FirstStart) ELSE DATEADD(MONTH, n.i, p.FirstStart) END AS StartDate,
           CASE WHEN p.Cycle = N'Annual' THEN DATEADD(YEAR, n.i + 1, p.FirstStart) ELSE DATEADD(MONTH, n.i + 1, p.FirstStart) END AS EndDate,
           -- Businesses on Platinum/Gold started one tier lower for their first four months (upgrade path)
           CASE WHEN p.Cycle = N'Monthly' AND n.i < 4 AND p.CurrentPlan = N'PLATINUM' THEN N'GOLD'
                WHEN p.Cycle = N'Monthly' AND n.i < 4 AND p.CurrentPlan = N'GOLD' AND p.Roll % 3 = 0 THEN N'SILVER'
                ELSE p.CurrentPlan END AS PlanCode
    FROM #Plan p
    JOIN #N n ON (CASE WHEN p.Cycle = N'Annual' THEN DATEADD(YEAR, n.i, p.FirstStart) ELSE DATEADD(MONTH, n.i, p.FirstStart) END) <= @Today
    WHERE p.CurrentPlan <> N'FREE' AND p.Status <> N'PendingApproval'
)
SELECT pr.*, sp.Id AS PlanId,
       CASE WHEN pr.Cycle = N'Annual' THEN sp.AnnualPrice ELSE sp.MonthlyPrice END AS Amount,
       CASE WHEN pr.BusinessStatus IN (N'Suspended', N'Inactive') AND pr.EndDate >= @Today THEN N'Cancelled'
            WHEN pr.EndDate > @Today THEN N'Active'
            ELSE N'Expired' END AS SubStatus
INTO #Period
FROM periods pr JOIN dbo.SubscriptionPlans sp ON sp.Code = pr.PlanCode;

-- Free listings and 14-day trials for businesses awaiting approval
INSERT INTO #Period (BusinessId, Slug, Code, BusinessStatus, Cycle, i, StartDate, EndDate, PlanCode, PlanId, Amount, SubStatus)
SELECT p.Id, p.Slug, p.Code, p.Status, N'Monthly', 0, p.FirstStart,
       CASE WHEN p.Status = N'PendingApproval' THEN DATEADD(DAY, 14, p.FirstStart) ELSE DATEADD(YEAR, 10, p.FirstStart) END,
       p.CurrentPlan, sp.Id, 0,
       CASE WHEN p.Status = N'PendingApproval' THEN N'Trial' WHEN p.Status = N'Inactive' THEN N'Cancelled' ELSE N'Active' END
FROM #Plan p JOIN dbo.SubscriptionPlans sp ON sp.Code = p.CurrentPlan
WHERE p.CurrentPlan = N'FREE' OR p.Status = N'PendingApproval';

MERGE dbo.BusinessSubscriptions AS t
USING #Period AS s
ON t.SubscriptionNumber = N'SUB-' + s.Code + N'-' + RIGHT(N'0' + CAST(s.i + 1 AS nvarchar(3)), 2)
WHEN MATCHED AND (t.Status <> s.SubStatus OR (t.CreatedBy = N'seed' AND t.Amount <> s.Amount))
    THEN UPDATE SET Status = s.SubStatus, Amount = CASE WHEN t.CreatedBy = N'seed' THEN s.Amount ELSE t.Amount END,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (SubscriptionNumber, BusinessId, PlanId, BillingCycle, StartDate, EndDate, Amount, Status, AutoRenew, CreatedBy, CreatedOn)
         VALUES (N'SUB-' + s.Code + N'-' + RIGHT(N'0' + CAST(s.i + 1 AS nvarchar(3)), 2), s.BusinessId, s.PlanId, s.Cycle, s.StartDate, s.EndDate,
                 s.Amount, s.SubStatus, CASE WHEN s.SubStatus IN (N'Active', N'Expired') AND s.Amount > 0 THEN 1 ELSE 0 END, N'seed',
                 TODATETIMEOFFSET(DATEADD(HOUR, 10, CAST(s.StartDate AS datetime2(0))), '+05:30'));

/* ---------- Subscription invoices (18% GST) ---------- */
MERGE dbo.Payments AS t
USING (
    SELECT N'INV-SB-' + SUBSTRING(bs.SubscriptionNumber, 5, 20) AS InvoiceNumber, bs.BusinessId, bs.Id AS ReferenceId, bs.Amount,
           CAST(ROUND(bs.Amount * 0.18, 2) AS decimal(12,2)) AS Tax,
           CASE ABS(CHECKSUM(bs.SubscriptionNumber)) % 4 WHEN 0 THEN N'Card' WHEN 1 THEN N'NetBanking' ELSE N'UPI' END AS Mode,
           TODATETIMEOFFSET(DATEADD(HOUR, 10, CAST(bs.StartDate AS datetime2(0))), '+05:30') AS PaidOn
    FROM dbo.BusinessSubscriptions bs
    WHERE bs.Amount > 0 AND bs.StartDate <= @Today
) AS s
ON t.InvoiceNumber = s.InvoiceNumber
-- Demo invoices follow their (repriced) subscription.
WHEN MATCHED AND t.CreatedBy = N'seed' AND t.Amount <> s.Amount
    THEN UPDATE SET Amount = s.Amount, TaxAmount = s.Tax, TotalAmount = s.Amount + s.Tax, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (InvoiceNumber, BusinessId, PaymentType, ReferenceId, Amount, TaxAmount, TotalAmount, PaymentMode, Status, PaidOn, CreatedBy, CreatedOn)
         VALUES (s.InvoiceNumber, s.BusinessId, N'Subscription', s.ReferenceId, s.Amount, s.Tax, s.Amount + s.Tax, s.Mode, N'Success', s.PaidOn, N'seed', s.PaidOn);

COMMIT TRANSACTION;
PRINT '12_Subscriptions.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Plan, #N, #Period;
GO
