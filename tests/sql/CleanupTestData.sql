/*
    Removes what the automated tests create (everything they write carries the marker "[cb-test]"):
      - bookings from tests/api/phone_flow.py, with their video rooms and notifications;
      - chat messages and attachments from tests/api/phase2_api.py and tests/e2e/phase2.cjs, the test-only conversations
        (the demo customer's non-seeded ones that hold nothing but test messages and video-call notices), their video rooms
        and notifications;
      - team members added by the tests.
    Seeded data and real conversations are left alone; conversations that keep messages get their preview and unread counts
    recalculated. Safe to run any number of times.

    sqlcmd -S .\SQLEXPRESS -E -C -I -d CallingBell -i tests\sql\CleanupTestData.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @mark nvarchar(20) = N'%![cb-test]%';   -- "[" escaped with "!": [cb-test] would otherwise be a character class
DECLARE @testCustomer nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = N'NAVEEN.MENON@DEMO.CALLINGBELL.IN');

BEGIN TRANSACTION;

-- ---------- bookings ----------
DECLARE @bookings TABLE (Id uniqueidentifier PRIMARY KEY, BookingNumber nvarchar(40));
INSERT @bookings SELECT Id, BookingNumber FROM dbo.Bookings WHERE Notes LIKE @mark ESCAPE '!';

-- ---------- chat ----------
DECLARE @messages TABLE (Id uniqueidentifier PRIMARY KEY, ConversationId uniqueidentifier, MediaId uniqueidentifier NULL);
INSERT @messages SELECT Id, ConversationId, AttachmentMediaId FROM dbo.ChatMessages WHERE Body LIKE @mark ESCAPE '!';

DECLARE @conversations TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @conversations
SELECT c.Id FROM dbo.Conversations c
WHERE c.CustomerUserId = @testCustomer AND c.CreatedBy <> N'seed'
  AND NOT EXISTS (SELECT 1 FROM dbo.ChatMessages m WHERE m.ConversationId = c.Id AND m.VideoRoomId IS NULL
                  AND m.Id NOT IN (SELECT Id FROM @messages));

DECLARE @rooms TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @rooms SELECT Id FROM dbo.VideoRooms
WHERE BookingId IN (SELECT Id FROM @bookings) OR ConversationId IN (SELECT Id FROM @conversations);

-- Video-call notices in the test conversations, and any that point at a removed room.
INSERT @messages SELECT m.Id, m.ConversationId, m.AttachmentMediaId FROM dbo.ChatMessages m
WHERE (m.ConversationId IN (SELECT Id FROM @conversations) OR m.VideoRoomId IN (SELECT Id FROM @rooms))
  AND m.Id NOT IN (SELECT Id FROM @messages);

-- ---------- notifications (their delivery attempts cascade) ----------
DELETE n FROM dbo.Notifications n
WHERE n.ReferenceId IN (SELECT Id FROM @bookings UNION ALL SELECT Id FROM @rooms UNION ALL SELECT Id FROM @messages)
   OR EXISTS (SELECT 1 FROM @bookings b WHERE n.Message LIKE N'%' + b.BookingNumber + N'%' OR n.LinkUrl LIKE N'%' + CAST(b.Id AS nvarchar(36)) + N'%');
DECLARE @notifications int = @@ROWCOUNT;

DELETE FROM dbo.ChatMessages WHERE Id IN (SELECT Id FROM @messages);
DECLARE @messageCount int = @@ROWCOUNT;
DELETE FROM dbo.Media WHERE MediaId IN (SELECT MediaId FROM @messages WHERE MediaId IS NOT NULL);
DECLARE @mediaCount int = @@ROWCOUNT;
DELETE FROM dbo.VideoRooms WHERE Id IN (SELECT Id FROM @rooms);
DECLARE @roomCount int = @@ROWCOUNT;
DELETE FROM dbo.Conversations WHERE Id IN (SELECT Id FROM @conversations);
DECLARE @conversationCount int = @@ROWCOUNT;
DELETE FROM dbo.Bookings WHERE Id IN (SELECT Id FROM @bookings);
DECLARE @bookingCount int = @@ROWCOUNT;

-- Conversations that keep real messages: preview and unread counts from what is left.
UPDATE c SET
    LastMessageAt       = x.SentAt,
    LastMessagePreview  = LEFT(x.Body, 200),
    LastSenderRole      = x.SenderRole,
    CustomerUnreadCount = (SELECT COUNT(*) FROM dbo.ChatMessages m WHERE m.ConversationId = c.Id AND m.SenderRole = N'Business' AND m.ReadAt IS NULL AND m.IsDeleted = 0),
    BusinessUnreadCount = (SELECT COUNT(*) FROM dbo.ChatMessages m WHERE m.ConversationId = c.Id AND m.SenderRole = N'Customer' AND m.ReadAt IS NULL AND m.IsDeleted = 0)
FROM dbo.Conversations c
CROSS APPLY (SELECT TOP (1) m.SentAt, m.Body, m.SenderRole FROM dbo.ChatMessages m
             WHERE m.ConversationId = c.Id AND m.IsDeleted = 0 ORDER BY m.SentAt DESC) x
WHERE c.Id IN (SELECT DISTINCT ConversationId FROM @messages);

-- ---------- team members ----------
DECLARE @staff TABLE (Id uniqueidentifier PRIMARY KEY);
INSERT @staff SELECT Id FROM dbo.BusinessStaff WHERE Bio LIKE @mark ESCAPE '!' OR Title LIKE @mark ESCAPE '!';
UPDATE dbo.Bookings SET StaffId = NULL WHERE StaffId IN (SELECT Id FROM @staff);
UPDATE dbo.VideoRooms SET StaffId = NULL WHERE StaffId IN (SELECT Id FROM @staff);
DELETE FROM dbo.BusinessStaff WHERE Id IN (SELECT Id FROM @staff);   -- services and hours cascade
DECLARE @staffCount int = @@ROWCOUNT;

COMMIT;

PRINT CONCAT(N'Removed test data: ', @bookingCount, N' booking(s), ', @conversationCount, N' conversation(s), ', @messageCount, N' message(s), ',
             @mediaCount, N' attachment(s), ', @roomCount, N' video room(s), ', @notifications, N' notification(s), ', @staffCount, N' team member(s).');
