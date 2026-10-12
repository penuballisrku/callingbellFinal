/* =====================================================================================
   Calling Bell - 33_NotificationEvents.sql
   Push notifications for more events (each also shows in the app's notification bell):
     * LEAD_STATUS      the business accepted, quoted, confirmed or declined a customer's request
     * BOOKING_REMINDER the day before a confirmed booking (customer)
     * SUBSCRIPTION     a paid plan ends in 7 days / 1 day without a renewal (business owner)
     * OFFERS           a campaign near the customer; only to customers who turned offers on
   Booking reminders, subscription and offers are sent by the scheduled notifications worker.
   * AspNetUsers.ReceiveOffers : the customer's "Offers near me" choice (promotional, off by default).
   Idempotent: guarded ALTER, MERGE on (RouteCode, Channel).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'dbo.AspNetUsers', N'ReceiveOffers') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD ReceiveOffers bit NOT NULL CONSTRAINT DF_AspNetUsers_ReceiveOffers DEFAULT 0;
GO

/* Web push for each (WhatsApp / SMS would need Meta-approved templates and DLT registration first). */
MERGE dbo.NotificationRoutingRules AS t
USING (VALUES
    (N'LEAD_STATUS',      1, N'WEB_PUSH'),
    (N'BOOKING_REMINDER', 1, N'WEB_PUSH'),
    (N'SUBSCRIPTION',     1, N'WEB_PUSH'),
    (N'OFFERS',           1, N'WEB_PUSH')
) AS s (RouteCode, Priority, Channel)
ON t.RouteCode = s.RouteCode AND t.Channel = s.Channel
WHEN NOT MATCHED BY TARGET THEN INSERT (RouteCode, Priority, Channel) VALUES (s.RouteCode, s.Priority, s.Channel);
GO

PRINT '33_NotificationEvents.sql completed';
