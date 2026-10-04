/* =====================================================================================
   Calling Bell - 21_ServerMemory.sql   (instance-wide; run once per SQL Server, NOT part of RunAll.sql)

   Why: with the default "min server memory" of 16 MB, Windows can trim SQL Server down to a
   few hundred MB when the machine is busy (browsers, IDEs). Queries then wait for a memory
   grant (wait type RESOURCE_SEMAPHORE) and give up after SQL Server's 25-second limit, so
   pages that normally answer in milliseconds take 25 seconds.

   What: keeps at least 1 GB for SQL Server once it has used it, and caps it at 4 GB so it
   doesn't starve the rest of a development machine. Adjust both for your server
   (production: give SQL Server most of the machine's memory).

   Revert:  EXEC sp_configure 'min server memory (MB)', 16;
            EXEC sp_configure 'max server memory (MB)', 2147483647; RECONFIGURE;
   ===================================================================================== */
SET NOCOUNT ON;
EXEC sys.sp_configure N'show advanced options', 1;
RECONFIGURE;
EXEC sys.sp_configure N'min server memory (MB)', 1024;
EXEC sys.sp_configure N'max server memory (MB)', 4096;
RECONFIGURE;
GO
SELECT name, value_in_use FROM sys.configurations WHERE name IN (N'min server memory (MB)', N'max server memory (MB)');
GO
