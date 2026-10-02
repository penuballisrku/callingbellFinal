/* =====================================================================================
   Calling Bell - 10_Bookings.sql
   Service bookings (past and upcoming) for businesses that accept online booking.
   Times are within working hours (IST), statuses follow the booking date, and platform
   commission (8%) is recorded on completed paid bookings.
   Deterministic and idempotent on BookingNumber.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Now datetimeoffset = SWITCHOFFSET(SYSDATETIMEOFFSET(), '+05:30');
DECLARE @TodayIst date = CAST(@Now AS date);

IF OBJECT_ID('tempdb..#Cust') IS NOT NULL DROP TABLE #Cust;
SELECT Id, DisplayName, PhoneNumber, ROW_NUMBER() OVER (ORDER BY NormalizedEmail) - 1 AS Idx
INTO #Cust FROM dbo.AspNetUsers WHERE UserType = N'Customer' AND IsActive = 1;
DECLARE @CustCount int = (SELECT COUNT(*) FROM #Cust);

IF OBJECT_ID('tempdb..#Svc') IS NOT NULL DROP TABLE #Svc;
SELECT Id, BusinessId, Name, Price, DurationMinutes, Type,
       ROW_NUMBER() OVER (PARTITION BY BusinessId ORDER BY IsPopular DESC, Price) - 1 AS Idx,
       COUNT(*) OVER (PARTITION BY BusinessId) AS Cnt
INTO #Svc FROM dbo.BusinessServices WHERE IsActive = 1;

DECLARE @Society TABLE (Idx int PRIMARY KEY, Name nvarchar(100));
INSERT INTO @Society VALUES (0, N'Green Meadows Apartments'), (1, N'Lake View Residency'), (2, N'Sai Krupa Enclave'), (3, N'Sunshine Heights'),
                            (4, N'Silver Oak Residency'), (5, N'Palm Grove Apartments'), (6, N'Lotus Enclave'), (7, N'Shanti Nilayam');
DECLARE @Note TABLE (Idx int PRIMARY KEY, Note nvarchar(200));
INSERT INTO @Note VALUES (0, N'Please call 10 minutes before arriving.'), (1, N'Gate pass will be shared on WhatsApp.'),
                         (2, N'Visitor parking is available in the basement.'), (3, N'Elderly parent at home - please be on time.'),
                         (4, N'Second visit for the same issue.'), (5, NULL), (6, NULL), (7, NULL);
DECLARE @Cancel TABLE (Idx int PRIMARY KEY, Reason nvarchar(200));
INSERT INTO @Cancel VALUES (0, N'Change of plans'), (1, N'Booked another provider'), (2, N'Rescheduled to a later date'), (3, N'Issue resolved on its own');

IF OBJECT_ID('tempdb..#N') IS NOT NULL DROP TABLE #N;
SELECT TOP (120) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k INTO #N FROM sys.all_objects;

;WITH biz AS (
    SELECT b.Id, b.Slug, b.Area, b.City, b.CreatedOn AS BusinessCreatedOn,
           DATEDIFF(DAY, b.CreatedOn, @Now) AS AgeDays,
           CONVERT(nvarchar(6), HASHBYTES('MD5', b.Slug), 2) AS Code,
           CAST((15 + ABS(CHECKSUM(b.Slug, N'bookings')) % 45) * CASE WHEN b.IsFeatured = 1 THEN 1.5 ELSE 1 END AS int) AS Target
    FROM dbo.Businesses b
    WHERE b.Status = N'Active' AND b.AcceptsOnlineBooking = 1 AND DATEDIFF(DAY, b.CreatedOn, @Now) > 7
),
d AS (
    SELECT biz.*, n.k,
           ABS(CHECKSUM(biz.Slug, n.k, N'x')) AS R1,
           ABS(CHECKSUM(biz.Slug, n.k, N'y')) AS R2,
           ABS(CHECKSUM(biz.Slug, n.k, N'z')) AS R3,
           CASE WHEN ABS(CHECKSUM(biz.Slug, n.k, N'f')) % 100 < 12
                THEN -(ABS(CHECKSUM(biz.Slug, n.k, N'g')) % 15)                                                   -- upcoming (0-14 days ahead)
                ELSE 1 + CAST(FLOOR((biz.AgeDays - 5) * POWER((ABS(CHECKSUM(biz.Slug, n.k, N'h')) % 1000) / 1000.0, 1.4)) AS int)
           END AS DaysAgo
    FROM biz JOIN #N n ON n.k < biz.Target
),
s1 AS (
    SELECT d.*, sv.Id AS ServiceId, sv.Price, sv.Type AS ServiceType,
           CASE WHEN sv.DurationMinutes > 240 THEN 240 ELSE sv.DurationMinutes END AS Duration,
           TODATETIMEOFFSET(DATEADD(MINUTE, (9 + d.R1 % 10) * 60 + (d.R2 % 2) * 30, CAST(DATEADD(DAY, -d.DaysAgo, @TodayIst) AS datetime2(0))), '+05:30') AS StartAt
    FROM d JOIN #Svc sv ON sv.BusinessId = d.Id AND sv.Idx = d.R3 % CASE WHEN sv.Cnt > 3 THEN 3 ELSE sv.Cnt END
),
s2 AS (
    SELECT s1.*,
           CASE WHEN s1.StartAt > @Now THEN CASE WHEN s1.R3 % 10 < 4 THEN N'Pending' ELSE N'Confirmed' END
                WHEN s1.DaysAgo <= 1 THEN CASE WHEN s1.R3 % 10 < 7 THEN N'Completed' ELSE N'Confirmed' END
                ELSE CASE WHEN s1.R3 % 100 < 80 THEN N'Completed' WHEN s1.R3 % 100 < 94 THEN N'Cancelled' ELSE N'NoShow' END
           END AS Status
    FROM s1
)
INSERT INTO dbo.Bookings (BookingNumber, BusinessId, ServiceId, CustomerUserId, CustomerName, CustomerPhone, ScheduledStart, ScheduledEnd,
                          Status, Amount, CommissionAmount, PaymentStatus, ServiceAddress, Notes, CancellationReason, CreatedBy, CreatedOn)
SELECT N'BK-' + s2.Code + N'-' + RIGHT(N'000' + CAST(s2.k + 1 AS nvarchar(4)), 3),
       s2.Id, s2.ServiceId, cu.Id, cu.DisplayName, cu.PhoneNumber,
       s2.StartAt, DATEADD(MINUTE, s2.Duration, s2.StartAt),
       s2.Status,
       s2.Price,
       CASE WHEN s2.Status = N'Completed' THEN ROUND(s2.Price * 0.08, 2) ELSE 0 END,
       CASE WHEN s2.Price = 0 THEN N'NotApplicable'
            WHEN s2.Status = N'Completed' THEN N'Paid'
            WHEN s2.Status = N'Cancelled' THEN N'Refunded'
            WHEN s2.Status = N'Confirmed' AND s2.R1 % 10 < 4 THEN N'Paid'
            ELSE N'Pending' END,
       CASE WHEN s2.ServiceType = N'AtHome'
            THEN N'Flat ' + CAST(101 + s2.R1 % 800 AS nvarchar(5)) + N', ' + so.Name + N', ' + s2.Area + N', ' + s2.City END,
       nt.Note,
       CASE WHEN s2.Status = N'Cancelled' THEN ca.Reason END,
       cu.Id,
       CASE WHEN s2.StartAt > @Now THEN DATEADD(HOUR, -(1 + s2.R2 % 60), @Now)
            ELSE DATEADD(HOUR, -(4 + s2.R2 % 120), s2.StartAt) END
FROM s2
JOIN #Cust cu ON cu.Idx = (ABS(CHECKSUM(s2.Slug, N'c')) + s2.k * 41) % @CustCount
JOIN @Society so ON so.Idx = s2.R2 % 8
JOIN @Note nt ON nt.Idx = s2.R1 % 8
JOIN @Cancel ca ON ca.Idx = s2.R2 % 4
WHERE NOT EXISTS (SELECT 1 FROM dbo.Bookings x WHERE x.BookingNumber = N'BK-' + s2.Code + N'-' + RIGHT(N'000' + CAST(s2.k + 1 AS nvarchar(4)), 3));

COMMIT TRANSACTION;
PRINT '10_Bookings.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Cust, #Svc, #N;
GO
