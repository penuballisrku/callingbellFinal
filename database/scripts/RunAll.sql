/* =====================================================================================
   Calling Bell - RunAll.sql
   Runs every database script in dependency order. Requires SQLCMD mode:
     * SSMS:            Query > SQLCMD Mode, then execute
     * Azure Data Studio: enable "SQLCMD" in the query toolbar
     * Command line:    sqlcmd -S .\SQLEXPRESS -E -C -d CallingBell -i RunAll.sql
   Run from this folder (or set :setvar ScriptDir to an absolute path).

   Order note: 08_Users runs before 04-07 because reviews, enquiries and bookings
   reference customer accounts. Business-owner accounts are created by 04_Businesses.
   ===================================================================================== */
:setvar ScriptDir "."
:on error exit

:r $(ScriptDir)\00_Schema.sql
:r $(ScriptDir)\01_MasterData.sql
:r $(ScriptDir)\02_Locations.sql
:r $(ScriptDir)\03_Categories.sql
:r $(ScriptDir)\08_Users.sql
:r $(ScriptDir)\04_Businesses.sql
:r $(ScriptDir)\05_BusinessServices.sql
:r $(ScriptDir)\06_Banners.sql
:r $(ScriptDir)\07_Reviews.sql
:r $(ScriptDir)\09_Enquiries.sql
:r $(ScriptDir)\10_Bookings.sql
:r $(ScriptDir)\11_Advertisements.sql
:r $(ScriptDir)\12_Subscriptions.sql
:r $(ScriptDir)\13_DashboardDemoData.sql
