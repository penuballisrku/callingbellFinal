/* =====================================================================================
   CallInBell - 42_DatabaseSettings.sql
   AUTO_CLOSE off. SQL Server Express turns it on for new databases: the database then
   shuts down whenever no connection is open, dropping its cached data and query plans,
   and the next request (often a visitor's search) waits many seconds while it reopens.

   Idempotent. Applies to the database the scripts run against.
   ===================================================================================== */
SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_auto_close_on = 1)
    ALTER DATABASE CURRENT SET AUTO_CLOSE OFF;
GO
