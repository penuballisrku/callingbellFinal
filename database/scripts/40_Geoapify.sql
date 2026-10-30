/* =====================================================================================
   CallInBell - 40_Geoapify.sql
   SubCategories.GeoapifyCategories: the Geoapify place categories (comma-separated) that
   match each sub-category, used by the "AI recommended" search and the OpenStreetMap
   catalogue agent when Geoapify is configured (Geoapify:ApiKey). Sub-categories left
   NULL have no clean Geoapify equivalent and keep using OpenStreetMap's Overpass with
   their OsmTags. Geoapify's category list: https://apidocs.geoapify.com/docs/places/

   Idempotent: only fills rows that are still NULL, so a value changed later (by an
   admin or by hand) is kept.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.SubCategories', 'GeoapifyCategories') IS NULL
    ALTER TABLE dbo.SubCategories ADD GeoapifyCategories nvarchar(400) NULL;
GO

UPDATE s
SET GeoapifyCategories = m.Categories, ModifiedOn = SYSDATETIMEOFFSET(), ModifiedBy = N'script:40_Geoapify'
FROM dbo.SubCategories s
JOIN (VALUES
    (N'gardening',             N'commercial.garden'),
    (N'locksmiths',            N'service.locksmith'),
    (N'doctors',               N'healthcare.clinic_or_praxis'),
    (N'clinics',               N'healthcare.clinic_or_praxis'),
    (N'hospitals',             N'healthcare.hospital'),
    (N'dental',                N'healthcare.dentist'),
    (N'pharmacy',              N'healthcare.pharmacy,commercial.health_and_beauty.pharmacy'),
    (N'eye-care',              N'commercial.health_and_beauty.optician'),
    (N'schools',               N'education.school'),
    (N'music-classes',         N'education.music_school'),
    (N'language-classes',      N'education.language_school'),
    (N'driving-schools',       N'education.driving_school'),
    (N'daycare-creches',       N'childcare.kindergarten'),
    (N'beauty-salons',         N'service.beauty.hairdresser'),
    (N'beauty-services',       N'service.beauty'),
    (N'fitness',               N'sport.fitness'),
    (N'spa',                   N'service.beauty.spa,service.beauty.massage'),
    (N'lawyers',               N'office.lawyer'),
    (N'legal-consultants',     N'office.lawyer'),
    (N'notary-services',       N'office.notary'),
    (N'architects',            N'office.architect'),
    (N'restaurants',           N'catering.restaurant'),
    (N'cafes',                 N'catering.cafe'),
    (N'sweet-shops',           N'commercial.food_and_drink.confectionery,commercial.food_and_drink.bakery'),
    (N'real-estate-agents',    N'office.estate_agent'),
    (N'property-management',   N'office.estate_agent'),
    (N'tours-travels',         N'service.travel_agency,office.travel_agent'),
    (N'car-repair',            N'service.vehicle.repair.car'),
    (N'car-wash',              N'service.vehicle.car_wash'),
    (N'bike-service',          N'service.vehicle.repair.motorcycle'),
    (N'veterinarians',         N'pet.veterinary'),
    (N'pet-shops',             N'pet.shop'),
    (N'pet-grooming',          N'pet.service'),
    (N'chartered-accountants', N'office.accountant'),
    (N'tax-consultants',       N'office.tax_advisor'),
    (N'insurance-advisors',    N'office.insurance'),
    (N'loan-advisors',         N'office.financial,office.financial_advisor'),
    (N'web-developers',        N'office.it'),
    (N'digital-marketing',     N'office.advertising_agency'),
    (N'maids-cooks',           N'office.employment_agency'),
    (N'tailors',               N'service.tailor'),
    (N'laundry',               N'service.cleaning.laundry,service.cleaning.dry_cleaning'),
    (N'hotels',                N'accommodation.hotel'),
    (N'homestays',             N'accommodation.guest_house'),
    (N'service-apartments',    N'accommodation.apartment'),
    (N'swimming-classes',      N'sport.swimming_pool'),
    (N'sports-courts',         N'sport.sports_centre')
) AS m (Slug, Categories) ON m.Slug = s.Slug
WHERE s.GeoapifyCategories IS NULL;
GO
