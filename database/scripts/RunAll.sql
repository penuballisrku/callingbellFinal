/* =====================================================================================
   Calling Bell - RunAll.sql
   Runs every database script in dependency order. Requires SQLCMD mode:
     * SSMS:            Query > SQLCMD Mode, then execute
     * Azure Data Studio: enable "SQLCMD" in the query toolbar
     * Command line:    sqlcmd -S .\SQLEXPRESS -E -C -f 65001 -d CallingBell -i RunAll.sql
                        (-f 65001 reads the scripts as UTF-8 so text such as "Cafés" or "24×7" is stored correctly)
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
:r $(ScriptDir)\14_MarketingContent.sql
:r $(ScriptDir)\15_MarketingMedia.sql
:r $(ScriptDir)\16_PopularServices.sql
:r $(ScriptDir)\17_HomeContent.sql
:r $(ScriptDir)\18_ExternalSearch.sql
:r $(ScriptDir)\19_CityAltNames.sql
:r $(ScriptDir)\20_Performance.sql
:r $(ScriptDir)\22_AboutContent.sql
:r $(ScriptDir)\23_TrustSafetyContent.sql
:r $(ScriptDir)\24_ContactSupportContent.sql
:r $(ScriptDir)\25_CountryPricing.sql
:r $(ScriptDir)\26_CountryImages.sql
:r $(ScriptDir)\27_Seo.sql
:r $(ScriptDir)\28_PopularSearches.sql
:r $(ScriptDir)\29_Notifications.sql
:r $(ScriptDir)\30_JoinCallingBell.sql
:r $(ScriptDir)\31_ChatStaffVideo.sql
