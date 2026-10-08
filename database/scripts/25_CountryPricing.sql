/* =====================================================================================
   Calling Bell - 25_CountryPricing.sql
   Currency and fair local prices per country (dbo.CountryPricing).

   Platform prices (subscription plans, Calling Bell's typical service prices in the AI
   overview) are set in Indian rupees. In another country they are shown and charged as
       INR price x PriceMultiplier, rounded to RoundingStep, in that country's currency.
   PriceMultiplier is purchasing-power based: the country's PPP conversion factor divided
   by India's (World Bank, GDP PPP, local currency per international $, 2023; India about
   20.6). So the Gold plan costs about what it is worth locally, not the plain exchange-
   rate amount (which would make it far too cheap in the US and Europe).
   A business's own service prices are never converted: they are in its own currency.

   TaxRate: India charges 18% GST. Services billed from India to customers abroad are
   zero-rated exports, so other countries carry no tax here; set TaxName/TaxRate for a
   country once Calling Bell registers for its sales tax/VAT.

   "ZZ" is the default for countries without a row (US dollars, US price level).
   The values are reasonable starting points; adjust them per market as needed.

   * Creates the table when missing (no EF migration needed).
   * Idempotent: MERGE by CountryCode; changed rows are updated, nothing is duplicated.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.CountryPricing', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CountryPricing
    (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_CountryPricing PRIMARY KEY CONSTRAINT DF_CountryPricing_Id DEFAULT NEWSEQUENTIALID(),
        CountryCode      nchar(2)         NOT NULL,
        CountryName      nvarchar(80)     NOT NULL,
        CurrencyCode     nchar(3)         NOT NULL,
        Locale           nvarchar(20)     NOT NULL,
        PriceMultiplier  decimal(18,6)    NOT NULL CONSTRAINT CK_CountryPricing_Multiplier CHECK (PriceMultiplier > 0),
        RoundingStep     decimal(12,2)    NOT NULL CONSTRAINT CK_CountryPricing_Step CHECK (RoundingStep > 0),
        TaxName          nvarchar(20)     NULL,
        TaxRate          decimal(5,4)     NOT NULL CONSTRAINT DF_CountryPricing_TaxRate DEFAULT (0) CONSTRAINT CK_CountryPricing_TaxRate CHECK (TaxRate >= 0 AND TaxRate < 1),
        IsActive         bit              NOT NULL CONSTRAINT DF_CountryPricing_IsActive DEFAULT (1),
        IsDeleted        bit              NOT NULL CONSTRAINT DF_CountryPricing_IsDeleted DEFAULT (0),
        CreatedBy        nvarchar(450)    NULL,
        CreatedOn        datetimeoffset   NOT NULL CONSTRAINT DF_CountryPricing_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy       nvarchar(450)    NULL,
        ModifiedOn       datetimeoffset   NULL
    );
    CREATE UNIQUE INDEX IX_CountryPricing_CountryCode ON dbo.CountryPricing (CountryCode);
END
GO

-- Subscriptions and invoices record the currency they were paid in (existing rows: INR). PaymentOrders already has Currency.
IF COL_LENGTH('dbo.BusinessSubscriptions', 'Currency') IS NULL
    ALTER TABLE dbo.BusinessSubscriptions ADD Currency nchar(3) NOT NULL CONSTRAINT DF_BusinessSubscriptions_Currency DEFAULT (N'INR');
IF COL_LENGTH('dbo.Payments', 'Currency') IS NULL
    ALTER TABLE dbo.Payments ADD Currency nchar(3) NOT NULL CONSTRAINT DF_Payments_Currency DEFAULT (N'INR');
GO

BEGIN TRANSACTION;

-- Ppp: local currency per international $ (World Bank, 2023, rounded). Multiplier = Ppp / India's 20.6.
DECLARE @IndiaPpp decimal(18,6) = 20.6;
DECLARE @Rows TABLE
(
    CountryCode nchar(2) NOT NULL PRIMARY KEY, CountryName nvarchar(80) NOT NULL, CurrencyCode nchar(3) NOT NULL, Locale nvarchar(20) NOT NULL,
    Ppp decimal(18,6) NOT NULL, RoundingStep decimal(12,2) NOT NULL, TaxName nvarchar(20) NULL, TaxRate decimal(5,4) NOT NULL
);
INSERT INTO @Rows (CountryCode, CountryName, CurrencyCode, Locale, Ppp, RoundingStep, TaxName, TaxRate) VALUES
(N'IN', N'India',                N'INR', N'en-IN',  20.6,    1,    N'GST', 0.18),
(N'ZZ', N'Other countries',      N'USD', N'en-US',   1.00,   1,    NULL,   0),
(N'US', N'United States',        N'USD', N'en-US',   1.00,   1,    NULL,   0),
(N'CA', N'Canada',               N'CAD', N'en-CA',   1.18,   1,    NULL,   0),
(N'MX', N'Mexico',               N'MXN', N'es-MX',  10.0,   10,    NULL,   0),
(N'BR', N'Brazil',               N'BRL', N'pt-BR',   2.5,    1,    NULL,   0),
(N'GB', N'United Kingdom',       N'GBP', N'en-GB',   0.68,   1,    NULL,   0),
(N'IE', N'Ireland',              N'EUR', N'en-IE',   0.78,   1,    NULL,   0),
(N'DE', N'Germany',              N'EUR', N'de-DE',   0.73,   1,    NULL,   0),
(N'FR', N'France',               N'EUR', N'fr-FR',   0.71,   1,    NULL,   0),
(N'NL', N'Netherlands',          N'EUR', N'nl-NL',   0.77,   1,    NULL,   0),
(N'IT', N'Italy',                N'EUR', N'it-IT',   0.61,   1,    NULL,   0),
(N'ES', N'Spain',                N'EUR', N'es-ES',   0.58,   1,    NULL,   0),
(N'CH', N'Switzerland',          N'CHF', N'de-CH',   1.06,   1,    NULL,   0),
(N'SE', N'Sweden',               N'SEK', N'sv-SE',   8.4,   10,    NULL,   0),
(N'NO', N'Norway',               N'NOK', N'nb-NO',   9.5,   10,    NULL,   0),
(N'DK', N'Denmark',              N'DKK', N'da-DK',   6.2,   10,    NULL,   0),
(N'AE', N'United Arab Emirates', N'AED', N'en-AE',   2.30,   1,    NULL,   0),
(N'SA', N'Saudi Arabia',         N'SAR', N'en-SA',   1.60,   1,    NULL,   0),
(N'QA', N'Qatar',                N'QAR', N'en-QA',   2.30,   1,    NULL,   0),
(N'KW', N'Kuwait',               N'KWD', N'en-KW',   0.18,   0.25, NULL,   0),
(N'OM', N'Oman',                 N'OMR', N'en-OM',   0.19,   0.25, NULL,   0),
(N'BH', N'Bahrain',              N'BHD', N'en-BH',   0.21,   0.25, NULL,   0),
(N'SG', N'Singapore',            N'SGD', N'en-SG',   0.82,   1,    NULL,   0),
(N'MY', N'Malaysia',             N'MYR', N'ms-MY',   1.50,   1,    NULL,   0),
(N'TH', N'Thailand',             N'THB', N'th-TH',  11.9,   10,    NULL,   0),
(N'PH', N'Philippines',          N'PHP', N'en-PH',  19.5,   10,    NULL,   0),
(N'ID', N'Indonesia',            N'IDR', N'id-ID',  4800,  1000,   NULL,   0),
(N'JP', N'Japan',                N'JPY', N'ja-JP',  94,    100,    NULL,   0),
(N'AU', N'Australia',            N'AUD', N'en-AU',   1.45,   1,    NULL,   0),
(N'NZ', N'New Zealand',          N'NZD', N'en-NZ',   1.44,   1,    NULL,   0),
(N'ZA', N'South Africa',         N'ZAR', N'en-ZA',   7.0,   10,    NULL,   0),
(N'KE', N'Kenya',                N'KES', N'en-KE',  46,     50,    NULL,   0),
(N'NG', N'Nigeria',              N'NGN', N'en-NG', 210,    500,    NULL,   0),
(N'NP', N'Nepal',                N'NPR', N'en-NP',  36,     10,    NULL,   0),
(N'BD', N'Bangladesh',           N'BDT', N'en-BD',  31,     10,    NULL,   0),
(N'LK', N'Sri Lanka',            N'LKR', N'en-LK', 115,     50,    NULL,   0),
(N'PK', N'Pakistan',             N'PKR', N'en-PK',  77,     50,    NULL,   0);

MERGE dbo.CountryPricing AS t
USING (SELECT CountryCode, CountryName, CurrencyCode, Locale, CAST(ROUND(Ppp / @IndiaPpp, 6) AS decimal(18,6)) AS PriceMultiplier,
              RoundingStep, TaxName, TaxRate FROM @Rows) AS s
ON t.CountryCode = s.CountryCode
WHEN MATCHED AND (t.CountryName <> s.CountryName OR t.CurrencyCode <> s.CurrencyCode OR t.Locale <> s.Locale
                  OR t.PriceMultiplier <> s.PriceMultiplier OR t.RoundingStep <> s.RoundingStep
                  OR ISNULL(t.TaxName, N'') <> ISNULL(s.TaxName, N'') OR t.TaxRate <> s.TaxRate OR t.IsDeleted = 1)
    THEN UPDATE SET CountryName = s.CountryName, CurrencyCode = s.CurrencyCode, Locale = s.Locale, PriceMultiplier = s.PriceMultiplier,
                    RoundingStep = s.RoundingStep, TaxName = s.TaxName, TaxRate = s.TaxRate, IsDeleted = 0,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (CountryCode, CountryName, CurrencyCode, Locale, PriceMultiplier, RoundingStep, TaxName, TaxRate, IsActive, CreatedBy)
         VALUES (s.CountryCode, s.CountryName, s.CurrencyCode, s.Locale, s.PriceMultiplier, s.RoundingStep, s.TaxName, s.TaxRate, 1, N'seed');

DECLARE @Total int = (SELECT COUNT(*) FROM dbo.CountryPricing WHERE IsActive = 1 AND IsDeleted = 0);
PRINT CONCAT('Country pricing: ', @Total, ' countries with local currency and prices.');

COMMIT TRANSACTION;
GO
