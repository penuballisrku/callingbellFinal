/* =====================================================================================
   Calling Bell - 13_DashboardDemoData.sql
   Analytics and activity data that power the business and admin dashboards:
     * BusinessDailyStats   - 180 days of profile views, impressions and contact clicks
     * PlatformDailyStats   - 365 days of DAU / new users / sessions / searches
     * Payments             - monthly booking-commission settlements and lead-credit top-ups
     * Favorites            - customers' saved businesses
     * Notifications        - recent lead/booking alerts for owners, approval alerts for admins
     * AuditLogs            - administrative actions
   Idempotent: unique keys / NOT EXISTS guards.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Now datetimeoffset = SYSDATETIMEOFFSET();
DECLARE @Today date = CAST(SWITCHOFFSET(@Now, '+05:30') AS date);

IF OBJECT_ID('tempdb..#D') IS NOT NULL DROP TABLE #D;
SELECT TOP (365) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS d INTO #D FROM sys.all_objects;

/* ---------- Business daily stats (last 180 days) ---------- */
;WITH biz AS (
    SELECT b.Id, b.Slug, CAST(b.CreatedOn AS date) AS Created,
           (18 + ABS(CHECKSUM(b.Slug, N'views')) % 40) * CASE WHEN b.IsFeatured = 1 THEN 2.2 ELSE 1.0 END AS BaseViews
    FROM dbo.Businesses b WHERE b.Status = N'Active'
),
days AS (
    SELECT biz.Id, biz.Slug, DATEADD(DAY, -#D.d, @Today) AS StatDate, #D.d,
           biz.BaseViews
             * (0.65 + 0.35 * (180 - #D.d) / 180.0)                                                        -- growth trend
             * CASE DATEPART(WEEKDAY, DATEADD(DAY, -#D.d, @Today)) WHEN 1 THEN 1.25 WHEN 7 THEN 1.15 ELSE 1.0 END  -- weekend uplift
             * (0.8 + (ABS(CHECKSUM(biz.Slug, #D.d)) % 40) / 100.0) AS Views
    FROM biz JOIN #D ON #D.d < 180 AND DATEADD(DAY, -#D.d, @Today) >= biz.Created
)
INSERT INTO dbo.BusinessDailyStats (BusinessId, StatDate, ProfileViews, SearchImpressions, CallClicks, WhatsAppClicks, DirectionRequests)
SELECT days.Id, days.StatDate,
       CAST(days.Views AS int),
       CAST(days.Views * (4 + ABS(CHECKSUM(days.Slug, days.d, N'imp')) % 3) AS int),
       CAST(days.Views * 0.06 + ABS(CHECKSUM(days.Slug, days.d, N'call')) % 3 AS int),
       CAST(days.Views * 0.04 + ABS(CHECKSUM(days.Slug, days.d, N'wa')) % 2 AS int),
       CAST(days.Views * 0.03 AS int)
FROM days
WHERE NOT EXISTS (SELECT 1 FROM dbo.BusinessDailyStats s WHERE s.BusinessId = days.Id AND s.StatDate = days.StatDate);

/* ---------- Platform daily stats (DAU / MAU source, last 365 days) ---------- */
INSERT INTO dbo.PlatformDailyStats (StatDate, ActiveUsers, NewUsers, Sessions, Searches)
SELECT x.StatDate, x.Dau, x.NewUsers, CAST(x.Dau * 1.62 AS int), CAST(x.Dau * 2.35 AS int)
FROM (
    SELECT DATEADD(DAY, -d, @Today) AS StatDate,
           CAST((1800 + 7700 * POWER((365 - d) / 365.0, 1.3))
                * CASE DATEPART(WEEKDAY, DATEADD(DAY, -d, @Today)) WHEN 1 THEN 1.18 WHEN 7 THEN 1.12 ELSE 1.0 END
                * (0.93 + (ABS(CHECKSUM(d, N'dau')) % 14) / 100.0) AS int) AS Dau,
           CAST((60 + 220 * POWER((365 - d) / 365.0, 1.2)) * (0.85 + (ABS(CHECKSUM(d, N'new')) % 30) / 100.0) AS int) AS NewUsers
    FROM #D
) x
WHERE NOT EXISTS (SELECT 1 FROM dbo.PlatformDailyStats p WHERE p.StatDate = x.StatDate);

-- Rolling 30-day distinct users (MAU): roughly 3.4x the trailing average DAU for a local-services marketplace.
UPDATE p SET MonthlyActiveUsers = CAST(x.AvgDau * 3.4 AS int)
FROM dbo.PlatformDailyStats p
CROSS APPLY (SELECT AVG(CAST(q.ActiveUsers AS decimal(12,2))) AS AvgDau FROM dbo.PlatformDailyStats q
             WHERE q.StatDate > DATEADD(DAY, -30, p.StatDate) AND q.StatDate <= p.StatDate) x
WHERE p.MonthlyActiveUsers = 0;

/* ---------- Monthly booking-commission settlements ---------- */
MERGE dbo.Payments AS t
USING (
    SELECT N'INV-BC-' + CONVERT(nvarchar(6), HASHBYTES('MD5', b.Slug), 2) + N'-' + FORMAT(m.MonthStart, 'yyyyMM') AS InvoiceNumber,
           b.Id AS BusinessId, m.Commission, DATEADD(MONTH, 1, m.MonthStart) AS SettledOn
    FROM (
        SELECT bk.BusinessId, DATEFROMPARTS(YEAR(bk.ScheduledStart), MONTH(bk.ScheduledStart), 1) AS MonthStart, SUM(bk.CommissionAmount) AS Commission
        FROM dbo.Bookings bk
        WHERE bk.Status = N'Completed' AND bk.CommissionAmount > 0
        GROUP BY bk.BusinessId, DATEFROMPARTS(YEAR(bk.ScheduledStart), MONTH(bk.ScheduledStart), 1)
    ) m
    JOIN dbo.Businesses b ON b.Id = m.BusinessId
    WHERE DATEADD(MONTH, 1, m.MonthStart) <= @Today
) AS s
ON t.InvoiceNumber = s.InvoiceNumber
WHEN NOT MATCHED BY TARGET
    THEN INSERT (InvoiceNumber, BusinessId, PaymentType, Amount, TaxAmount, TotalAmount, PaymentMode, Status, PaidOn, CreatedBy, CreatedOn)
         VALUES (s.InvoiceNumber, s.BusinessId, N'BookingCommission', s.Commission, ROUND(s.Commission * 0.18, 2), s.Commission + ROUND(s.Commission * 0.18, 2),
                 N'Settlement', N'Success', TODATETIMEOFFSET(DATEADD(HOUR, 11, CAST(s.SettledOn AS datetime2(0))), '+05:30'), N'seed',
                 TODATETIMEOFFSET(DATEADD(HOUR, 11, CAST(s.SettledOn AS datetime2(0))), '+05:30'));

/* ---------- Lead-credit top-ups ---------- */
MERGE dbo.Payments AS t
USING (
    SELECT N'INV-LC-' + CONVERT(nvarchar(6), HASHBYTES('MD5', b.Slug), 2) + N'-' + RIGHT(N'0' + CAST(n.d + 1 AS nvarchar(3)), 2) AS InvoiceNumber,
           b.Id AS BusinessId,
           CAST(CASE ABS(CHECKSUM(b.Slug, n.d, N'lc')) % 3 WHEN 0 THEN 1500 WHEN 1 THEN 3000 ELSE 5000 END AS decimal(12,2)) AS Amount,
           DATEADD(DAY, -(20 + n.d * 47 + ABS(CHECKSUM(b.Slug, n.d)) % 20), @Now) AS PaidOn
    FROM dbo.Businesses b
    JOIN #D n ON n.d < 1 + ABS(CHECKSUM(b.Slug, N'lccount')) % 6
    WHERE b.Status = N'Active' AND ABS(CHECKSUM(b.Slug, N'buyslc')) % 100 < 35
      AND DATEADD(DAY, -(20 + n.d * 47 + ABS(CHECKSUM(b.Slug, n.d)) % 20), @Now) > b.CreatedOn
) AS s
ON t.InvoiceNumber = s.InvoiceNumber
WHEN NOT MATCHED BY TARGET
    THEN INSERT (InvoiceNumber, BusinessId, PaymentType, Amount, TaxAmount, TotalAmount, PaymentMode, Status, PaidOn, CreatedBy, CreatedOn)
         VALUES (s.InvoiceNumber, s.BusinessId, N'LeadCredits', s.Amount, ROUND(s.Amount * 0.18, 2), s.Amount + ROUND(s.Amount * 0.18, 2),
                 N'UPI', N'Success', s.PaidOn, N'seed', s.PaidOn);

/* ---------- Favourites ---------- */
IF OBJECT_ID('tempdb..#ActiveBiz') IS NOT NULL DROP TABLE #ActiveBiz;
SELECT Id, ROW_NUMBER() OVER (ORDER BY Slug) - 1 AS Idx INTO #ActiveBiz FROM dbo.Businesses WHERE Status = N'Active';
DECLARE @BizCount int = (SELECT COUNT(*) FROM #ActiveBiz);

INSERT INTO dbo.Favorites (UserId, BusinessId, CreatedOn)
SELECT u.Id, ab.Id, MIN(DATEADD(DAY, -(ABS(CHECKSUM(u.Id, n.d)) % 120), @Now))
FROM dbo.AspNetUsers u
JOIN #D n ON n.d < 2 + ABS(CHECKSUM(u.Id, N'fav')) % 7
JOIN #ActiveBiz ab ON ab.Idx = ABS(CHECKSUM(u.Id, n.d, N'pick')) % @BizCount
WHERE u.UserType = N'Customer' AND u.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.Favorites f WHERE f.UserId = u.Id AND f.BusinessId = ab.Id)
GROUP BY u.Id, ab.Id;

/* ---------- Notifications ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Notifications)
BEGIN
    INSERT INTO dbo.Notifications (UserId, Title, Message, NotificationType, LinkUrl, IsRead, CreatedOn)
    SELECT b.OwnerUserId,
           CASE e.EnquiryType WHEN N'Quotation' THEN N'New quotation request' WHEN N'Callback' THEN N'Callback requested' ELSE N'New enquiry' END,
           e.CustomerName + N': ' + LEFT(e.Message, 120),
           N'Lead', N'/business/leads', CASE WHEN e.Status = N'New' THEN 0 ELSE 1 END, e.CreatedOn
    FROM dbo.Enquiries e JOIN dbo.Businesses b ON b.Id = e.BusinessId
    WHERE e.CreatedOn >= DATEADD(DAY, -7, @Now)
    UNION ALL
    SELECT b.OwnerUserId, N'New booking', bk.CustomerName + N' booked ' + s.Name + N' for ' + FORMAT(bk.ScheduledStart, 'dd MMM, h:mm tt'),
           N'Booking', N'/business/bookings', CASE WHEN bk.Status = N'Pending' THEN 0 ELSE 1 END, bk.CreatedOn
    FROM dbo.Bookings bk JOIN dbo.Businesses b ON b.Id = bk.BusinessId JOIN dbo.BusinessServices s ON s.Id = bk.ServiceId
    WHERE bk.CreatedOn >= DATEADD(DAY, -7, @Now)
    UNION ALL
    SELECT a.Id, N'Business awaiting approval', b.Name + N' (' + b.City + N') submitted their listing for review.',
           N'Approval', N'/admin/businesses?status=PendingApproval', 0, b.CreatedOn
    FROM dbo.Businesses b CROSS JOIN dbo.AspNetUsers a
    WHERE b.Status = N'PendingApproval' AND a.UserType = N'Administrator'
    UNION ALL
    SELECT a.Id, N'Review reported', N'A review on ' + b.Name + N' was reported and needs moderation.',
           N'Moderation', N'/admin/reviews?status=Flagged', 0, r.CreatedOn
    FROM dbo.Reviews r JOIN dbo.Businesses b ON b.Id = r.BusinessId CROSS JOIN dbo.AspNetUsers a
    WHERE r.Status = N'Flagged' AND a.UserType = N'Administrator' AND r.CreatedOn >= DATEADD(DAY, -30, @Now);
END

/* ---------- Audit trail of administrative actions ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.AuditLogs)
BEGIN
    DECLARE @Admin nvarchar(450) = (SELECT TOP 1 Id FROM dbo.AspNetUsers WHERE NormalizedEmail = N'ADMIN@DEMO.CALLINGBELL.IN');
    INSERT INTO dbo.AuditLogs (UserId, Action, EntityName, EntityId, Changes, IpAddress, CreatedOn)
    SELECT @Admin, N'Modified', N'Business', CONVERT(nvarchar(36), b.Id),
           N'{"VerificationStatus":{"old":"Pending","new":"Verified"},"Status":{"old":"PendingApproval","new":"Active"}}',
           N'10.20.4.17', b.VerifiedOn
    FROM dbo.Businesses b WHERE b.VerifiedOn IS NOT NULL
    UNION ALL
    SELECT @Admin, N'Modified', N'Business', CONVERT(nvarchar(36), b.Id),
           N'{"Status":{"old":"Active","new":"Suspended"},"Reason":"Repeated customer complaints about overbilling"}',
           N'10.20.4.17', DATEADD(DAY, -18, @Now)
    FROM dbo.Businesses b WHERE b.Status = N'Suspended'
    UNION ALL
    SELECT @Admin, N'Modified', N'Advertisement', CONVERT(nvarchar(36), a.Id),
           N'{"Status":{"old":"PendingApproval","new":"Rejected"},"Reason":"Unsubstantiated claim in ad copy"}',
           N'10.20.4.17', DATEADD(DAY, -2, @Now)
    FROM dbo.Advertisements a WHERE a.Status = N'Rejected';
END

COMMIT TRANSACTION;
PRINT '13_DashboardDemoData.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #D, #ActiveBiz;
GO
