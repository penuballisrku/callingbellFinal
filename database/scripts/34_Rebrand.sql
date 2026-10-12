/* =====================================================================================
   CallInBell - 34_Rebrand.sql
   The platform is now called CallInBell (it was "Calling Bell"). The earlier scripts already seed the new name; this updates text
   that an existing database stored under the old one:
     * content shown on the site: MarketingContent, Banners, LookupValues
     * notification templates and in-app notifications already sent
     * reviews written by the seeded demo customers (@demo.callingbell.in) only: real customers' words are never changed
   Identifiers stay as they are (database name, template names such as callingbell_new_lead, the callingbell.in domain).
   Idempotent: every UPDATE only touches rows that still contain the old name.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

DECLARE @old nvarchar(20) = N'Calling Bell', @new nvarchar(20) = N'CallInBell';

BEGIN TRANSACTION;

UPDATE dbo.MarketingContent SET
    Eyebrow = REPLACE(Eyebrow, @old, @new), Title = REPLACE(Title, @old, @new),
    Subtitle = REPLACE(Subtitle, @old, @new), Body = REPLACE(Body, @old, @new)
WHERE Eyebrow LIKE N'%Calling Bell%' OR Title LIKE N'%Calling Bell%' OR Subtitle LIKE N'%Calling Bell%' OR Body LIKE N'%Calling Bell%';

UPDATE dbo.Banners SET Title = REPLACE(Title, @old, @new) WHERE Title LIKE N'%Calling Bell%';

UPDATE dbo.LookupValues SET Description = REPLACE(Description, @old, @new) WHERE Description LIKE N'%Calling Bell%';

UPDATE dbo.NotificationTemplates SET TemplateContent = REPLACE(TemplateContent, @old, @new) WHERE TemplateContent LIKE N'%Calling Bell%';

UPDATE dbo.Notifications SET Title = REPLACE(Title, @old, @new), Message = REPLACE(Message, @old, @new)
WHERE Title LIKE N'%Calling Bell%' OR Message LIKE N'%Calling Bell%';

UPDATE r SET
    Title = REPLACE(r.Title, @old, @new), Comment = REPLACE(r.Comment, @old, @new), OwnerReply = REPLACE(r.OwnerReply, @old, @new)
FROM dbo.Reviews r
JOIN dbo.AspNetUsers u ON u.Id = r.CustomerUserId
WHERE u.Email LIKE N'%@demo.callingbell.in'
  AND (r.Title LIKE N'%Calling Bell%' OR r.Comment LIKE N'%Calling Bell%' OR r.OwnerReply LIKE N'%Calling Bell%');

COMMIT TRANSACTION;
PRINT '34_Rebrand: done';
