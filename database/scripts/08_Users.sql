/* =====================================================================================
   Calling Bell - 08_Users.sql
   Platform administrators and customer accounts.
   (Business-owner accounts are created by 04_Businesses.sql together with their listings.)

   NOTE: execute before 07_Reviews.sql - reviews, enquiries and bookings reference customers.
         RunAll.sql runs the scripts in the correct dependency order.

   All demo accounts share the password:  CallingBell@2026
   All e-mail addresses use the non-routable demo.callingbell.in domain.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @PasswordHash nvarchar(max) = N'AQAAAAIAAYagAAAAEDDVMKoMpeUr6i+eSo4QvV1QWOKIXPq1NU3Vg7tUwtUfZEweayOOe9GLHR3gEPK9ew==';

IF OBJECT_ID('tempdb..#U') IS NOT NULL DROP TABLE #U;
CREATE TABLE #U (
    Email nvarchar(256) PRIMARY KEY, FullName nvarchar(150), Phone nvarchar(20), CitySlug nvarchar(120),
    UserType nvarchar(32), RoleName nvarchar(64), DaysAgo int, IsActive bit, LastLoginDaysAgo int NULL);

/* ---------- Administrators ---------- */
INSERT INTO #U VALUES
(N'admin@demo.callingbell.in', N'Kiran Rao',   N'+91 90000 10001', N'hyderabad', N'Administrator', N'Administrator', 540, 1, 0),
(N'ops@demo.callingbell.in',   N'Ayesha Khan', N'+91 90000 10002', N'bengaluru', N'Administrator', N'Administrator', 480, 1, 1);

/* ---------- Customers (150, deterministic name/city combinations) ---------- */
DECLARE @First TABLE (Idx int PRIMARY KEY, Name nvarchar(40));
INSERT INTO @First VALUES
(0,N'Aarav'),(1,N'Priya'),(2,N'Rohan'),(3,N'Ananya'),(4,N'Vivaan'),(5,N'Diya'),(6,N'Aditya'),(7,N'Ishita'),(8,N'Arjun'),(9,N'Kavya'),
(10,N'Siddharth'),(11,N'Meera'),(12,N'Karthik'),(13,N'Sneha'),(14,N'Rahul'),(15,N'Pooja'),(16,N'Nikhil'),(17,N'Aishwarya'),(18,N'Varun'),(19,N'Neha'),
(20,N'Harsha'),(21,N'Divya'),(22,N'Akash'),(23,N'Shreya'),(24,N'Manish'),(25,N'Lakshmi'),(26,N'Pranav'),(27,N'Ritika'),(28,N'Sanjay'),(29,N'Bhavana'),
(30,N'Abhishek'),(31,N'Nandini'),(32,N'Vikas'),(33,N'Swati'),(34,N'Tarun'),(35,N'Keerthi'),(36,N'Gaurav'),(37,N'Anjali'),(38,N'Deepak'),(39,N'Sowmya'),
(40,N'Rajesh'),(41,N'Tanvi'),(42,N'Suresh'),(43,N'Pallavi'),(44,N'Amit'),(45,N'Revathi'),(46,N'Kunal'),(47,N'Madhuri'),(48,N'Naveen'),(49,N'Sahana'),
(50,N'Yash'),(51,N'Farhan'),(52,N'Zoya'),(53,N'Joseph'),(54,N'Sana'),(55,N'Imran'),(56,N'Gurpreet'),(57,N'Debjani'),(58,N'Anirban'),(59,N'Jyoti');

DECLARE @Last TABLE (Idx int PRIMARY KEY, Name nvarchar(40));
INSERT INTO @Last VALUES
(0,N'Sharma'),(1,N'Reddy'),(2,N'Iyer'),(3,N'Patel'),(4,N'Nair'),(5,N'Gupta'),(6,N'Rao'),(7,N'Menon'),(8,N'Kulkarni'),(9,N'Singh'),
(10,N'Desai'),(11,N'Chatterjee'),(12,N'Joshi'),(13,N'Pillai'),(14,N'Agarwal'),(15,N'Verma'),(16,N'Naidu'),(17,N'Banerjee'),(18,N'Shetty'),(19,N'Mehta'),
(20,N'Krishnan'),(21,N'Bose'),(22,N'Kapoor'),(23,N'Hegde'),(24,N'Mishra'),(25,N'Pandey'),(26,N'Chowdary'),(27,N'Sinha'),(28,N'Saxena'),(29,N'Varma'),
(30,N'Bhat'),(31,N'Malhotra'),(32,N'Rajan'),(33,N'Kumar'),(34,N'Dutta'),(35,N'Shah'),(36,N'Jain'),(37,N'Prasad'),(38,N'Thomas'),(39,N'Fernandes'),
(40,N'Khan'),(41,N'Ahmed'),(42,N'Mukherjee'),(43,N'Goswami'),(44,N'Yadav'),(45,N'Trivedi'),(46,N'Srinivasan'),(47,N'Raghavan'),(48,N'Murthy'),(49,N'Das');

DECLARE @CityOrder TABLE (Idx int PRIMARY KEY, Slug nvarchar(120));
INSERT INTO @CityOrder VALUES (0,N'hyderabad'),(1,N'bengaluru'),(2,N'mumbai'),(3,N'pune'),(4,N'chennai'),(5,N'delhi'),
                              (6,N'hyderabad'),(7,N'bengaluru'),(8,N'kolkata'),(9,N'ahmedabad'),(10,N'jaipur'),(11,N'kochi');

;WITH n AS (SELECT TOP (150) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i FROM sys.all_objects)
INSERT INTO #U (Email, FullName, Phone, CitySlug, UserType, RoleName, DaysAgo, IsActive, LastLoginDaysAgo)
SELECT LOWER(f.Name + N'.' + l.Name) + N'@demo.callingbell.in',
       f.Name + N' ' + l.Name,
       N'+91 ' + STUFF(CAST(9000000000 + ABS(CHECKSUM(f.Name + l.Name)) % 999999999 AS nvarchar(10)), 6, 0, N' '),
       c.Slug,
       N'Customer', N'Customer',
       15 + (n.i * 13) % 420,
       CASE WHEN n.i % 37 = 36 THEN 0 ELSE 1 END,
       (n.i * 7) % 45
FROM n
JOIN @First f ON f.Idx = n.i % 60
JOIN @Last  l ON l.Idx = (n.i * 7 + n.i / 60) % 50
JOIN @CityOrder c ON c.Idx = n.i % 12;

/* ---------- Upsert users ---------- */
MERGE dbo.AspNetUsers AS t
USING (SELECT u.*, c.Id AS CityId FROM #U u LEFT JOIN dbo.Cities c ON c.Slug = u.CitySlug) AS s
ON t.NormalizedEmail = UPPER(s.Email)
WHEN MATCHED AND (t.DisplayName <> s.FullName OR t.UserType <> s.UserType OR t.IsActive <> s.IsActive OR ISNULL(t.PhoneNumber, N'') <> s.Phone)
    THEN UPDATE SET DisplayName = s.FullName, UserType = s.UserType, IsActive = s.IsActive, PhoneNumber = s.Phone, CityId = s.CityId,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
                 PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount,
                 DisplayName, UserType, CityId, IsActive, LastLoginOn, CreatedBy, CreatedOn, IsDeleted)
         VALUES (LOWER(CONVERT(nvarchar(36), NEWID())), s.Email, UPPER(s.Email), s.Email, UPPER(s.Email), 1, @PasswordHash,
                 UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')), LOWER(CONVERT(nvarchar(36), NEWID())),
                 s.Phone, 1, 0, 1, 0,
                 s.FullName, s.UserType, s.CityId, s.IsActive,
                 CASE WHEN s.LastLoginDaysAgo IS NULL THEN NULL ELSE DATEADD(HOUR, -(s.LastLoginDaysAgo * 24 + 3), SYSDATETIMEOFFSET()) END,
                 N'seed', DATEADD(DAY, -s.DaysAgo, SYSDATETIMEOFFSET()), 0);

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM #U s
JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(s.Email)
JOIN dbo.AspNetRoles r ON r.NormalizedName = UPPER(s.RoleName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

COMMIT TRANSACTION;
PRINT '08_Users.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #U;
GO
