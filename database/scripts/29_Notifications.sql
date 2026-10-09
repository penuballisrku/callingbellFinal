/* =====================================================================================
   Calling Bell - 29_Notifications.sql
   Delivery of notifications beyond the app: web push (Firebase Cloud Messaging), WhatsApp
   (Meta Cloud API), RCS and SMS, with fallback between them, plus OTP over WhatsApp then SMS.

   * dbo.Notifications (existing, in-app inbox) gains: RouteCode (which channels deliver it),
     ReferenceType/ReferenceId, IdempotencyKey (one notification per event) and the background
     dispatcher's state (DispatchStatus, DispatchStep, NextDispatchAt, DispatchLockedUntil, ...).
   * dbo.NotificationDevices      : browsers registered for web push (FCM tokens) per user.
   * dbo.NotificationDeliveries   : every attempt per channel/provider and its status, updated by
                                    provider webhooks. No message text or OTP codes are stored.
   * dbo.NotificationRoutingRules : the channels each route tries, in priority order.
   * dbo.NotificationProviders    : providers per channel (and country), in priority order.
   * dbo.NotificationTemplates    : WhatsApp template names (Meta-approved) and SMS/RCS texts
                                    (India: DLT template ids) per route and channel.
   * Credentials are NOT stored here: they come from environment variables / Azure Key Vault.
   * Idempotent: safe to run repeatedly. Seed rows MERGE on their natural keys and never
     overwrite IsActive / Priority an administrator has changed (only inserted when missing).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

/* ---------- dbo.Notifications: routing, idempotency and dispatch state ---------- */
IF COL_LENGTH(N'dbo.Notifications', N'RouteCode') IS NULL            ALTER TABLE dbo.Notifications ADD RouteCode nvarchar(32) NULL;
IF COL_LENGTH(N'dbo.Notifications', N'ReferenceType') IS NULL        ALTER TABLE dbo.Notifications ADD ReferenceType nvarchar(32) NULL;
IF COL_LENGTH(N'dbo.Notifications', N'ReferenceId') IS NULL          ALTER TABLE dbo.Notifications ADD ReferenceId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.Notifications', N'IdempotencyKey') IS NULL       ALTER TABLE dbo.Notifications ADD IdempotencyKey nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.Notifications', N'DispatchStatus') IS NULL       ALTER TABLE dbo.Notifications ADD DispatchStatus nvarchar(16) NULL;
IF COL_LENGTH(N'dbo.Notifications', N'DispatchStep') IS NULL         ALTER TABLE dbo.Notifications ADD DispatchStep int NOT NULL CONSTRAINT DF_Notifications_DispatchStep DEFAULT 0;
IF COL_LENGTH(N'dbo.Notifications', N'DispatchAttempts') IS NULL     ALTER TABLE dbo.Notifications ADD DispatchAttempts int NOT NULL CONSTRAINT DF_Notifications_DispatchAttempts DEFAULT 0;
IF COL_LENGTH(N'dbo.Notifications', N'NextDispatchAt') IS NULL       ALTER TABLE dbo.Notifications ADD NextDispatchAt datetimeoffset NULL;
IF COL_LENGTH(N'dbo.Notifications', N'DispatchLockedUntil') IS NULL  ALTER TABLE dbo.Notifications ADD DispatchLockedUntil datetimeoffset NULL;
IF COL_LENGTH(N'dbo.Notifications', N'DispatchedAt') IS NULL         ALTER TABLE dbo.Notifications ADD DispatchedAt datetimeoffset NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Notifications_IdempotencyKey' AND object_id = OBJECT_ID(N'dbo.Notifications'))
    CREATE UNIQUE INDEX UX_Notifications_IdempotencyKey ON dbo.Notifications (IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
-- The dispatcher's queue: pending notifications due now.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_Dispatch' AND object_id = OBJECT_ID(N'dbo.Notifications'))
    CREATE INDEX IX_Notifications_Dispatch ON dbo.Notifications (DispatchStatus, NextDispatchAt) INCLUDE (DispatchLockedUntil)
        WHERE DispatchStatus IN (N'Pending', N'Processing');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_Type_CreatedOn' AND object_id = OBJECT_ID(N'dbo.Notifications'))
    CREATE INDEX IX_Notifications_Type_CreatedOn ON dbo.Notifications (NotificationType, CreatedOn DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notifications_Reference' AND object_id = OBJECT_ID(N'dbo.Notifications'))
    CREATE INDEX IX_Notifications_Reference ON dbo.Notifications (ReferenceType, ReferenceId) WHERE ReferenceId IS NOT NULL;
GO

/* ---------- dbo.NotificationDevices ---------- */
IF OBJECT_ID(N'dbo.NotificationDevices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationDevices
    (
        Id                uniqueidentifier NOT NULL CONSTRAINT PK_NotificationDevices PRIMARY KEY CONSTRAINT DF_NotificationDevices_Id DEFAULT NEWSEQUENTIALID(),
        UserId            nvarchar(450)    NOT NULL CONSTRAINT FK_NotificationDevices_AspNetUsers REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE,
        Token             nvarchar(1024)   NOT NULL,
        TokenHash         char(64)         NOT NULL,
        DeviceType        nvarchar(16)     NOT NULL CONSTRAINT DF_NotificationDevices_DeviceType DEFAULT N'WEB',
        Browser           nvarchar(40)     NULL,
        Platform          nvarchar(40)     NULL,
        IsActive          bit              NOT NULL CONSTRAINT DF_NotificationDevices_IsActive DEFAULT 1,
        DeactivatedReason nvarchar(40)     NULL,
        FailureCount      int              NOT NULL CONSTRAINT DF_NotificationDevices_FailureCount DEFAULT 0,
        CreatedAt         datetimeoffset   NOT NULL CONSTRAINT DF_NotificationDevices_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt         datetimeoffset   NOT NULL CONSTRAINT DF_NotificationDevices_UpdatedAt DEFAULT SYSDATETIMEOFFSET(),
        LastUsedAt        datetimeoffset   NULL
    );
    -- A token belongs to one browser profile: unique across users (a shared computer re-registers it to whoever signs in).
    CREATE UNIQUE INDEX UX_NotificationDevices_TokenHash ON dbo.NotificationDevices (TokenHash);
    CREATE INDEX IX_NotificationDevices_User_Active ON dbo.NotificationDevices (UserId, IsActive) INCLUDE (Token);
END
GO

/* ---------- dbo.NotificationDeliveries ---------- */
IF OBJECT_ID(N'dbo.NotificationDeliveries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationDeliveries
    (
        Id                uniqueidentifier NOT NULL CONSTRAINT PK_NotificationDeliveries PRIMARY KEY CONSTRAINT DF_NotificationDeliveries_Id DEFAULT NEWSEQUENTIALID(),
        NotificationId    uniqueidentifier NULL CONSTRAINT FK_NotificationDeliveries_Notifications REFERENCES dbo.Notifications (Id) ON DELETE CASCADE,
        OtpCodeId         uniqueidentifier NULL CONSTRAINT FK_NotificationDeliveries_OtpCodes REFERENCES dbo.OtpCodes (Id) ON DELETE CASCADE,
        RouteCode         nvarchar(32)     NOT NULL,
        RouteStep         int              NOT NULL,
        Channel           nvarchar(32)     NOT NULL,
        Provider          nvarchar(40)     NOT NULL,
        Status            nvarchar(16)     NOT NULL,
        Recipient         nvarchar(40)     NULL,
        ProviderMessageId nvarchar(200)    NULL,
        ErrorCode         nvarchar(60)     NULL,
        ErrorMessage      nvarchar(500)    NULL,
        AttemptCount      int              NOT NULL CONSTRAINT DF_NotificationDeliveries_AttemptCount DEFAULT 0,
        CreatedAt         datetimeoffset   NOT NULL CONSTRAINT DF_NotificationDeliveries_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
        SentAt            datetimeoffset   NULL,
        DeliveredAt       datetimeoffset   NULL,
        ReadAt            datetimeoffset   NULL,
        UpdatedAt         datetimeoffset   NULL,
        CONSTRAINT CK_NotificationDeliveries_Owner CHECK (NotificationId IS NOT NULL OR OtpCodeId IS NOT NULL)
    );
    CREATE INDEX IX_NotificationDeliveries_NotificationId ON dbo.NotificationDeliveries (NotificationId) WHERE NotificationId IS NOT NULL;
    CREATE INDEX IX_NotificationDeliveries_OtpCodeId ON dbo.NotificationDeliveries (OtpCodeId) WHERE OtpCodeId IS NOT NULL;
    CREATE INDEX IX_NotificationDeliveries_Status ON dbo.NotificationDeliveries (Status, CreatedAt);
    -- Webhooks find the attempt by the provider's message id.
    CREATE INDEX IX_NotificationDeliveries_ProviderMessageId ON dbo.NotificationDeliveries (Provider, ProviderMessageId) WHERE ProviderMessageId IS NOT NULL;
END
GO

/* ---------- dbo.NotificationProviders ---------- */
IF OBJECT_ID(N'dbo.NotificationProviders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationProviders
    (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_NotificationProviders PRIMARY KEY CONSTRAINT DF_NotificationProviders_Id DEFAULT NEWSEQUENTIALID(),
        ProviderName     nvarchar(40)     NOT NULL,
        Channel          nvarchar(32)     NOT NULL,
        IsActive         bit              NOT NULL CONSTRAINT DF_NotificationProviders_IsActive DEFAULT 1,
        Priority         int              NOT NULL CONSTRAINT DF_NotificationProviders_Priority DEFAULT 1,
        CountryCode      nchar(2)         NULL,
        ConfigurationKey nvarchar(100)    NULL,
        CreatedAt        datetimeoffset   NOT NULL CONSTRAINT DF_NotificationProviders_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt        datetimeoffset   NOT NULL CONSTRAINT DF_NotificationProviders_UpdatedAt DEFAULT SYSDATETIMEOFFSET()
    );
    CREATE UNIQUE INDEX UX_NotificationProviders_Channel_Name_Country ON dbo.NotificationProviders (Channel, ProviderName, CountryCode);
END
GO

/* ---------- dbo.NotificationTemplates ---------- */
IF OBJECT_ID(N'dbo.NotificationTemplates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationTemplates
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT PK_NotificationTemplates PRIMARY KEY CONSTRAINT DF_NotificationTemplates_Id DEFAULT NEWSEQUENTIALID(),
        TemplateCode    nvarchar(32)     NOT NULL,
        Channel         nvarchar(32)     NOT NULL,
        LanguageCode    nvarchar(10)     NOT NULL CONSTRAINT DF_NotificationTemplates_LanguageCode DEFAULT N'en',
        TemplateName    nvarchar(120)    NOT NULL,
        TemplateContent nvarchar(1000)   NOT NULL,
        IsActive        bit              NOT NULL CONSTRAINT DF_NotificationTemplates_IsActive DEFAULT 1,
        CreatedAt       datetimeoffset   NOT NULL CONSTRAINT DF_NotificationTemplates_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt       datetimeoffset   NOT NULL CONSTRAINT DF_NotificationTemplates_UpdatedAt DEFAULT SYSDATETIMEOFFSET()
    );
    CREATE UNIQUE INDEX UX_NotificationTemplates_Code_Channel_Language ON dbo.NotificationTemplates (TemplateCode, Channel, LanguageCode);
END
GO

/* ---------- dbo.NotificationRoutingRules ---------- */
IF OBJECT_ID(N'dbo.NotificationRoutingRules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationRoutingRules
    (
        Id        uniqueidentifier NOT NULL CONSTRAINT PK_NotificationRoutingRules PRIMARY KEY CONSTRAINT DF_NotificationRoutingRules_Id DEFAULT NEWSEQUENTIALID(),
        RouteCode nvarchar(32)     NOT NULL,
        Priority  int              NOT NULL,
        Channel   nvarchar(32)     NOT NULL,
        IsActive  bit              NOT NULL CONSTRAINT DF_NotificationRoutingRules_IsActive DEFAULT 1,
        CreatedAt datetimeoffset   NOT NULL CONSTRAINT DF_NotificationRoutingRules_CreatedAt DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt datetimeoffset   NOT NULL CONSTRAINT DF_NotificationRoutingRules_UpdatedAt DEFAULT SYSDATETIMEOFFSET()
    );
    CREATE UNIQUE INDEX UX_NotificationRoutingRules_Route_Channel ON dbo.NotificationRoutingRules (RouteCode, Channel);
    CREATE INDEX IX_NotificationRoutingRules_Route_Priority ON dbo.NotificationRoutingRules (RouteCode, Priority) INCLUDE (Channel, IsActive);
END
GO

/* ---------- Default routes ---------- */
MERGE dbo.NotificationRoutingRules AS t
USING (VALUES
    (N'NEW_LEAD', 1, N'WEB_PUSH'),
    (N'NEW_LEAD', 2, N'WHATSAPP'),
    (N'NEW_LEAD', 3, N'RCS'),
    (N'NEW_LEAD', 4, N'SMS'),
    (N'BOOKING',  1, N'WEB_PUSH'),
    (N'BOOKING',  2, N'WHATSAPP'),
    (N'OTP',      1, N'WHATSAPP_AUTHENTICATION'),
    (N'OTP',      2, N'SMS')
) AS s (RouteCode, Priority, Channel)
ON t.RouteCode = s.RouteCode AND t.Channel = s.Channel
WHEN NOT MATCHED BY TARGET THEN INSERT (RouteCode, Priority, Channel) VALUES (s.RouteCode, s.Priority, s.Channel);
GO

/* ---------- Providers (ConfigurationKey = configuration section; no secrets here) ---------- */
MERGE dbo.NotificationProviders AS t
USING (VALUES
    (N'Fcm',      N'WEB_PUSH',                1, NULL,  N'Notifications:Fcm'),
    (N'Meta',     N'WHATSAPP',                1, NULL,  N'Notifications:WhatsApp'),
    (N'Meta',     N'WHATSAPP_AUTHENTICATION', 1, NULL,  N'Notifications:WhatsApp'),
    (N'Rcs',      N'RCS',                     1, NULL,  N'Notifications:Rcs'),
    (N'Msg91',    N'SMS',                     1, N'IN', N'Notifications:Sms:Msg91'),
    (N'Twilio',   N'SMS',                     2, NULL,  N'Notifications:Sms:Twilio'),
    -- Development: writes "SMS not sent" to the log (never the text). Off unless Notifications:Sms:Log:Enabled is true.
    (N'Log',      N'SMS',                     9, NULL,  N'Notifications:Sms:Log')
) AS s (ProviderName, Channel, Priority, CountryCode, ConfigurationKey)
ON t.ProviderName = s.ProviderName AND t.Channel = s.Channel AND ISNULL(t.CountryCode, N'') = ISNULL(s.CountryCode, N'')
WHEN MATCHED AND ISNULL(t.ConfigurationKey, N'') <> s.ConfigurationKey THEN UPDATE SET ConfigurationKey = s.ConfigurationKey, UpdatedAt = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET THEN INSERT (ProviderName, Channel, Priority, CountryCode, ConfigurationKey) VALUES (s.ProviderName, s.Channel, s.Priority, s.CountryCode, s.ConfigurationKey);
GO

/* ---------- Templates ----------
   WHATSAPP: TemplateName must match the template approved in Meta Business Manager (category UTILITY for leads and bookings,
   AUTHENTICATION for codes). SMS (India): TemplateName = the DLT template id registered for the sender id - replace the
   placeholders below with your registered ids before going live. */
MERGE dbo.NotificationTemplates AS t
USING (VALUES
    (N'NEW_LEAD', N'WHATSAPP', N'en', N'callingbell_new_lead',
        N'🔔 New Lead' + NCHAR(10) + NCHAR(10) + N'Service: {{1}}' + NCHAR(10) + N'Area: {{2}}' + NCHAR(10) + N'Customer: {{3}}' + NCHAR(10) + NCHAR(10) + N'Open the lead in Calling Bell.'),
    (N'NEW_LEAD', N'RCS',      N'en', N'callingbell_new_lead',
        N'New lead on Calling Bell: {{1}} enquiry near {{2}} from {{3}}. Open {{4}} to respond.'),
    (N'NEW_LEAD', N'SMS',      N'en', N'DLT_TEMPLATE_ID_NEW_LEAD',
        N'New lead on Calling Bell: {{1}} enquiry near {{2}} from {{3}}. Respond at {{4}} - Calling Bell'),
    (N'BOOKING',  N'WHATSAPP', N'en', N'callingbell_booking_update',
        N'📅 {{1}}' + NCHAR(10) + NCHAR(10) + N'{{2}}' + NCHAR(10) + N'When: {{3}}' + NCHAR(10) + NCHAR(10) + N'View it in Calling Bell.'),
    (N'OTP',      N'WHATSAPP_AUTHENTICATION', N'en', N'callingbell_otp',
        N'{{1}} is your verification code. For your security, do not share this code.'),
    (N'OTP',      N'SMS',      N'en', N'DLT_TEMPLATE_ID_OTP',
        N'{{1}} is your Calling Bell verification code. It expires in {{2}} minutes. Do not share it with anyone.')
) AS s (TemplateCode, Channel, LanguageCode, TemplateName, TemplateContent)
ON t.TemplateCode = s.TemplateCode AND t.Channel = s.Channel AND t.LanguageCode = s.LanguageCode
WHEN NOT MATCHED BY TARGET THEN INSERT (TemplateCode, Channel, LanguageCode, TemplateName, TemplateContent) VALUES (s.TemplateCode, s.Channel, s.LanguageCode, s.TemplateName, s.TemplateContent);
GO

DECLARE @rules int = (SELECT COUNT(*) FROM dbo.NotificationRoutingRules), @providers int = (SELECT COUNT(*) FROM dbo.NotificationProviders),
        @templates int = (SELECT COUNT(*) FROM dbo.NotificationTemplates);
PRINT 'Notifications: ' + CAST(@rules AS nvarchar(10)) + ' routing rules, ' + CAST(@providers AS nvarchar(10)) + ' providers, '
    + CAST(@templates AS nvarchar(10)) + ' templates.';
GO
