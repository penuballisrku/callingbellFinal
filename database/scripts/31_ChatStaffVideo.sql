/* =====================================================================================
   Calling Bell - 31_ChatStaffVideo.sql
   Phase 2: customer-business chat, staff management and video consultations.

   * dbo.Conversations / dbo.ChatMessages : one conversation per customer and business, with
     text and attachments (images, PDFs stored in dbo.Media), read receipts and unread counts.
   * dbo.BusinessStaff (+ StaffServices, StaffHours) : a business's team: who does which
     services and when they work. Bookings.StaffId assigns a booking to a team member; booking
     slots count only the people who can do the service at that time.
   * dbo.VideoRooms : private browser-to-browser video calls for a booking or a conversation.
   * Notification routes CHAT and VIDEO (web push to the recipient's browsers).
   * Demo data: teams for active businesses (from their size and category), their services and
     hours, upcoming bookings assigned to them, and a few realistic conversations.
   * Idempotent: safe to run repeatedly; demo rows are only added where none exist.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ===================== Chat ===================== */
IF OBJECT_ID(N'dbo.Conversations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Conversations
    (
        Id                  uniqueidentifier NOT NULL CONSTRAINT PK_Conversations PRIMARY KEY CONSTRAINT DF_Conversations_Id DEFAULT NEWSEQUENTIALID(),
        BusinessId          uniqueidentifier NOT NULL CONSTRAINT FK_Conversations_Businesses REFERENCES dbo.Businesses (Id),
        CustomerUserId      nvarchar(450)    NOT NULL CONSTRAINT FK_Conversations_AspNetUsers REFERENCES dbo.AspNetUsers (Id),
        LastMessageAt       datetimeoffset   NULL,
        LastMessagePreview  nvarchar(200)    NULL,
        LastSenderRole      nvarchar(16)     NULL,
        CustomerUnreadCount int              NOT NULL CONSTRAINT DF_Conversations_CustomerUnread DEFAULT 0,
        BusinessUnreadCount int              NOT NULL CONSTRAINT DF_Conversations_BusinessUnread DEFAULT 0,
        CustomerLastReadAt  datetimeoffset   NULL,
        BusinessLastReadAt  datetimeoffset   NULL,
        CreatedBy           nvarchar(450)    NULL,
        CreatedOn           datetimeoffset   NOT NULL CONSTRAINT DF_Conversations_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy          nvarchar(450)    NULL,
        ModifiedOn          datetimeoffset   NULL,
        IsDeleted           bit              NOT NULL CONSTRAINT DF_Conversations_IsDeleted DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_Conversations_Business_Customer ON dbo.Conversations (BusinessId, CustomerUserId) WHERE IsDeleted = 0;
    CREATE INDEX IX_Conversations_Customer ON dbo.Conversations (CustomerUserId, LastMessageAt DESC) WHERE IsDeleted = 0;
    CREATE INDEX IX_Conversations_Business ON dbo.Conversations (BusinessId, LastMessageAt DESC) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.ChatMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatMessages
    (
        Id                    uniqueidentifier NOT NULL CONSTRAINT PK_ChatMessages PRIMARY KEY CONSTRAINT DF_ChatMessages_Id DEFAULT NEWSEQUENTIALID(),
        ConversationId        uniqueidentifier NOT NULL CONSTRAINT FK_ChatMessages_Conversations REFERENCES dbo.Conversations (Id) ON DELETE CASCADE,
        SenderUserId          nvarchar(450)    NOT NULL CONSTRAINT FK_ChatMessages_AspNetUsers REFERENCES dbo.AspNetUsers (Id),
        SenderRole            nvarchar(16)     NOT NULL,
        Body                  nvarchar(2000)   NULL,
        AttachmentMediaId     uniqueidentifier NULL,
        AttachmentName        nvarchar(200)    NULL,
        AttachmentContentType nvarchar(100)    NULL,
        AttachmentSize        int              NULL,
        SentAt                datetimeoffset   NOT NULL CONSTRAINT DF_ChatMessages_SentAt DEFAULT SYSDATETIMEOFFSET(),
        ReadAt                datetimeoffset   NULL,
        IsDeleted             bit              NOT NULL CONSTRAINT DF_ChatMessages_IsDeleted DEFAULT 0,
        CONSTRAINT CK_ChatMessages_Content CHECK (Body IS NOT NULL OR AttachmentMediaId IS NOT NULL)
    );
    CREATE INDEX IX_ChatMessages_Conversation_SentAt ON dbo.ChatMessages (ConversationId, SentAt DESC) INCLUDE (SenderRole, ReadAt) WHERE IsDeleted = 0;
END
GO

/* ===================== Staff ===================== */
IF OBJECT_ID(N'dbo.BusinessStaff', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BusinessStaff
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT PK_BusinessStaff PRIMARY KEY CONSTRAINT DF_BusinessStaff_Id DEFAULT NEWSEQUENTIALID(),
        BusinessId      uniqueidentifier NOT NULL CONSTRAINT FK_BusinessStaff_Businesses REFERENCES dbo.Businesses (Id),
        FullName        nvarchar(120)    NOT NULL,
        Title           nvarchar(80)     NULL,
        Phone           nvarchar(20)     NULL,
        Email           nvarchar(256)    NULL,
        Bio             nvarchar(500)    NULL,
        YearsExperience int              NULL,
        Languages       nvarchar(200)    NULL,
        UserId          nvarchar(450)    NULL CONSTRAINT FK_BusinessStaff_AspNetUsers REFERENCES dbo.AspNetUsers (Id),
        AcceptsBookings bit              NOT NULL CONSTRAINT DF_BusinessStaff_AcceptsBookings DEFAULT 1,
        IsActive        bit              NOT NULL CONSTRAINT DF_BusinessStaff_IsActive DEFAULT 1,
        SortOrder       int              NOT NULL CONSTRAINT DF_BusinessStaff_SortOrder DEFAULT 0,
        CreatedBy       nvarchar(450)    NULL,
        CreatedOn       datetimeoffset   NOT NULL CONSTRAINT DF_BusinessStaff_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy      nvarchar(450)    NULL,
        ModifiedOn      datetimeoffset   NULL,
        IsDeleted       bit              NOT NULL CONSTRAINT DF_BusinessStaff_IsDeleted DEFAULT 0
    );
    CREATE INDEX IX_BusinessStaff_Business ON dbo.BusinessStaff (BusinessId, IsActive, SortOrder) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.BusinessStaffServices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BusinessStaffServices
    (
        StaffId   uniqueidentifier NOT NULL CONSTRAINT FK_BusinessStaffServices_Staff REFERENCES dbo.BusinessStaff (Id) ON DELETE CASCADE,
        ServiceId uniqueidentifier NOT NULL CONSTRAINT FK_BusinessStaffServices_Services REFERENCES dbo.BusinessServices (Id),
        CONSTRAINT PK_BusinessStaffServices PRIMARY KEY (StaffId, ServiceId)
    );
    CREATE INDEX IX_BusinessStaffServices_Service ON dbo.BusinessStaffServices (ServiceId);
END
GO

IF OBJECT_ID(N'dbo.BusinessStaffHours', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BusinessStaffHours
    (
        Id        uniqueidentifier NOT NULL CONSTRAINT PK_BusinessStaffHours PRIMARY KEY CONSTRAINT DF_BusinessStaffHours_Id DEFAULT NEWSEQUENTIALID(),
        StaffId   uniqueidentifier NOT NULL CONSTRAINT FK_BusinessStaffHours_Staff REFERENCES dbo.BusinessStaff (Id) ON DELETE CASCADE,
        DayOfWeek tinyint          NOT NULL CONSTRAINT CK_BusinessStaffHours_Day CHECK (DayOfWeek BETWEEN 0 AND 6),
        OpenTime  time(0)          NULL,
        CloseTime time(0)          NULL,
        IsClosed  bit              NOT NULL CONSTRAINT DF_BusinessStaffHours_IsClosed DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_BusinessStaffHours_Staff_Day ON dbo.BusinessStaffHours (StaffId, DayOfWeek);
END
GO

IF COL_LENGTH(N'dbo.Bookings', N'StaffId') IS NULL
    ALTER TABLE dbo.Bookings ADD StaffId uniqueidentifier NULL CONSTRAINT FK_Bookings_BusinessStaff REFERENCES dbo.BusinessStaff (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bookings_Staff_Start' AND object_id = OBJECT_ID(N'dbo.Bookings'))
    CREATE INDEX IX_Bookings_Staff_Start ON dbo.Bookings (StaffId, ScheduledStart) INCLUDE (ScheduledEnd, Status) WHERE StaffId IS NOT NULL;
GO

/* ===================== Video ===================== */
IF OBJECT_ID(N'dbo.VideoRooms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VideoRooms
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT PK_VideoRooms PRIMARY KEY CONSTRAINT DF_VideoRooms_Id DEFAULT NEWSEQUENTIALID(),
        BusinessId      uniqueidentifier NOT NULL CONSTRAINT FK_VideoRooms_Businesses REFERENCES dbo.Businesses (Id),
        CustomerUserId  nvarchar(450)    NOT NULL CONSTRAINT FK_VideoRooms_AspNetUsers REFERENCES dbo.AspNetUsers (Id),
        BookingId       uniqueidentifier NULL CONSTRAINT FK_VideoRooms_Bookings REFERENCES dbo.Bookings (Id),
        ConversationId  uniqueidentifier NULL CONSTRAINT FK_VideoRooms_Conversations REFERENCES dbo.Conversations (Id),
        StaffId         uniqueidentifier NULL CONSTRAINT FK_VideoRooms_BusinessStaff REFERENCES dbo.BusinessStaff (Id),
        Status          nvarchar(16)     NOT NULL CONSTRAINT DF_VideoRooms_Status DEFAULT N'Scheduled',
        OpensAt         datetimeoffset   NULL,
        ClosesAt        datetimeoffset   NULL,
        StartedAt       datetimeoffset   NULL,
        EndedAt         datetimeoffset   NULL,
        DurationSeconds int              NULL,
        CreatedBy       nvarchar(450)    NULL,
        CreatedOn       datetimeoffset   NOT NULL CONSTRAINT DF_VideoRooms_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy      nvarchar(450)    NULL,
        ModifiedOn      datetimeoffset   NULL,
        IsDeleted       bit              NOT NULL CONSTRAINT DF_VideoRooms_IsDeleted DEFAULT 0
    );
    CREATE UNIQUE INDEX UX_VideoRooms_Booking ON dbo.VideoRooms (BookingId) WHERE BookingId IS NOT NULL AND IsDeleted = 0;
    CREATE INDEX IX_VideoRooms_Conversation ON dbo.VideoRooms (ConversationId, CreatedOn DESC) WHERE ConversationId IS NOT NULL;
    CREATE INDEX IX_VideoRooms_Customer ON dbo.VideoRooms (CustomerUserId, Status);
END
GO

-- A chat message can carry a video call ("Join video call" in the conversation).
IF COL_LENGTH(N'dbo.ChatMessages', N'VideoRoomId') IS NULL
    ALTER TABLE dbo.ChatMessages ADD VideoRoomId uniqueidentifier NULL CONSTRAINT FK_ChatMessages_VideoRooms REFERENCES dbo.VideoRooms (Id);
GO

/* ===================== Notification routes ===================== */
IF OBJECT_ID(N'dbo.NotificationRoutingRules', N'U') IS NOT NULL
MERGE dbo.NotificationRoutingRules AS t
USING (VALUES (N'CHAT', 1, N'WEB_PUSH'), (N'VIDEO', 1, N'WEB_PUSH'), (N'VIDEO', 2, N'WHATSAPP')) AS s (RouteCode, Priority, Channel)
ON t.RouteCode = s.RouteCode AND t.Channel = s.Channel
WHEN NOT MATCHED BY TARGET THEN INSERT (RouteCode, Priority, Channel) VALUES (s.RouteCode, s.Priority, s.Channel);
IF OBJECT_ID(N'dbo.NotificationTemplates', N'U') IS NOT NULL
MERGE dbo.NotificationTemplates AS t
USING (VALUES (N'VIDEO', N'WHATSAPP', N'en', N'callingbell_video_consultation',
    N'🎥 Video consultation with {{1}}' + NCHAR(10) + N'When: {{2}}' + NCHAR(10) + NCHAR(10) + N'Join from Calling Bell: {{3}}')) AS s (TemplateCode, Channel, LanguageCode, TemplateName, TemplateContent)
ON t.TemplateCode = s.TemplateCode AND t.Channel = s.Channel AND t.LanguageCode = s.LanguageCode
WHEN NOT MATCHED BY TARGET THEN INSERT (TemplateCode, Channel, LanguageCode, TemplateName, TemplateContent) VALUES (s.TemplateCode, s.Channel, s.LanguageCode, s.TemplateName, s.TemplateContent);
GO

/* ===================== Demo data: teams ===================== */
BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#Names') IS NOT NULL DROP TABLE #Names;
CREATE TABLE #Names (N int IDENTITY(0,1) PRIMARY KEY, FullName nvarchar(120));
INSERT INTO #Names (FullName) VALUES
(N'Ravi Teja Kandula'), (N'Priya Venkataraman'), (N'Mohammed Irfan'), (N'Sneha Kulkarni'), (N'Arjun Reddy Pasupuleti'), (N'Kavya Nair'),
(N'Suresh Babu Gorle'), (N'Ananya Mukherjee'), (N'Imran Shaikh'), (N'Deepika Rao'), (N'Karthik Subramanian'), (N'Pooja Agarwal'),
(N'Vikram Singh Rathore'), (N'Lakshmi Prasanna'), (N'Naveen Kumar Yadav'), (N'Farah Siddiqui'), (N'Harish Menon'), (N'Swathi Reddy'),
(N'Rahul Deshpande'), (N'Meera Iyer'), (N'Sandeep Chowdary'), (N'Nikhila Varma'), (N'Abdul Rahman'), (N'Shruti Bansal'),
(N'Prakash Hegde'), (N'Divya Pillai'), (N'Gaurav Malhotra'), (N'Haritha Bandi'), (N'Tarun Joshi'), (N'Bhavana Shetty');

IF OBJECT_ID('tempdb..#Titles') IS NOT NULL DROP TABLE #Titles;
CREATE TABLE #Titles (CategorySlug nvarchar(140), Seq int, Title nvarchar(80), Years int, PRIMARY KEY (CategorySlug, Seq));
INSERT INTO #Titles VALUES
(N'home-services', 0, N'Senior Technician', 12), (N'home-services', 1, N'Service Technician', 6), (N'home-services', 2, N'Field Supervisor', 15), (N'home-services', 3, N'Assistant Technician', 3),
(N'healthcare', 0, N'Consultant', 14), (N'healthcare', 1, N'Senior Resident', 7), (N'healthcare', 2, N'Nurse Coordinator', 9), (N'healthcare', 3, N'Physician Assistant', 5),
(N'education', 0, N'Senior Faculty', 11), (N'education', 1, N'Subject Tutor', 6), (N'education', 2, N'Academic Counsellor', 8), (N'education', 3, N'Junior Faculty', 3),
(N'beauty-wellness', 0, N'Senior Stylist', 10), (N'beauty-wellness', 1, N'Beauty Therapist', 6), (N'beauty-wellness', 2, N'Wellness Coach', 8), (N'beauty-wellness', 3, N'Junior Stylist', 2),
(N'legal-services', 0, N'Senior Advocate', 18), (N'legal-services', 1, N'Associate Advocate', 6), (N'legal-services', 2, N'Legal Consultant', 9), (N'legal-services', 3, N'Paralegal', 3),
(N'architecture-interiors', 0, N'Principal Architect', 16), (N'architecture-interiors', 1, N'Interior Designer', 7), (N'architecture-interiors', 2, N'Project Manager', 10), (N'architecture-interiors', 3, N'Design Associate', 3),
(N'food-dining', 0, N'Head Chef', 14), (N'food-dining', 1, N'Restaurant Manager', 9), (N'food-dining', 2, N'Sous Chef', 6), (N'food-dining', 3, N'Catering Coordinator', 4),
(N'events-weddings', 0, N'Lead Event Planner', 12), (N'events-weddings', 1, N'Decor Specialist', 7), (N'events-weddings', 2, N'Photographer', 8), (N'events-weddings', 3, N'Event Coordinator', 4),
(N'real-estate', 0, N'Senior Property Consultant', 13), (N'real-estate', 1, N'Property Advisor', 5), (N'real-estate', 2, N'Leasing Manager', 8), (N'real-estate', 3, N'Site Executive', 3),
(N'travel-transport', 0, N'Travel Consultant', 9), (N'travel-transport', 1, N'Senior Driver', 15), (N'travel-transport', 2, N'Operations Lead', 10), (N'travel-transport', 3, N'Booking Executive', 4),
(N'automotive', 0, N'Senior Mechanic', 14), (N'automotive', 1, N'Service Advisor', 7), (N'automotive', 2, N'Detailing Specialist', 5), (N'automotive', 3, N'Workshop Technician', 3),
(N'pet-care', 0, N'Veterinarian', 11), (N'pet-care', 1, N'Pet Groomer', 6), (N'pet-care', 2, N'Animal Behaviourist', 8), (N'pet-care', 3, N'Vet Assistant', 3),
(N'finance-tax', 0, N'Chartered Accountant', 15), (N'finance-tax', 1, N'Tax Consultant', 8), (N'finance-tax', 2, N'Audit Associate', 5), (N'finance-tax', 3, N'Accounts Executive', 3),
(N'appliance-repair', 0, N'Senior Service Engineer', 12), (N'appliance-repair', 1, N'Service Engineer', 6), (N'appliance-repair', 2, N'Field Technician', 4), (N'appliance-repair', 3, N'Trainee Technician', 1),
(N'it-digital', 0, N'Lead Developer', 11), (N'it-digital', 1, N'UI/UX Designer', 6), (N'it-digital', 2, N'Digital Marketing Specialist', 7), (N'it-digital', 3, N'Support Engineer', 3),
(N'construction-renovation', 0, N'Site Engineer', 13), (N'construction-renovation', 1, N'Project Supervisor', 10), (N'construction-renovation', 2, N'Quantity Surveyor', 7), (N'construction-renovation', 3, N'Foreman', 18),
(N'fashion-tailoring', 0, N'Master Tailor', 20), (N'fashion-tailoring', 1, N'Fashion Designer', 8), (N'fashion-tailoring', 2, N'Embroidery Artist', 12), (N'fashion-tailoring', 3, N'Fitting Specialist', 5),
(N'astrology-pooja', 0, N'Senior Astrologer', 22), (N'astrology-pooja', 1, N'Vedic Pandit', 15), (N'astrology-pooja', 2, N'Vastu Consultant', 10), (N'astrology-pooja', 3, N'Assistant Pandit', 4);

-- Up to four team members per active business without a team (fewer for small teams), never the same name twice in a business.
IF OBJECT_ID('tempdb..#NewStaff') IS NOT NULL DROP TABLE #NewStaff;
;WITH biz AS (
    SELECT b.Id, b.Name, b.Email, b.Languages, c.Slug AS CategorySlug,
           CASE WHEN ISNULL(b.TeamSize, 2) >= 8 THEN 4 WHEN ISNULL(b.TeamSize, 2) >= 4 THEN 3 ELSE 2 END AS Members,
           ABS(CHECKSUM(b.Id)) % 30 AS Seed
    FROM dbo.Businesses b JOIN dbo.Categories c ON c.Id = b.CategoryId
    WHERE b.Status = N'Active' AND b.IsDeleted = 0
      AND NOT EXISTS (SELECT 1 FROM dbo.BusinessStaff s WHERE s.BusinessId = b.Id)
), seq AS (SELECT v.n FROM (VALUES (0), (1), (2), (3)) v(n))
SELECT NEWID() AS Id, biz.Id AS BusinessId, nm.FullName, COALESCE(t.Title, N'Specialist') AS Title, COALESCE(t.Years, 5) AS Years,
       seq.n AS SortOrder, biz.Languages, biz.Name AS BusinessName,
       LOWER(REPLACE(REPLACE(nm.FullName, N' ', N'.'), N'''', N'')) + N'@demo.callingbell.in' AS Email,
       N'+91 9' + RIGHT(N'0000' + CAST(ABS(CHECKSUM(biz.Id, seq.n)) % 10000 AS nvarchar(4)), 4) + N' ' + RIGHT(N'00000' + CAST(ABS(CHECKSUM(seq.n, biz.Id)) % 100000 AS nvarchar(5)), 5) AS Phone
INTO #NewStaff
FROM biz CROSS JOIN seq
JOIN #Names nm ON nm.N = (biz.Seed + seq.n * 7) % 30
LEFT JOIN #Titles t ON t.CategorySlug = biz.CategorySlug AND t.Seq = seq.n
WHERE seq.n < biz.Members;

INSERT INTO dbo.BusinessStaff (Id, BusinessId, FullName, Title, Phone, Email, Bio, YearsExperience, Languages, AcceptsBookings, IsActive, SortOrder, CreatedBy, CreatedOn)
SELECT Id, BusinessId, FullName, Title, Phone, Email,
       Title + N' at ' + BusinessName + N' with ' + CAST(Years AS nvarchar(3)) + N' years of experience.',
       Years, Languages, CASE WHEN SortOrder = 3 THEN 0 ELSE 1 END, 1, SortOrder, N'seed', DATEADD(DAY, -(60 + SortOrder * 20), SYSDATETIMEOFFSET())
FROM #NewStaff;

-- Everyone does every service of their business, except the last member of bigger teams, who does the first half.
INSERT INTO dbo.BusinessStaffServices (StaffId, ServiceId)
SELECT s.Id, sv.Id
FROM #NewStaff s
JOIN dbo.BusinessServices sv ON sv.BusinessId = s.BusinessId AND sv.IsDeleted = 0
WHERE s.SortOrder < 3 OR ABS(CHECKSUM(sv.Id)) % 2 = 0;

-- Working hours: the business's hours; each member takes one weekday off.
INSERT INTO dbo.BusinessStaffHours (StaffId, DayOfWeek, OpenTime, CloseTime, IsClosed)
SELECT s.Id, h.DayOfWeek,
       CASE WHEN h.IsClosed = 1 OR h.DayOfWeek = (s.SortOrder + 1) % 7 THEN NULL ELSE h.OpenTime END,
       CASE WHEN h.IsClosed = 1 OR h.DayOfWeek = (s.SortOrder + 1) % 7 THEN NULL ELSE h.CloseTime END,
       CASE WHEN h.IsClosed = 1 OR h.DayOfWeek = (s.SortOrder + 1) % 7 THEN 1 ELSE 0 END
FROM #NewStaff s JOIN dbo.BusinessHours h ON h.BusinessId = s.BusinessId;

-- Pending and confirmed bookings without a professional: assign the members who do that service, in turn.
;WITH unassigned AS (
    SELECT bk.Id, bk.ServiceId, ROW_NUMBER() OVER (PARTITION BY bk.BusinessId ORDER BY bk.ScheduledStart) AS Rn
    FROM dbo.Bookings bk WHERE bk.StaffId IS NULL AND bk.Status IN (N'Pending', N'Confirmed', N'Completed') AND bk.IsDeleted = 0
), pick AS (
    SELECT u.Id AS BookingId,
           (SELECT TOP 1 ss.StaffId FROM dbo.BusinessStaffServices ss JOIN dbo.BusinessStaff st ON st.Id = ss.StaffId
            WHERE ss.ServiceId = u.ServiceId AND st.AcceptsBookings = 1 AND st.IsActive = 1
            ORDER BY (ABS(CHECKSUM(st.Id)) + u.Rn) % 97) AS StaffId
    FROM unassigned u
)
UPDATE bk SET StaffId = p.StaffId FROM dbo.Bookings bk JOIN pick p ON p.BookingId = bk.Id WHERE p.StaffId IS NOT NULL;

COMMIT TRANSACTION;
DROP TABLE #NewStaff; DROP TABLE #Titles; DROP TABLE #Names;
GO

/* ===================== Demo data: conversations ===================== */
IF NOT EXISTS (SELECT 1 FROM dbo.Conversations WHERE CreatedBy = N'seed')
BEGIN
    BEGIN TRANSACTION;
    -- Customers who booked a business recently chat with it about the booking.
    IF OBJECT_ID('tempdb..#Pairs') IS NOT NULL DROP TABLE #Pairs;
    SELECT TOP 24 NEWID() AS ConversationId, x.BusinessId, x.CustomerUserId, x.OwnerUserId, x.ServiceName, x.CustomerName, x.BusinessName, x.ScheduledStart,
           ROW_NUMBER() OVER (ORDER BY x.ScheduledStart DESC) AS Rn
    INTO #Pairs
    FROM (
        SELECT bk.BusinessId, bk.CustomerUserId, b.OwnerUserId, sv.Name AS ServiceName, bk.CustomerName, b.Name AS BusinessName, bk.ScheduledStart,
               ROW_NUMBER() OVER (PARTITION BY bk.BusinessId, bk.CustomerUserId ORDER BY bk.ScheduledStart DESC) AS PerPair,
               ROW_NUMBER() OVER (PARTITION BY bk.BusinessId ORDER BY bk.ScheduledStart DESC) AS PerBusiness
        FROM dbo.Bookings bk JOIN dbo.Businesses b ON b.Id = bk.BusinessId AND b.Status = N'Active'
        JOIN dbo.BusinessServices sv ON sv.Id = bk.ServiceId
        WHERE bk.Status IN (N'Pending', N'Confirmed') AND bk.ScheduledStart > SYSDATETIMEOFFSET()
    ) x
    WHERE x.PerPair = 1 AND x.PerBusiness <= 2
    ORDER BY x.ScheduledStart;

    IF OBJECT_ID('tempdb..#Msgs') IS NOT NULL DROP TABLE #Msgs;
    CREATE TABLE #Msgs (Seq int, Role nvarchar(16), Body nvarchar(400), MinutesAgo int);
    INSERT INTO #Msgs VALUES
    (1, N'Customer', N'Hi, I have booked {service} for {when}. Is there anything I should keep ready before you come?', 2880),
    (2, N'Business', N'Hello {first}, thank you for booking with {business}. Please keep the area clear and share any photos of the issue here if you can.', 2850),
    (3, N'Customer', N'Sure, will do. Can you also tell me roughly how long it will take?', 1500),
    (4, N'Business', N'It usually takes about an hour. Our professional will call you 30 minutes before arriving.', 1440),
    (5, N'Customer', N'Perfect, thanks!', 1430);

    INSERT INTO dbo.Conversations (Id, BusinessId, CustomerUserId, LastMessageAt, LastMessagePreview, LastSenderRole, CustomerUnreadCount, BusinessUnreadCount,
                                   CustomerLastReadAt, BusinessLastReadAt, CreatedBy, CreatedOn)
    SELECT p.ConversationId, p.BusinessId, p.CustomerUserId,
           DATEADD(MINUTE, -1430 + p.Rn * 7, SYSDATETIMEOFFSET()), N'Perfect, thanks!', N'Customer', 0, CASE WHEN p.Rn % 3 = 0 THEN 1 ELSE 0 END,
           DATEADD(MINUTE, -1430 + p.Rn * 7, SYSDATETIMEOFFSET()), CASE WHEN p.Rn % 3 = 0 THEN DATEADD(MINUTE, -1440 + p.Rn * 7, SYSDATETIMEOFFSET()) ELSE DATEADD(MINUTE, -1420 + p.Rn * 7, SYSDATETIMEOFFSET()) END,
           N'seed', DATEADD(MINUTE, -2880 + p.Rn * 7, SYSDATETIMEOFFSET())
    FROM #Pairs p;

    INSERT INTO dbo.ChatMessages (ConversationId, SenderUserId, SenderRole, Body, SentAt, ReadAt)
    SELECT p.ConversationId, CASE WHEN m.Role = N'Customer' THEN p.CustomerUserId ELSE p.OwnerUserId END, m.Role,
           REPLACE(REPLACE(REPLACE(REPLACE(m.Body, N'{service}', p.ServiceName), N'{business}', p.BusinessName),
                   N'{first}', LEFT(p.CustomerName, CHARINDEX(N' ', p.CustomerName + N' ') - 1)),
                   N'{when}', FORMAT(SWITCHOFFSET(p.ScheduledStart, '+05:30'), 'ddd d MMM, h:mm tt')),
           DATEADD(MINUTE, -m.MinutesAgo + p.Rn * 7, SYSDATETIMEOFFSET()),
           CASE WHEN m.Seq = 5 AND p.Rn % 3 = 0 THEN NULL ELSE DATEADD(MINUTE, -m.MinutesAgo + p.Rn * 7 + 5, SYSDATETIMEOFFSET()) END
    FROM #Pairs p CROSS JOIN #Msgs m;

    COMMIT TRANSACTION;
    DROP TABLE #Msgs; DROP TABLE #Pairs;
END
GO

DECLARE @staff int = (SELECT COUNT(*) FROM dbo.BusinessStaff WHERE IsDeleted = 0), @conv int = (SELECT COUNT(*) FROM dbo.Conversations),
        @msgs int = (SELECT COUNT(*) FROM dbo.ChatMessages), @assigned int = (SELECT COUNT(*) FROM dbo.Bookings WHERE StaffId IS NOT NULL);
PRINT 'Chat, staff & video: ' + CAST(@staff AS nvarchar(10)) + ' team members, ' + CAST(@assigned AS nvarchar(10)) + ' bookings assigned, '
    + CAST(@conv AS nvarchar(10)) + ' conversations, ' + CAST(@msgs AS nvarchar(10)) + ' messages.';
GO
