/* =====================================================================================
   Calling Bell - 18_ExternalSearch.sql
   OpenStreetMap search tags per sub-category (dbo.SubCategories.OsmTags).

   Search results list registered businesses first. Below them the platform shows real places
   near the selected area from OpenStreetMap ("AI Recommended": matched to the search by the
   local AI model) and, when a Google Places API key is configured, from Google Maps.
   These tags tell the OpenStreetMap search which kinds of places belong to a sub-category.

   Format: selectors separated by "|"
     key=value   an OpenStreetMap tag, e.g. craft=electrician, amenity=dentist
     name~words  places whose name contains the words (case-insensitive). Every sub-category
                 has at least one: the fast OpenStreetMap search (Photon) matches names only.
   Only lower-case letters, digits, spaces, "_" and ":" are allowed; the API rejects anything else.

   * Requires 00_Schema.sql (OsmTags column) and 03_Categories.sql (sub-categories).
   * Idempotent: plain UPDATE per slug.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#Osm') IS NOT NULL DROP TABLE #Osm;
CREATE TABLE #Osm (SubSlug nvarchar(140) PRIMARY KEY, OsmTags nvarchar(400) NOT NULL);
INSERT INTO #Osm (SubSlug, OsmTags) VALUES
-- Home services
(N'electrical',            N'craft=electrician|shop=electrical|name~electrician'),
(N'plumbing',              N'craft=plumber|name~plumbing|name~plumber'),
(N'carpenters',            N'craft=carpenter|name~carpenter|name~furniture works'),
(N'painting',              N'craft=painter|name~painting contractor|name~painters'),
(N'ac-repair',             N'craft=hvac|name~air condition|name~ac service|name~ac repair'),
(N'pest-control',          N'craft=pest_control|name~pest control'),
(N'cleaning',              N'craft=cleaning|office=cleaning|name~cleaning services'),
(N'tank-cleaning',         N'name~tank cleaning'),
(N'gardening',             N'shop=garden_centre|craft=gardener|name~nursery garden'),
(N'locksmiths',            N'craft=locksmith|shop=locksmith|name~locksmith|name~key maker'),
(N'inverter-battery',      N'shop=batteries|name~inverter|name~battery'),
(N'glass-aluminium',       N'craft=glaziery|name~glass|name~aluminium'),
-- Healthcare
(N'hospitals',             N'amenity=hospital|name~hospital'),
(N'clinics',               N'amenity=clinic|healthcare=clinic|name~clinic'),
(N'doctors',               N'amenity=doctors|healthcare=doctor|name~clinic|name~doctor'),
(N'dental',                N'amenity=dentist|healthcare=dentist|name~dental|name~dentist'),
(N'diagnostics',           N'healthcare=laboratory|name~diagnostic|name~pathology'),
(N'pharmacy',              N'amenity=pharmacy|shop=chemist|name~pharmacy|name~medical'),
(N'eye-care',              N'shop=optician|healthcare=optometrist|name~eye hospital|name~eye care'),
(N'ayurveda-homeopathy',   N'healthcare=alternative|name~ayurved|name~homoeo|name~homeo'),
(N'ambulance-services',    N'emergency=ambulance_station|name~ambulance'),
(N'blood-banks',           N'healthcare=blood_donation|name~blood bank'),
-- Beauty & wellness
(N'beauty-salons',         N'shop=hairdresser|shop=beauty|name~salon'),
(N'beauty-services',       N'shop=beauty|shop=cosmetics|name~beauty parlour'),
(N'bridal-makeup',         N'name~makeup|name~bridal'),
(N'spa',                   N'leisure=spa|shop=massage|name~spa'),
(N'fitness',               N'leisure=fitness_centre|name~gym|name~fitness'),
(N'yoga',                  N'sport=yoga|name~yoga'),
(N'tattoo-studios',        N'shop=tattoo|name~tattoo'),
(N'nail-art',              N'name~nail'),
(N'physiotherapy',         N'healthcare=physiotherapist|name~physio'),
-- Education
(N'schools',               N'amenity=school|name~school'),
(N'private-tutors',        N'name~tuition|name~tutorial'),
(N'test-preparation',      N'amenity=prep_school|name~coaching|name~academy'),
(N'language-classes',      N'amenity=language_school|name~spoken english|name~language'),
(N'music-classes',         N'amenity=music_school|name~music academy|name~music school'),
(N'dance-classes',         N'amenity=dancing_school|leisure=dance|name~dance'),
(N'abacus-classes',        N'name~abacus'),
(N'skill-training',        N'amenity=training|name~training institute|name~computer institute'),
-- Architecture & interiors
(N'interior-designers',    N'craft=interior_designer|shop=interior_decoration|office=interior_design|name~interior'),
(N'architects',            N'office=architect|name~architect'),
-- Legal
(N'lawyers',               N'office=lawyer|name~advocate|name~law firm'),
(N'notary-services',       N'office=notary|name~notary'),
(N'legal-consultants',     N'office=lawyer|name~legal'),
-- Finance & tax
(N'chartered-accountants', N'office=accountant|name~chartered accountant|name~ca firm'),
(N'tax-consultants',       N'office=tax_advisor|name~tax consultant|name~tax'),
(N'insurance-advisors',    N'office=insurance|name~insurance'),
(N'loan-advisors',         N'office=financial|office=financial_advisor|name~loan|name~finance'),
-- Real estate
(N'real-estate-agents',    N'office=estate_agent|name~real estate|name~realty|name~properties'),
(N'property-management',   N'office=property_management|office=estate_agent|name~property management'),
-- Food & dining
(N'restaurants',           N'amenity=restaurant|name~restaurant'),
(N'cafes',                 N'amenity=cafe|name~cafe'),
(N'sweet-shops',           N'shop=confectionery|shop=pastry|name~sweets|name~sweet house'),
(N'caterers',              N'craft=caterer|name~caterers|name~catering'),
(N'tiffin-services',       N'name~tiffin|name~mess|name~meals'),
-- Events & weddings
(N'photographers',         N'craft=photographer|shop=photo|name~photography|name~studio'),
(N'event-planners',        N'office=event_management|name~event'),
(N'decorators',            N'name~decorat'),
(N'mehendi-artists',       N'name~mehendi|name~mehndi'),
(N'dj-sound',              N'name~dj|name~sound system|name~sounds'),
(N'wedding-cards',         N'name~wedding card|name~cards'),
-- Automotive
(N'car-repair',            N'shop=car_repair|name~car care|name~motors'),
(N'bike-service',          N'shop=motorcycle_repair|shop=motorcycle|name~bike service'),
(N'car-wash',              N'amenity=car_wash|name~car wash'),
(N'tyres',                 N'shop=tyres|name~tyres'),
(N'towing',                N'name~towing|name~crane service'),
(N'driving-schools',       N'amenity=driving_school|name~driving school'),
-- Travel & transport
(N'taxi-services',         N'amenity=taxi|name~cabs|name~taxi'),
(N'tours-travels',         N'shop=travel_agency|office=travel_agent|name~tours|name~travels'),
(N'packers-movers',        N'office=moving_company|name~packers|name~movers'),
(N'bus-hire',              N'name~bus hire|name~travels'),
-- IT & digital
(N'web-developers',        N'office=it|craft=web_design|name~web design|name~software'),
(N'graphic-designers',     N'craft=graphic_design|name~graphic'),
(N'digital-marketing',     N'office=advertising_agency|name~digital marketing|name~digital'),
(N'cctv-installation',     N'shop=security|name~cctv|name~security systems'),
-- Appliance repair
(N'washing-machine-repair',N'craft=electronics_repair|name~washing machine'),
(N'refrigerator-repair',   N'craft=electronics_repair|name~refrigerat|name~fridge'),
(N'ro-purifier-service',   N'name~water purifier|name~purifier|name~aqua'),
(N'laptop-repair',         N'shop=computer|craft=computer_repair|name~laptop|name~computer service'),
(N'mobile-repair',         N'shop=mobile_phone|name~mobile repair|name~mobile service'),
-- Construction & renovation
(N'civil-contractors',     N'craft=builder|office=construction_company|name~constructions|name~builders'),
(N'waterproofing',         N'name~waterproofing'),
(N'flooring',              N'shop=flooring|shop=tiles|name~tiles|name~granite'),
(N'solar-installation',    N'craft=photovoltaic|name~solar'),
(N'fabrication',           N'craft=metal_construction|craft=welder|name~fabrication|name~engineering works'),
-- Pet care
(N'veterinarians',         N'amenity=veterinary|name~veterinary|name~pet clinic'),
(N'pet-grooming',          N'shop=pet_grooming|name~pet grooming'),
(N'pet-boarding',          N'amenity=animal_boarding|name~pet boarding|name~kennel'),
(N'pet-shops',             N'shop=pet|name~pet shop|name~pet store'),
(N'dog-trainers',          N'amenity=animal_training|name~dog training'),
-- Fashion & tailoring
(N'tailors',               N'craft=tailor|shop=tailor|name~tailor'),
(N'boutiques',             N'shop=boutique|name~boutique'),
(N'laundry',               N'shop=laundry|shop=dry_cleaning|name~laundry|name~dry clean'),
-- Hotels & stays
(N'hotels',                N'tourism=hotel|name~hotel'),
(N'homestays',             N'tourism=guest_house|name~homestay'),
(N'service-apartments',    N'tourism=apartment|name~service apartment'),
(N'resorts',               N'leisure=resort|name~resort'),
-- Astrology & pooja
(N'astrologers',           N'shop=psychic|name~astrolog|name~jyothish'),
(N'vastu-consultants',     N'name~vastu'),
(N'pandits',               N'name~pandit|name~purohit'),
-- Sports & hobbies
(N'swimming-classes',      N'leisure=swimming_pool|sport=swimming|name~swimming'),
(N'sports-courts',         N'leisure=sports_centre|sport=badminton|name~sports'),
(N'cricket-academies',     N'sport=cricket|name~cricket'),
(N'martial-arts',          N'sport=martial_arts|sport=karate|sport=taekwondo|name~karate|name~martial'),
(N'chess-classes',         N'name~chess'),
-- Family care
(N'daycare-creches',       N'amenity=childcare|amenity=kindergarten|name~daycare|name~creche'),
(N'elder-care',            N'amenity=social_facility|name~old age|name~senior care'),
(N'home-nursing',          N'healthcare=nurse|name~nursing|name~home care'),
(N'maids-cooks',           N'office=employment_agency|name~maid'),
(N'babysitters',           N'name~babysit|name~nanny'),
-- Business services
(N'printing-press',        N'craft=printer|shop=copyshop|name~printers|name~printing'),
(N'courier-services',      N'office=courier|name~courier|name~logistics'),
(N'packaging-suppliers',   N'name~packaging|name~packers'),
(N'translation-services',  N'office=translator|name~translat'),
(N'scrap-dealers',         N'name~scrap'),
-- Documentation
(N'passport-visa',         N'name~passport|name~visa'),
(N'pan-aadhaar-centres',   N'name~aadhaar|name~aadhar|name~mee seva|name~meeseva'),
(N'rto-services',          N'name~rto|name~driving licence'),
(N'certificate-services',  N'name~mee seva|name~meeseva|name~csc'),
-- Agriculture
(N'seeds-fertilisers',     N'shop=agrarian|name~fertili|name~seeds'),
(N'dairy-suppliers',       N'shop=dairy|name~dairy|name~milk'),
(N'tractor-hire',          N'name~tractor'),
(N'drip-irrigation',       N'name~irrigation|name~drip'),
(N'borewell-drilling',     N'name~borewell|name~bore well');

IF EXISTS (SELECT 1 FROM #Osm o WHERE NOT EXISTS (SELECT 1 FROM dbo.SubCategories sc WHERE sc.Slug = o.SubSlug))
    THROW 50019, 'An OpenStreetMap tag row references a sub-category slug that does not exist. Run 03_Categories.sql first.', 1;
IF EXISTS (SELECT 1 FROM #Osm WHERE OsmTags LIKE N'%[^a-z0-9 _:=~|]%' COLLATE Latin1_General_BIN)
    THROW 50020, 'OpenStreetMap tags may only contain lower-case letters, digits, spaces, "_", ":", "=", "~" and "|".', 1;

UPDATE sc
SET    sc.OsmTags = o.OsmTags
FROM   dbo.SubCategories sc
JOIN   #Osm o ON o.SubSlug = sc.Slug
WHERE  ISNULL(sc.OsmTags, N'') <> o.OsmTags;

DECLARE @Mapped int = (SELECT COUNT(*) FROM #Osm),
        @Unmapped int = (SELECT COUNT(*) FROM dbo.SubCategories WHERE OsmTags IS NULL AND IsActive = 1);
PRINT CONCAT('OpenStreetMap tags: ', @Mapped, ' sub-categories mapped, ', @Unmapped, ' active sub-categories without tags.');

DROP TABLE #Osm;
COMMIT TRANSACTION;
