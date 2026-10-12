/* =====================================================================================
   CallInBell - 35_WebPushVapid.sql
   Standard Web Push (VAPID) as a web push provider, without Firebase: the server signs each push with its own key pair
   (Notifications:WebPush) and posts it straight to the browser's push service. Same priority as Firebase: whichever is
   configured sends (VAPID devices are NotificationDevices rows with DeviceType WEBPUSH, Firebase ones WEB).
   Idempotent: MERGE on (ProviderName, Channel, CountryCode).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

MERGE dbo.NotificationProviders AS t
USING (VALUES
    (N'WebPush', N'WEB_PUSH', 1, NULL, N'Notifications:WebPush')
) AS s (ProviderName, Channel, Priority, CountryCode, ConfigurationKey)
ON t.ProviderName = s.ProviderName AND t.Channel = s.Channel AND ISNULL(t.CountryCode, N'') = ISNULL(s.CountryCode, N'')
WHEN MATCHED AND ISNULL(t.ConfigurationKey, N'') <> s.ConfigurationKey THEN UPDATE SET ConfigurationKey = s.ConfigurationKey, UpdatedAt = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET THEN INSERT (ProviderName, Channel, Priority, CountryCode, ConfigurationKey) VALUES (s.ProviderName, s.Channel, s.Priority, s.CountryCode, s.ConfigurationKey);
GO

PRINT '35_WebPushVapid: done';
