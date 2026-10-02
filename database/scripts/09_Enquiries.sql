/* =====================================================================================
   Calling Bell - 09_Enquiries.sql
   Leads: general enquiries, quotation requests and callback requests.
   Volume grows towards the present to reflect platform growth; status depends on age.
   Deterministic and idempotent: EnquiryNumber is derived from the business slug + sequence.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Now datetimeoffset = SYSDATETIMEOFFSET();

IF OBJECT_ID('tempdb..#M') IS NOT NULL DROP TABLE #M;
CREATE TABLE #M (CatSlug nvarchar(140), Idx int, Template nvarchar(500));
INSERT INTO #M VALUES
(N'home-services', 0, N'Hi, I need {service} at my flat in {area}. Is someone available this weekend?'),
(N'home-services', 1, N'Please share the charges for {service}. It is a 3 BHK apartment.'),
(N'home-services', 2, N'Can you send a quotation for {service}? We are a 40-flat society in {area}.'),
(N'home-services', 3, N'Looking for {service} urgently this evening. Please call back.'),
(N'healthcare', 0, N'Is the doctor available for {service} tomorrow morning?'),
(N'healthcare', 1, N'Please share the cost of {service} and whether insurance is accepted.'),
(N'healthcare', 2, N'I would like to book {service} for my father (68 years). Do you offer home visits in {area}?'),
(N'healthcare', 3, N'Need a callback regarding {service} for my daughter.'),
(N'education', 0, N'Looking for {service} for my son in Class 8 (CBSE). What are the timings?'),
(N'education', 1, N'Please share fee details for {service} and the next batch start date.'),
(N'education', 2, N'Do you offer a trial for {service}? We stay near {area}.'),
(N'education', 3, N'Need a callback to discuss {service} for Class 11 PCM.'),
(N'beauty-wellness', 0, N'Do you have a slot for {service} this Saturday?'),
(N'beauty-wellness', 1, N'Need {service} for 4 people for a family function. Please share a package price.'),
(N'beauty-wellness', 2, N'Is {service} available at home in {area}?'),
(N'beauty-wellness', 3, N'Please call me regarding {service}.'),
(N'legal-services', 0, N'I need advice on {service} regarding an ancestral property dispute.'),
(N'legal-services', 1, N'Please share your fees for {service}. We are a small business with 12 employees.'),
(N'legal-services', 2, N'Can I get a video consultation for {service} this week?'),
(N'legal-services', 3, N'Need a callback about {service}.'),
(N'architecture-interiors', 0, N'We have a new 3 BHK in {area}. Interested in {service}.'),
(N'architecture-interiors', 1, N'Looking for {service} for a 2,400 sq ft villa. Please share an estimate.'),
(N'architecture-interiors', 2, N'Can you share your portfolio and a quotation for {service}?'),
(N'architecture-interiors', 3, N'Please call to discuss {service}.'),
(N'food-dining', 0, N'Want to book {service} for 25 guests on Saturday evening.'),
(N'food-dining', 1, N'Do you cater outside? Need {service} for 200 guests.'),
(N'food-dining', 2, N'Please share the menu and pricing for {service}.'),
(N'food-dining', 3, N'Please call back about {service} for an office party.'),
(N'events-weddings', 0, N'Planning a wedding in December for 400 guests. Please share {service} details.'),
(N'events-weddings', 1, N'Need {service} for my parents'' 25th anniversary. Please share a quote.'),
(N'events-weddings', 2, N'Can you send a quotation for {service}? The venue is in {area}.'),
(N'events-weddings', 3, N'Please call to discuss {service}.'),
(N'real-estate', 0, N'Looking for a 2 BHK on rent near {area}, budget around 35,000. Interested in {service}.'),
(N'real-estate', 1, N'Want {service} for my flat before selling. Please share charges.'),
(N'real-estate', 2, N'NRI owner here, interested in {service}.'),
(N'real-estate', 3, N'Please call back regarding {service}.'),
(N'travel-transport', 0, N'Need {service} on the 14th at 5 am for 3 passengers.'),
(N'travel-transport', 1, N'Please quote for {service} for a family of five.'),
(N'travel-transport', 2, N'Moving out of {area} next month. Need a quote for {service}.'),
(N'travel-transport', 3, N'Please call back about {service}.'),
(N'automotive', 0, N'My car needs {service}. Can I drop it tomorrow morning?'),
(N'automotive', 1, N'What is the cost of {service} for a Hyundai Creta?'),
(N'automotive', 2, N'Need {service} near {area} as soon as possible.'),
(N'automotive', 3, N'Please call back regarding {service}.');

IF OBJECT_ID('tempdb..#Cust') IS NOT NULL DROP TABLE #Cust;
SELECT Id, DisplayName, Email, PhoneNumber, ROW_NUMBER() OVER (ORDER BY NormalizedEmail) - 1 AS Idx
INTO #Cust FROM dbo.AspNetUsers WHERE UserType = N'Customer' AND IsActive = 1;
DECLARE @CustCount int = (SELECT COUNT(*) FROM #Cust);

IF OBJECT_ID('tempdb..#Svc') IS NOT NULL DROP TABLE #Svc;
SELECT Id, BusinessId, Name, Price, ROW_NUMBER() OVER (PARTITION BY BusinessId ORDER BY IsPopular DESC, Name) - 1 AS Idx,
       COUNT(*) OVER (PARTITION BY BusinessId) AS Cnt
INTO #Svc FROM dbo.BusinessServices WHERE IsActive = 1;

IF OBJECT_ID('tempdb..#N') IS NOT NULL DROP TABLE #N;
SELECT TOP (120) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k INTO #N FROM sys.all_objects;

;WITH biz AS (
    SELECT b.Id, b.Slug, b.Area, b.CreatedOn AS BusinessCreatedOn, ISNULL(b.ResponseTimeMinutes, 30) AS ResponseTime, c.Slug AS CatSlug,
           DATEDIFF(DAY, b.CreatedOn, @Now) AS AgeDays,
           CONVERT(nvarchar(6), HASHBYTES('MD5', b.Slug), 2) AS Code,
           (12 + ABS(CHECKSUM(b.Slug, N'leads')) % 34) * CASE WHEN b.IsFeatured = 1 THEN 2 ELSE 1 END AS Target
    FROM dbo.Businesses b
    JOIN dbo.Categories c ON c.Id = b.CategoryId
    WHERE b.Status = N'Active' AND DATEDIFF(DAY, b.CreatedOn, @Now) > 3
),
d AS (
    SELECT biz.*, n.k,
           ABS(CHECKSUM(biz.Slug, n.k, N'a')) AS R1,
           ABS(CHECKSUM(biz.Slug, n.k, N'b')) AS R2,
           ABS(CHECKSUM(biz.Slug, n.k, N'c')) AS R3,
           -- skew towards recent dates: age * u^1.6
           CAST(FLOOR((biz.AgeDays - 1) * POWER((ABS(CHECKSUM(biz.Slug, n.k, N'd')) % 1000) / 1000.0, 1.6)) AS int) AS DaysAgo
    FROM biz JOIN #N n ON n.k < biz.Target
),
e AS (
    SELECT d.*,
           CASE WHEN d.R1 % 100 < 50 THEN N'Enquiry' WHEN d.R1 % 100 < 80 THEN N'Quotation' ELSE N'Callback' END AS EnquiryType,
           DATEADD(MINUTE, -(d.R2 % 720), DATEADD(DAY, -d.DaysAgo, @Now)) AS CreatedOn
    FROM d
),
f AS (
    SELECT e.*,
           CASE WHEN e.DaysAgo < 2 THEN N'New'
                WHEN e.DaysAgo < 7 THEN CASE WHEN e.R3 % 10 < 4 THEN N'New' ELSE N'Contacted' END
                ELSE CASE WHEN e.R3 % 100 < 20 THEN N'Contacted'
                          WHEN e.R3 % 100 < 40 THEN N'Quoted'
                          WHEN e.R3 % 100 < 75 THEN N'Converted'
                          ELSE N'Lost' END END AS Status,
           CASE e.EnquiryType WHEN N'Callback' THEN 3 WHEN N'Quotation' THEN 1 + e.R2 % 2 ELSE (e.R2 % 2) * 2 END AS MsgIdx
    FROM e
)
INSERT INTO dbo.Enquiries (EnquiryNumber, BusinessId, ServiceId, CustomerUserId, CustomerName, CustomerPhone, CustomerEmail,
                           EnquiryType, Message, PreferredDate, Budget, QuotedAmount, Status, Source, RespondedOn, CreatedBy, CreatedOn)
SELECT N'LD-' + f.Code + N'-' + RIGHT(N'000' + CAST(f.k + 1 AS nvarchar(4)), 3),
       f.Id, s.Id,
       CASE WHEN f.R1 % 10 < 7 THEN cu.Id END,
       cu.DisplayName, cu.PhoneNumber,
       CASE WHEN f.R1 % 10 < 7 THEN cu.Email END,
       f.EnquiryType,
       REPLACE(REPLACE(m.Template, N'{service}', s.Name), N'{area}', f.Area),
       CASE WHEN f.R2 % 3 = 0 THEN CAST(DATEADD(DAY, 1 + f.R3 % 10, f.CreatedOn) AS date) END,
       CASE WHEN f.EnquiryType = N'Quotation' AND s.Price > 0 AND f.R3 % 2 = 0 THEN ROUND(s.Price * (1 + f.R2 % 3) / 100, 0) * 100 END,
       CASE WHEN f.Status IN (N'Quoted', N'Converted') AND f.EnquiryType = N'Quotation' AND s.Price > 0 THEN ROUND(s.Price * (1 + f.R3 % 3) * 0.95 / 10, 0) * 10 END,
       f.Status,
       CASE WHEN f.R2 % 100 < 45 THEN N'Search' WHEN f.R2 % 100 < 75 THEN N'Profile' WHEN f.R2 % 100 < 90 THEN N'Advertisement' ELSE N'Direct' END,
       CASE WHEN f.Status <> N'New' THEN DATEADD(MINUTE, f.ResponseTime + f.R3 % 90, f.CreatedOn) END,
       CASE WHEN f.R1 % 10 < 7 THEN cu.Id END,
       f.CreatedOn
FROM f
JOIN #Cust cu ON cu.Idx = (ABS(CHECKSUM(f.Slug)) + f.k * 53) % @CustCount
JOIN #Svc s ON s.BusinessId = f.Id AND s.Idx = f.R3 % s.Cnt
JOIN #M m ON m.CatSlug = f.CatSlug AND m.Idx = f.MsgIdx
WHERE NOT EXISTS (SELECT 1 FROM dbo.Enquiries x WHERE x.EnquiryNumber = N'LD-' + f.Code + N'-' + RIGHT(N'000' + CAST(f.k + 1 AS nvarchar(4)), 3));

COMMIT TRANSACTION;
PRINT '09_Enquiries.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #M, #Cust, #Svc, #N;
GO
