/* =====================================================================================
   CallInBell - 43_OpenPlaces.sql
   Open Places API (openplacesapi.com): Overture Maps places (Foursquare, Microsoft, Meta and
   OpenStreetMap listings merged), far denser than OpenStreetMap alone in Indian towns. Shown in
   the "AI recommended" search results next to the OpenStreetMap places when OpenPlaces:ApiKey
   is configured.

   * SubCategories.OpenPlacesCategories: the Overture category slugs (comma-separated) that match
     each sub-category; checked against GET https://api.openplacesapi.com/v1/categories. Left
     NULL where Overture has no clean equivalent: those are searched by the sub-category's name.
     Only fills rows that are still NULL, so a value changed later is kept.
   * dbo.OpenPlaces: Overture places. Either a whole country imported from Overture's open data
     with tools/overture/import_places.py (searched locally: no API key or quota), or places the
     API returned, kept so their details and "Join CallInBell" work later (the API has no
     single-place lookup). The data's licences allow storing it. Cell is a 0.1 degree grid cell
     ((floor((lat + 90) * 10) * 10000) + floor((lon + 180) * 10)), so a search near a point reads
     only the cells around it.
   * dbo.OpenPlacesImports: one row per imported country, with the area it covers; searches inside
     it use the local copy, anywhere else the API.

   Idempotent.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.SubCategories', 'OpenPlacesCategories') IS NULL
    ALTER TABLE dbo.SubCategories ADD OpenPlacesCategories nvarchar(400) NULL;
GO

IF OBJECT_ID(N'dbo.OpenPlaces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OpenPlaces
    (
        PlaceId       nvarchar(100)  NOT NULL CONSTRAINT PK_OpenPlaces PRIMARY KEY NONCLUSTERED,
        Name          nvarchar(300)  NOT NULL,
        Category      nvarchar(100)  NULL,
        -- ",slug,slug," so one can be matched with LIKE '%,slug,%'.
        AltCategories nvarchar(400)  NULL,
        CategoryPath  nvarchar(300)  NULL,
        Address       nvarchar(500)  NULL,
        Locality      nvarchar(150)  NULL,
        Region        nvarchar(150)  NULL,
        Postcode      nvarchar(20)   NULL,
        CountryCode   nvarchar(2)    NULL,
        Latitude      decimal(9, 6)  NOT NULL,
        Longitude     decimal(9, 6)  NOT NULL,
        Cell          int            NOT NULL,
        Phone         nvarchar(50)   NULL,
        Website       nvarchar(500)  NULL,
        Confidence    decimal(4, 3)  NULL,
        Datasets      nvarchar(200)  NULL,
        -- The Overture release a bulk import came from; NULL for places kept from API results.
        Release       nvarchar(20)   NULL,
        FirstSeenOn   datetimeoffset NOT NULL,
        LastSeenOn    datetimeoffset NOT NULL
    );
    CREATE CLUSTERED INDEX IX_OpenPlaces_Cell ON dbo.OpenPlaces (Cell) WITH (DATA_COMPRESSION = PAGE);
END
GO

IF OBJECT_ID(N'dbo.OpenPlacesImports', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OpenPlacesImports
    (
        CountryCode nvarchar(2)    NOT NULL CONSTRAINT PK_OpenPlacesImports PRIMARY KEY,
        Release     nvarchar(20)   NOT NULL,
        Places      int            NOT NULL,
        MinLatitude decimal(9, 6)  NOT NULL,
        MaxLatitude decimal(9, 6)  NOT NULL,
        MinLongitude decimal(9, 6) NOT NULL,
        MaxLongitude decimal(9, 6) NOT NULL,
        ImportedOn  datetimeoffset NOT NULL
    );
END
GO

UPDATE s
SET OpenPlacesCategories = m.Categories, ModifiedOn = SYSDATETIMEOFFSET(), ModifiedBy = N'script:43_OpenPlaces'
FROM dbo.SubCategories s
JOIN (VALUES
    -- Agriculture
    (N'borewell-drilling',      N'well_drilling'),
    (N'dairy-suppliers',        N'dairy_store,dairy_farm'),
    (N'drip-irrigation',        N'irrigation_service'),
    (N'seeds-fertilisers',      N'agricultural_seed_store'),
    -- Appliance repair
    (N'laptop-repair',          N'it_service_and_computer_repair'),
    (N'mobile-repair',          N'mobile_phone_repair'),
    (N'refrigerator-repair',    N'appliance_repair_service'),
    (N'ro-purifier-service',    N'water_purification_service'),
    (N'washing-machine-repair', N'appliance_repair_service'),
    -- Architecture & interiors
    (N'architects',             N'architect'),
    (N'interior-designers',     N'interior_design'),
    -- Automotive
    (N'bike-service',           N'motorcycle_repair'),
    (N'car-repair',             N'automotive_repair'),
    (N'car-wash',               N'car_wash'),
    (N'driving-schools',        N'driving_school'),
    (N'towing',                 N'towing_service'),
    (N'tyres',                  N'tire_shop,tire_dealer_and_repair'),
    -- Beauty & wellness
    (N'beauty-salons',          N'beauty_salon,hair_salon'),
    (N'beauty-services',        N'beauty_salon,makeup_artist'),
    (N'bridal-makeup',          N'makeup_artist'),
    (N'fitness',                N'gym,fitness_trainer'),
    (N'nail-art',               N'nail_salon'),
    (N'physiotherapy',          N'physical_therapy'),
    (N'spa',                    N'spa,day_spa'),
    (N'tattoo-studios',         N'tattoo'),
    (N'yoga',                   N'yoga_studio'),
    -- Business services
    (N'courier-services',       N'courier_and_delivery_service'),
    (N'packaging-suppliers',    N'packaging_contractors_and_service'),
    (N'printing-press',         N'printing_service'),
    (N'scrap-dealers',          N'recycling_center'),
    (N'translation-services',   N'translation_service'),
    -- Construction & renovation
    (N'civil-contractors',      N'building_contractor,contractor'),
    (N'fabrication',            N'metal_fabricator,sheet_metal_fabricator'),
    (N'flooring',               N'flooring_store,tile_store'),
    (N'solar-installation',     N'solar_installation'),
    (N'waterproofing',          N'waterproofing'),
    -- Documentation
    (N'passport-visa',          N'passport_and_visa_service'),
    -- Education
    (N'dance-classes',          N'dance_studio'),
    (N'language-classes',       N'language_school'),
    (N'music-classes',          N'music_school'),
    (N'private-tutors',         N'private_tutor,tutoring_service'),
    (N'schools',                N'school'),
    (N'skill-training',         N'vocational_and_technical_school'),
    (N'test-preparation',       N'test_preparation,computer_coaching'),
    -- Events & weddings
    (N'dj-sound',               N'dj_service'),
    (N'event-planners',         N'party_and_event_planning'),
    (N'photographers',          N'photography_service'),
    (N'wedding-cards',          N'cards_and_stationery_store'),
    -- Family care
    (N'babysitters',            N'nanny_service'),
    (N'daycare-creches',        N'child_care_and_day_care,day_care_preschool'),
    (N'elder-care',             N'home_health_care,retirement_home'),
    (N'home-nursing',           N'home_health_care,nursing'),
    -- Fashion & tailoring
    (N'boutiques',              N'fashion_boutique,designer_clothing'),
    (N'laundry',                N'laundry_service,dry_cleaning'),
    (N'tailors',                N'tailor'),
    -- Finance & tax
    (N'chartered-accountants',  N'accountant'),
    (N'insurance-advisors',     N'insurance_agency'),
    (N'tax-consultants',        N'tax_service'),
    -- Food & dining
    (N'cafes',                  N'cafe,bakery'),
    (N'caterers',               N'caterer'),
    (N'restaurants',            N'restaurant'),
    (N'sweet-shops',            N'indian_sweets_shop'),
    -- Healthcare
    (N'ambulance-services',     N'ambulance_or_ems_service'),
    (N'ayurveda-homeopathy',    N'ayurveda'),
    (N'blood-banks',            N'blood_and_plasma_donation_center'),
    (N'clinics',                N'doctors_office,outpatient_care_facility'),
    (N'dental',                 N'dental_clinic'),
    (N'diagnostics',            N'diagnostics_imaging_or_lab_service'),
    (N'doctors',                N'doctors_office'),
    (N'eye-care',               N'vision_or_eye_care_clinic'),
    (N'hospitals',              N'hospital'),
    (N'pharmacy',               N'pharmacy'),
    -- Home services
    (N'ac-repair',              N'hvac_service'),
    (N'carpenters',             N'carpenter'),
    (N'cleaning',               N'home_cleaning,cleaning_service'),
    (N'electrical',             N'electrician'),
    (N'gardening',              N'landscaping'),
    (N'glass-aluminium',        N'glass_and_mirror_sales_service,aluminum_supplier'),
    (N'inverter-battery',       N'battery_inverter_supplier,battery_store'),
    (N'locksmiths',             N'key_and_locksmith'),
    (N'painting',               N'painting'),
    (N'pest-control',           N'pest_control_service'),
    (N'plumbing',               N'plumbing'),
    -- Hotels & stays
    (N'homestays',              N'guest_house,bed_and_breakfast'),
    (N'hotels',                 N'hotel'),
    (N'resorts',                N'resort'),
    (N'service-apartments',     N'service_apartment'),
    -- IT & digital
    (N'cctv-installation',      N'security_systems'),
    (N'digital-marketing',      N'marketing_agency'),
    (N'graphic-designers',      N'graphic_designer'),
    (N'web-developers',         N'web_designer'),
    -- Legal
    (N'lawyers',                N'attorney_or_law_firm'),
    (N'legal-consultants',      N'legal_service'),
    (N'notary-services',        N'notary_public'),
    -- Pet care
    (N'dog-trainers',           N'dog_trainer'),
    (N'pet-boarding',           N'pet_boarding'),
    (N'pet-grooming',           N'pet_groomer'),
    (N'pet-shops',              N'pet_store'),
    (N'veterinarians',          N'veterinarian'),
    -- Real estate
    (N'property-management',    N'property_management'),
    (N'real-estate-agents',     N'real_estate_agent,real_estate_service'),
    -- Sports & hobbies
    (N'martial-arts',           N'martial_arts_club'),
    (N'sports-courts',          N'badminton_court'),
    (N'swimming-classes',       N'swimming_instructor,swimming_pool'),
    -- Travel & transport
    (N'bus-hire',               N'bus_rental'),
    (N'packers-movers',         N'mover'),
    (N'taxi-services',          N'taxi_service,taxi_or_ride_share_service'),
    (N'tours-travels',          N'tour_operator,travel_service')
) AS m (Slug, Categories) ON m.Slug = s.Slug
WHERE s.OpenPlacesCategories IS NULL;
GO
