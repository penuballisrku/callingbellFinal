/* =====================================================================================
   Calling Bell - 04_Businesses.sql
   Business owners, business listings, working hours, logos, covers and gallery images.

   * Owner accounts are created here (business onboarding) with role BusinessOwner;
     individual practitioners additionally receive the ServiceProvider role.
   * Contact details use the demo.callingbell.in domain and generated phone numbers.
   * Idempotent: MERGE on Businesses.Slug / owner e-mail / (BusinessId, DayOfWeek) / media file.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @PasswordHash nvarchar(max) = N'AQAAAAIAAYagAAAAEDDVMKoMpeUr6i+eSo4QvV1QWOKIXPq1NU3Vg7tUwtUfZEweayOOe9GLHR3gEPK9ew==';
DECLARE @Now datetimeoffset = SYSDATETIMEOFFSET();

IF OBJECT_ID('tempdb..#B') IS NOT NULL DROP TABLE #B;
CREATE TABLE #B (
    Slug nvarchar(180) PRIMARY KEY, Name nvarchar(160), Initials nvarchar(3), SubSlug nvarchar(140), CitySlug nvarchar(120), AreaSlug nvarchar(140),
    OwnerName nvarchar(150), OwnerEmail nvarchar(100), Tagline nvarchar(200), Description nvarchar(1000), AddressLine nvarchar(300), Landmark nvarchar(150),
    YearEstablished int, TeamSize int, Status nvarchar(32), Verification nvarchar(32), IsFeatured bit, Availability nvarchar(40),
    AcceptsBooking bit, Video bit, HomeService bit, DaysAgo int);

INSERT INTO #B VALUES
-- ===== Hyderabad =====
(N'sparkline-electricals', N'Sparkline Electricals', N'SE', N'electrical', N'hyderabad', N'madhapur', N'Venkatesh Goud', N'venkatesh.goud',
 N'Licensed electricians for homes & offices', N'Government-licensed electrical contractors handling wiring, MCB and DB upgrades, inverter installation and emergency repairs across Madhapur, Kondapur and Hitech City.',
 N'Plot 42, Ayyappa Society Main Road', N'Near Madhapur Metro Station', 2012, 14, N'Active', N'Verified', 1, N'Online', 1, 0, 1, 410),
(N'aquafix-plumbing-services', N'AquaFix Plumbing Services', N'AP', N'plumbing', N'hyderabad', N'kukatpally', N'Srinivas Reddy Patlolla', N'srinivas.patlolla',
 N'Leak-free homes, guaranteed', N'Same-day plumbing for leaking taps, concealed pipeline repairs, bathroom fittings and overhead tank cleaning, backed by a 30-day workmanship warranty.',
 N'H.No 5-36/2, KPHB Phase 3', N'Opp. JNTU Gate', 2015, 9, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 380),
(N'coolbreeze-ac-care', N'CoolBreeze AC Care', N'CB', N'ac-repair', N'hyderabad', N'gachibowli', N'Mohammed Imran Qureshi', N'imran.qureshi',
 N'Split, window & cassette AC experts', N'Multi-brand AC servicing for Daikin, Voltas, LG and Blue Star - gas refilling, PCB repairs, installation and annual maintenance contracts.',
 N'Shop 7, DLF Road, Gachibowli', N'Beside Indian Oil Petrol Bunk', 2016, 18, N'Active', N'Verified', 1, N'Online', 1, 0, 1, 350),
(N'dr-anitha-rao-family-clinic', N'Dr. Anitha Rao Family Clinic', N'AR', N'doctors', N'hyderabad', N'banjara-hills', N'Dr. Anitha Rao', N'anitha.rao',
 N'MBBS, MD (General Medicine)', N'Family physician with 18 years of experience in diabetes, hypertension and thyroid management. Walk-in consultations, teleconsultations and home visits for senior citizens.',
 N'Road No. 12, Banjara Hills', N'Near Care Hospital Out-Patient Block', 2008, 6, N'Active', N'Verified', 1, N'AvailableForVideo', 1, 1, 1, 420),
(N'sunrise-multispeciality-hospital', N'Sunrise Multispeciality Hospital', N'SH', N'hospitals', N'hyderabad', N'secunderabad', N'Dr. Prakash Chandra Varma', N'prakash.varma',
 N'24x7 emergency & 150-bed care', N'NABH-accredited 150-bed hospital with 24x7 emergency, cardiology, orthopaedics, maternity and an in-house diagnostic wing. Cashless with all major insurers.',
 N'1-8-31, SP Road, Secunderabad', N'Near Paradise Circle', 1998, 420, N'Active', N'Verified', 1, N'Online', 1, 1, 0, 430),
(N'smilecraft-dental-studio', N'SmileCraft Dental Studio', N'SC', N'dental', N'hyderabad', N'jubilee-hills', N'Dr. Keerthana Reddy', N'keerthana.reddy',
 N'Painless dentistry & clear aligners', N'Digital dentistry studio offering root canal treatment, implants, clear aligners and smile design with same-day crowns.',
 N'Road No. 36, Jubilee Hills', N'Above Ratnadeep Supermarket', 2014, 11, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 1, 0, 300),
(N'brightminds-home-tutors', N'BrightMinds Home Tutors', N'BM', N'private-tutors', N'hyderabad', N'kondapur', N'Lakshmi Priya Nandula', N'lakshmipriya.nandula',
 N'CBSE, ICSE & IB tutors at home', N'Verified subject tutors for Classes 1-12 across CBSE, ICSE, IB and State Board, with monthly progress reports and online doubt-clearing sessions.',
 N'Flat 302, Botanical Garden Road', N'Near Sarath City Capital Mall', 2017, 35, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 1, 260),
(N'glow-and-grace-salon', N'Glow & Grace Unisex Salon', N'GG', N'beauty-salons', N'hyderabad', N'kondapur', N'Swathi Kandukuri', N'swathi.kandukuri',
 N'Hair, skin & bridal studio', N'Unisex salon specialising in hair colouring, keratin treatments, facials and bridal makeup, using L''Oréal Professionnel and O3+ products.',
 N'2nd Floor, Kothaguda Cross Road', N'Near Kondapur Bus Depot', 2018, 12, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 240),
(N'fitzone-personal-training', N'FitZone Personal Training', N'FZ', N'fitness', N'hyderabad', N'gachibowli', N'Rahul Varma Datla', N'rahul.datla',
 N'ACE-certified personal trainers', N'One-on-one strength, fat-loss and post-natal training at home, in community gyms or online, with nutrition plans by a registered dietitian.',
 N'Telecom Nagar Extension', N'Opp. ISB Main Gate', 2019, 8, N'Active', N'Verified', 0, N'AvailableForVideo', 1, 1, 1, 200),
(N'rao-and-associates-advocates', N'Rao & Associates, Advocates', N'RA', N'lawyers', N'hyderabad', N'ameerpet', N'Adv. Suresh Kumar Rao', N'suresh.rao',
 N'Civil, property & family law', N'Practising before the Telangana High Court and City Civil Courts in property disputes, partition suits, divorce and RERA matters.',
 N'7-1-58, Dharam Karan Road, Ameerpet', N'Near Ameerpet Metro Interchange', 2003, 7, N'Active', N'Verified', 1, N'AvailableForCall', 1, 1, 0, 400),
(N'paradise-spice-kitchen', N'Paradise Spice Kitchen', N'PS', N'restaurants', N'hyderabad', N'banjara-hills', N'Syed Abdul Rahman', N'abdul.rahman',
 N'Hyderabadi biryani & Mughlai', N'Dum biryani, haleem in season and slow-cooked Mughlai curries, with family dining for 120 guests and a party hall for celebrations.',
 N'Road No. 1, Banjara Hills', N'Opp. GVK One Mall', 2011, 45, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 330),
(N'studio-axis-architects', N'Studio Axis Architects', N'SA', N'architects', N'hyderabad', N'jubilee-hills', N'Ar. Nikhil Chakravarthy', N'nikhil.chakravarthy',
 N'Residential & commercial architecture', N'Practice designing villas, gated communities and office campuses, from GHMC approvals and structural coordination to turnkey execution.',
 N'Road No. 45, Jubilee Hills', N'Near Peddamma Temple', 2010, 22, N'Active', N'Verified', 1, N'AvailableForVideo', 1, 1, 0, 390),
(N'nestcraft-interiors', N'NestCraft Interiors', N'NC', N'interior-designers', N'hyderabad', N'kondapur', N'Divya Teja Bommakanti', N'divyateja.b',
 N'Modular kitchens & full-home interiors', N'End-to-end interiors for 2 and 3 BHK apartments - modular kitchens, wardrobes and false ceilings - delivered in 45 days with a 10-year warranty.',
 N'Plot 18, Raghavendra Colony', N'Near Kondapur Main Road', 2016, 26, N'Active', N'Verified', 1, N'AvailableForBooking', 1, 1, 1, 310),
(N'swift-city-cabs', N'Swift City Cabs', N'SW', N'taxi-services', N'hyderabad', N'ameerpet', N'Ramesh Babu Yadav', N'rameshbabu.yadav',
 N'Airport transfers & outstation trips', N'Sedan, SUV and Tempo Traveller rentals for RGIA airport transfers, local packages and outstation trips to Srisailam, Warangal and Vijayawada.',
 N'Shop 4, Mythrivanam Complex', N'Ameerpet X Roads', 2013, 60, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 360),
(N'shubh-utsav-events', N'Shubh Utsav Events', N'SU', N'event-planners', N'hyderabad', N'secunderabad', N'Kavitha Agarwal', N'kavitha.agarwal',
 N'Weddings, sangeets & corporate events', N'Full-service planners for weddings, sangeet nights, naming ceremonies and corporate offsites, with in-house décor and trusted catering partners.',
 N'3rd Floor, Minerva Complex, SD Road', N'Near Secunderabad Clock Tower', 2009, 30, N'Active', N'Verified', 0, N'AvailableForCall', 1, 1, 0, 340),
(N'homekey-realty', N'HomeKey Realty', N'HK', N'real-estate-agents', N'hyderabad', N'gachibowli', N'Arjun Reddy Mettu', N'arjun.mettu',
 N'RERA-registered property advisors', N'Buy, sell and rent apartments and villas in the western corridor - Gachibowli, Narsingi, Kokapet and Tellapur - with legal due-diligence support.',
 N'Unit 5, Sai Prithvi Arcade', N'Near Gachibowli Flyover', 2014, 15, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 0, 280),
(N'yogshala-wellness-studio', N'Yogshala Wellness Studio', N'YW', N'yoga', N'hyderabad', N'madhapur', N'Meenakshi Sundaram', N'meenakshi.sundaram',
 N'Hatha, Vinyasa & therapeutic yoga', N'Small-batch Hatha and Vinyasa classes, therapeutic yoga for back pain and PCOS, and live online sessions for working professionals.',
 N'1st Floor, Kavuri Hills Phase 2', N'Near Madhapur Police Station', 2015, 6, N'Active', N'Verified', 0, N'Online', 1, 1, 0, 230),
(N'urbanglow-at-home-beauty', N'UrbanGlow At-Home Beauty', N'UG', N'beauty-services', N'hyderabad', N'kukatpally', N'Sneha Vemuri', N'sneha.vemuri',
 N'Salon services at your doorstep', N'Certified beauticians for waxing, facials, manicure-pedicure and party makeup at home, using single-use hygiene kits.',
 N'15-31, KPHB Road No. 4', N'Near Forum Sujana Mall', 2020, 24, N'PendingApproval', N'Pending', 0, N'Offline', 1, 0, 1, 12),
(N'bloomfield-montessori-house', N'Bloomfield Montessori House', N'BF', N'schools', N'hyderabad', N'kondapur', N'Aparna Raghunath', N'aparna.raghunath',
 N'Montessori pre-primary & primary', N'AMI-trained teachers, a 1:8 teacher-student ratio and an activity-led curriculum for children aged 2 to 10.',
 N'Masjid Banda Road, Kondapur', N'Near Botanical Garden', 2016, 28, N'Active', N'Verified', 0, N'Online', 0, 0, 0, 300),
(N'vishwakarma-wood-works', N'Vishwakarma Wood Works', N'VW', N'carpenters', N'hyderabad', N'ameerpet', N'Narsimha Chary', N'narsimha.chary',
 N'Custom furniture & repairs', N'Custom wardrobes, bed frames, door repairs and furniture polishing by third-generation carpenters.',
 N'SR Nagar Main Road', N'Near SR Nagar Bus Stop', 1999, 10, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 330),
-- ===== Bengaluru =====
(N'voltcare-electrical-solutions', N'VoltCare Electrical Solutions', N'VC', N'electrical', N'bengaluru', N'hsr-layout', N'Manjunath Gowda', N'manjunath.gowda',
 N'60-minute electrician visits', N'BESCOM-licensed electricians for rewiring, smart switch installation, EV charger fitting and emergency fault repairs in South Bengaluru.',
 N'27th Main, Sector 2, HSR Layout', N'Near BDA Complex', 2014, 16, N'Active', N'Verified', 1, N'Online', 1, 0, 1, 370),
(N'pipeline-pros-plumbing', N'PipeLine Pros Plumbing', N'PP', N'plumbing', N'bengaluru', N'whitefield', N'Shivakumar Hegde', N'shivakumar.hegde',
 N'Plumbing for apartments & villas', N'Leak detection with thermal cameras, CPVC repiping, borewell motor repairs and bathroom renovations for Whitefield and Marathahalli.',
 N'ITPL Main Road, Hoodi', N'Near Phoenix Marketcity', 2016, 12, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 320),
(N'arctic-air-services', N'Arctic Air Services', N'AA', N'ac-repair', N'bengaluru', N'koramangala', N'Naveen Kumar Shetty', N'naveen.shetty',
 N'AC repair & gas top-up in 2 hours', N'Multi-brand AC servicing, deep jet cleaning, compressor replacement and new AC installation with transparent rate cards.',
 N'80 Feet Road, 4th Block, Koramangala', N'Near Sony World Signal', 2012, 20, N'Active', N'Verified', 0, N'Busy', 1, 0, 1, 300),
(N'dr-ramesh-iyer-diabetes-care', N'Dr. Ramesh Iyer Diabetes Care', N'RI', N'doctors', N'bengaluru', N'jayanagar', N'Dr. Ramesh Iyer', N'ramesh.iyer',
 N'Diabetologist & endocrinologist', N'Consultant diabetologist (MD, DM Endocrinology) offering diabetes reversal programmes, thyroid care and continuous glucose monitoring.',
 N'11th Main, 4th Block, Jayanagar', N'Near Jayanagar Shopping Complex', 2006, 9, N'Active', N'Verified', 1, N'AvailableForVideo', 1, 1, 0, 410),
(N'healthfirst-clinic', N'HealthFirst Clinic', N'HF', N'clinics', N'bengaluru', N'indiranagar', N'Dr. Farah Siddiqui', N'farah.siddiqui',
 N'General & paediatric OPD, 8am-10pm', N'Neighbourhood clinic with general physicians, paediatricians, vaccinations, minor procedures and an in-house sample collection centre.',
 N'12th Main, HAL 2nd Stage, Indiranagar', N'Near ESI Hospital', 2017, 14, N'Active', N'Verified', 0, N'Online', 1, 1, 0, 290),
(N'precision-diagnostics', N'Precision Diagnostics', N'PD', N'diagnostics', N'bengaluru', N'malleshwaram', N'Dr. Harish Kulkarni', N'harish.kulkarni',
 N'NABL-accredited lab & imaging', N'Full-body health packages, MRI, CT, ultrasound and home sample collection with digital reports in 6 hours.',
 N'8th Cross, Sampige Road, Malleshwaram', N'Near Mantri Square Mall', 2009, 48, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 0, 1, 380),
(N'the-scholars-den', N'The Scholars Den', N'SD', N'test-preparation', N'bengaluru', N'jayanagar', N'Prof. Raghavendra Bhat', N'raghavendra.bhat',
 N'JEE, NEET & KCET coaching', N'Classroom and hybrid coaching for JEE Main/Advanced, NEET and KCET with weekly mock tests and IIT-alumni faculty.',
 N'9th Block, 30th Cross, Jayanagar', N'Near Ragigudda Temple', 2005, 40, N'Active', N'Verified', 1, N'AvailableForChat', 1, 1, 0, 420),
(N'codecraft-academy', N'CodeCraft Academy', N'CA', N'skill-training', N'bengaluru', N'electronic-city', N'Ankit Malhotra', N'ankit.malhotra',
 N'Full-stack & data science bootcamps', N'Weekend and evening bootcamps in full-stack development, data science and cloud, with live projects and placement assistance.',
 N'Phase 1, Neeladri Road', N'Near Infosys Gate 1', 2018, 25, N'Active', N'Verified', 0, N'Online', 1, 1, 0, 250),
(N'tress-and-tones-salon', N'Tress & Tones Salon', N'TT', N'beauty-salons', N'bengaluru', N'indiranagar', N'Ritu Nair', N'ritu.nair',
 N'Premium unisex salon', N'Precision haircuts, balayage, Olaplex treatments and grooming for men and women by internationally trained stylists.',
 N'100 Feet Road, Indiranagar', N'Near Indiranagar Metro Station', 2015, 16, N'Active', N'Verified', 1, N'AvailableForBooking', 1, 0, 0, 330),
(N'iron-temple-fitness-coaching', N'Iron Temple Fitness Coaching', N'IT', N'fitness', N'bengaluru', N'koramangala', N'Vikram Shenoy', N'vikram.shenoy',
 N'Strength & conditioning coaches', N'Certified strength coaches for powerlifting, mobility and marathon preparation, with in-gym and at-home sessions.',
 N'5th Block, Koramangala', N'Near Forum Mall', 2016, 10, N'Active', N'Verified', 0, N'Online', 1, 1, 1, 270),
(N'kini-and-partners-law-chambers', N'Kini & Partners Law Chambers', N'KP', N'lawyers', N'bengaluru', N'malleshwaram', N'Adv. Prakash Kini', N'prakash.kini',
 N'Corporate, startup & IP law', N'Advisory for startups on incorporation, ESOPs, trademarks and contracts, plus litigation before the Karnataka High Court.',
 N'15th Cross, Margosa Road, Malleshwaram', N'Near Malleshwaram Ground', 2007, 12, N'Active', N'Verified', 0, N'AvailableForCall', 1, 1, 0, 390),
(N'the-filter-coffee-house', N'The Filter Coffee House', N'FC', N'cafes', N'bengaluru', N'jayanagar', N'Girish Adiga', N'girish.adiga',
 N'South Indian breakfast & coffee', N'Old-Bengaluru style darshini serving benne dosa, idli-vada and decoction filter coffee since 1994.',
 N'4th T Block, 30th Cross', N'Near Jayanagar 4th Block Bus Stop', 1994, 18, N'Active', N'Verified', 0, N'Online', 0, 0, 0, 300),
(N'brickline-architects', N'Brickline Architects', N'BA', N'architects', N'bengaluru', N'whitefield', N'Ar. Sanjana Prabhu', N'sanjana.prabhu',
 N'Sustainable architecture studio', N'IGBC-accredited architects designing energy-efficient homes, tech offices and schools with exposed brick and passive cooling.',
 N'EPIP Zone, Whitefield', N'Near Brigade Tech Park', 2011, 18, N'Active', N'Verified', 0, N'AvailableForVideo', 1, 1, 0, 360),
(N'woodnest-interiors', N'WoodNest Interiors', N'WN', N'interior-designers', N'bengaluru', N'hsr-layout', N'Karthik Ramaswamy', N'karthik.ramaswamy',
 N'Interiors for modern apartments', N'Space-saving modular furniture, kitchens and home-office set-ups, with 3D walkthroughs before execution.',
 N'19th Main, Sector 3, HSR Layout', N'Near Agara Lake', 2019, 20, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 1, 1, 220),
(N'namma-ride-cabs', N'Namma Ride Cabs', N'NR', N'taxi-services', N'bengaluru', N'electronic-city', N'Basavaraj Patil', N'basavaraj.patil',
 N'KIA airport cabs at fixed fares', N'Fixed-fare transfers to Kempegowda International Airport, corporate employee transport and weekend getaways to Mysuru and Coorg.',
 N'Hosur Road, Electronic City Phase 2', N'Near Electronic City Toll Plaza', 2014, 85, N'Active', N'Verified', 1, N'Online', 1, 0, 0, 350),
(N'sparkle-home-cleaning', N'Sparkle Home Cleaning', N'SP', N'cleaning', N'bengaluru', N'whitefield', N'Anitha Thomas', N'anitha.thomas',
 N'Deep cleaning & sanitisation', N'Move-in deep cleaning, sofa and carpet shampooing, kitchen degreasing and bathroom descaling by trained, background-verified staff.',
 N'Varthur Road, Whitefield', N'Near Forum Shantiniketan', 2018, 30, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 0, 1, 210),
(N'phoolwari-floral-decorators', N'Phoolwari Floral Decorators', N'PF', N'decorators', N'bengaluru', N'jayanagar', N'Lakshmi Narasimhan', N'lakshmi.narasimhan',
 N'Floral, stage & mandap décor', N'Fresh-flower mandap décor, stage backdrops, balloon décor and themed birthday set-ups across Bengaluru.',
 N'3rd Block, Jayanagar', N'Near Ashoka Pillar', 2011, 20, N'Active', N'Verified', 0, N'AvailableForChat', 1, 0, 1, 280),
(N'trustnest-property-management', N'TrustNest Property Management', N'TN', N'property-management', N'bengaluru', N'koramangala', N'Shalini Menon', N'shalini.menon',
 N'Rental management for NRIs', N'Tenant screening, rent collection, maintenance and legal paperwork for landlords and NRI property owners.',
 N'1st Block, Koramangala', N'Near Wipro Park', 2015, 14, N'Active', N'Verified', 0, N'Online', 1, 1, 0, 260),
-- ===== Mumbai =====
(N'powerhouse-electricians', N'PowerHouse Electricians', N'PH', N'electrical', N'mumbai', N'andheri-west', N'Sachin Patil', N'sachin.patil',
 N'24x7 emergency electricians', N'Licensed electricians for short-circuit repairs, society wiring audits, DB upgrades and decorative lighting across the Western suburbs.',
 N'Shop 3, Lokhandwala Back Road', N'Near Infinity Mall', 2010, 15, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 400),
(N'bandra-smile-dental-clinic', N'Bandra Smile Dental Clinic', N'BS', N'dental', N'mumbai', N'bandra-west', N'Dr. Zara Merchant', N'zara.merchant',
 N'Cosmetic & implant dentistry', N'Cosmetic dentistry, implants, veneers and child-friendly dental care in a modern clinic on Hill Road.',
 N'2nd Floor, Hill Road, Bandra West', N'Near Mehboob Studio', 2012, 10, N'Active', N'Verified', 1, N'AvailableForBooking', 1, 1, 0, 370),
(N'lakeview-hospital-powai', N'Lakeview Hospital', N'LH', N'hospitals', N'mumbai', N'powai', N'Dr. Anand Deshmukh', N'anand.deshmukh',
 N'200-bed multispeciality hospital', N'Tertiary-care hospital with cardiac sciences, neurosurgery, a 24x7 trauma centre and cashless tie-ups with major insurers.',
 N'Central Avenue, Powai', N'Near Powai Lake', 2004, 650, N'Active', N'Verified', 1, N'Online', 1, 1, 0, 430),
(N'dr-sneha-kulkarni-skin-clinic', N'Dr. Sneha Kulkarni Skin Clinic', N'SK', N'doctors', N'mumbai', N'dadar-west', N'Dr. Sneha Kulkarni', N'sneha.kulkarni',
 N'Dermatologist & cosmetologist', N'MD Dermatology treating acne, pigmentation, hair loss and eczema, with laser and chemical-peel procedures.',
 N'Ranade Road, Dadar West', N'Near Dadar Station West', 2010, 7, N'Active', N'Verified', 0, N'AvailableForVideo', 1, 1, 0, 340),
(N'mehta-legal-consultants', N'Mehta Legal Consultants', N'ML', N'legal-consultants', N'mumbai', N'andheri-west', N'Rohan Mehta', N'rohan.mehta',
 N'GST, company law & contracts', N'Legal and compliance advisory for SMEs - GST disputes, company law filings, employment contracts and trademark registration.',
 N'Veera Desai Road, Andheri West', N'Near Andheri Sports Complex', 2013, 9, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 0, 310),
(N'coastal-curry-house', N'Coastal Curry House', N'CC', N'restaurants', N'mumbai', N'dadar-west', N'Prashant Shetty', N'prashant.shetty',
 N'Malvani & Mangalorean seafood', N'Bombil fry, surmai thali and Mangalorean prawn ghee roast cooked with coastal masalas ground in-house every morning.',
 N'Gokhale Road, Dadar West', N'Near Portuguese Church', 2008, 28, N'Active', N'Verified', 1, N'Online', 1, 0, 0, 380),
(N'framestory-photography', N'FrameStory Photography', N'FS', N'photographers', N'mumbai', N'bandra-west', N'Aditya Khanna', N'aditya.khanna',
 N'Candid wedding photography', N'Candid wedding photography, pre-wedding shoots and cinematic films across India, with albums delivered in 30 days.',
 N'Pali Hill, Bandra West', N'Near Pali Naka', 2014, 12, N'Active', N'Verified', 0, N'AvailableForCall', 1, 1, 0, 290),
(N'skyline-property-advisors', N'Skyline Property Advisors', N'SL', N'real-estate-agents', N'mumbai', N'powai', N'Neha Agarwal', N'neha.agarwal',
 N'Rentals & resale in Powai', N'Specialists in Powai and Chandivali rentals, resale flats and NRI property management with verified listings.',
 N'Orchard Avenue, Powai', N'Near Galleria Shopping Centre', 2012, 11, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 320),
(N'kinetic-physio-and-rehab', N'Kinetic Physio & Rehab', N'KR', N'physiotherapy', N'mumbai', N'malad-west', N'Dr. Pooja Bhatia', N'pooja.bhatia',
 N'Sports & orthopaedic physiotherapy', N'Physiotherapy for back pain, sports injuries, post-surgery rehab and geriatric mobility - at the clinic or at home.',
 N'Marve Road, Malad West', N'Near Infiniti Mall Malad', 2016, 8, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 1, 1, 260),
(N'gati-shift-packers-movers', N'Gati Shift Packers & Movers', N'GS', N'packers-movers', N'mumbai', N'chembur', N'Imtiaz Shaikh', N'imtiaz.shaikh',
 N'Household & office relocation', N'Local and intercity relocation with multi-layer packing, GPS-tracked vehicles and transit insurance.',
 N'Sion-Trombay Road, Chembur', N'Near Diamond Garden', 2009, 55, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 300),
(N'glamup-bridal-studio', N'GlamUp Bridal Studio', N'GU', N'beauty-services', N'mumbai', N'malad-west', N'Farheen Ansari', N'farheen.ansari',
 N'Bridal & party makeup artists', N'HD and airbrush bridal makeup, hairstyling and saree draping at your venue, with trial sessions.',
 N'Evershine Nagar, Malad West', N'Near Malad Bus Depot', 2017, 9, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 1, 240),
(N'chembur-car-care', N'Chembur Car Care', N'CR', N'car-repair', N'mumbai', N'chembur', N'Joseph D''Souza', N'joseph.dsouza',
 N'Multi-brand car service centre', N'Periodic servicing, denting-painting, clutch overhauls and insurance claims for Maruti, Hyundai, Honda and Tata cars.',
 N'RC Marg, Chembur', N'Near Chembur Station', 2006, 22, N'Suspended', N'Verified', 0, N'Offline', 0, 0, 0, 400),
-- ===== Pune =====
(N'shree-ganesh-electricals', N'Shree Ganesh Electricals', N'SG', N'electrical', N'pune', N'kothrud', N'Dattatray Joshi', N'dattatray.joshi',
 N'Trusted electricians since 2001', N'Residential electrical works, inverter and solar panel installation, and annual maintenance for housing societies in Kothrud and Karve Nagar.',
 N'Paud Road, Kothrud', N'Near Kothrud Depot', 2001, 10, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 380),
(N'baner-family-dental-care', N'Baner Family Dental Care', N'BD', N'dental', N'pune', N'baner', N'Dr. Aditi Deshpande', N'aditi.deshpande',
 N'Gentle dental care for families', N'Root canals, braces, implants and paediatric dentistry with strict sterilisation protocols and EMI options.',
 N'Baner Road', N'Near Balewadi High Street', 2015, 8, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 1, 0, 300),
(N'vidya-home-tutors', N'Vidya Home Tutors', N'VH', N'private-tutors', N'pune', N'aundh', N'Sunita Kulkarni', N'sunita.kulkarni',
 N'Maths & Science specialists', N'Experienced home and online tutors for Maths, Science and English for Classes 6-12, including Olympiad preparation.',
 N'DP Road, Aundh', N'Near Aundh ITI', 2016, 22, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 1, 250),
(N'coolpoint-ac-and-refrigeration', N'CoolPoint AC & Refrigeration', N'CP', N'ac-repair', N'pune', N'viman-nagar', N'Amol Pawar', N'amol.pawar',
 N'AC, fridge & washing machine repairs', N'Same-day repair of air conditioners, refrigerators and washing machines with genuine spares and a 90-day service warranty.',
 N'Nagar Road, Viman Nagar', N'Near Phoenix Marketcity Pune', 2013, 14, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 330),
(N'carewell-clinic-hinjewadi', N'CareWell Clinic', N'CW', N'clinics', N'pune', N'hinjewadi', N'Dr. Nitin Gaikwad', N'nitin.gaikwad',
 N'Corporate health checks & OPD', N'Multi-speciality OPD, corporate health check-ups and occupational health services for the Hinjewadi IT park.',
 N'Phase 1, Hinjewadi', N'Near Wipro Circle', 2018, 12, N'Active', N'Verified', 0, N'Online', 1, 1, 0, 210),
(N'design-collective-architects', N'Design Collective Architects', N'DC', N'architects', N'pune', N'baner', N'Ar. Rohit Sathe', N'rohit.sathe',
 N'Contemporary homes & bungalows', N'Contemporary bungalows, row houses and commercial facades with PMC approvals and site supervision.',
 N'Pancard Club Road, Baner', N'Near Baner Hill', 2012, 14, N'Active', N'Verified', 0, N'AvailableForVideo', 1, 1, 0, 340),
(N'sahyadri-tours-and-travels', N'Sahyadri Tours & Travels', N'ST', N'tours-travels', N'pune', N'kothrud', N'Ganesh Bhosale', N'ganesh.bhosale',
 N'Weekend treks & holiday packages', N'Weekend treks in the Sahyadris, Konkan holiday packages and pilgrimage tours to Shirdi and Ashtavinayak in AC coaches.',
 N'Karve Road, Kothrud', N'Near Nal Stop', 2011, 18, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 0, 300),
(N'prana-yoga-studio', N'Prana Yoga Studio', N'PY', N'yoga', N'pune', N'aundh', N'Anjali Bhagwat', N'anjali.bhagwat',
 N'Alignment-based yoga', N'Alignment-focused yoga with props, prenatal yoga and pranayama workshops for all age groups.',
 N'Bremen Chowk, Aundh', N'Near Westend Mall', 2014, 5, N'Inactive', N'Verified', 0, N'Offline', 0, 0, 0, 360),
(N'colourcraft-painters', N'ColourCraft Painters', N'CF', N'painting', N'pune', N'viman-nagar', N'Santosh Kamble', N'santosh.kamble',
 N'Interior & exterior painting', N'Interior and exterior painting, texture walls, waterproof coatings and wood polish with dust-free mechanised sanding.',
 N'Clover Park, Viman Nagar', N'Near Symbiosis College', 2014, 25, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 0, 1, 290),
-- ===== Chennai =====
(N'murugan-power-electricals', N'Murugan Power Electricals', N'MP', N'electrical', N'chennai', N'velachery', N'Murugan Selvaraj', N'murugan.selvaraj',
 N'Residential & commercial electrical works', N'Wiring, panel boards, generator connections and lighting design for homes and shops in Velachery, Madipakkam and Pallikaranai.',
 N'100 Feet Bypass Road, Velachery', N'Near Phoenix MarketCity Chennai', 2007, 12, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 380),
(N'adyar-plumbing-works', N'Adyar Plumbing Works', N'AW', N'plumbing', N'chennai', N'adyar', N'Senthil Kumar', N'senthil.kumar',
 N'Plumbing & waterproofing', N'Plumbing repairs, terrace waterproofing, sump cleaning and RO installation with prompt service across South Chennai.',
 N'LB Road, Adyar', N'Near Adyar Signal', 2010, 10, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 350),
(N'dr-lakshmi-narayanan-child-care', N'Dr. Lakshmi Narayanan Child Care', N'LN', N'doctors', N'chennai', N'anna-nagar', N'Dr. Lakshmi Narayanan', N'lakshmi.narayanan',
 N'Paediatrician & neonatologist', N'Paediatric consultations, vaccinations, growth monitoring and newborn care with an evening OPD and teleconsultation.',
 N'2nd Avenue, Anna Nagar', N'Near Anna Nagar Tower Park', 2005, 6, N'Active', N'Verified', 1, N'AvailableForVideo', 1, 1, 0, 410),
(N'sri-balaji-pharmacy', N'Sri Balaji Pharmacy', N'BP', N'pharmacy', N'chennai', N'omr-sholinganallur', N'R. Balaji', N'r.balaji',
 N'24x7 pharmacy with home delivery', N'Prescription medicines, surgical supplies and wellness products with 30-minute home delivery along OMR.',
 N'OMR, Sholinganallur', N'Near Sholinganallur Junction', 2012, 9, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 280),
(N'kanchana-bridal-makeover', N'Kanchana Bridal Makeover', N'KB', N'beauty-services', N'chennai', N't-nagar', N'Kanchana Ravi', N'kanchana.ravi',
 N'Traditional South Indian bridal looks', N'Traditional and contemporary bridal makeup, temple jewellery styling and mehendi for South Indian weddings.',
 N'Usman Road, T. Nagar', N'Near Pondy Bazaar', 2012, 8, N'Active', N'Verified', 0, N'AvailableForChat', 1, 0, 1, 300),
(N'venkataraman-and-co-advocates', N'Venkataraman & Co. Advocates', N'VA', N'lawyers', N'chennai', N't-nagar', N'Adv. S. Venkataraman', N's.venkataraman',
 N'Property, family & criminal law', N'Senior counsel practising before the Madras High Court in property, family, cheque bounce and criminal matters.',
 N'GN Chetty Road, T. Nagar', N'Near Vani Mahal', 1996, 10, N'Active', N'Verified', 0, N'AvailableForCall', 1, 1, 0, 420),
(N'annapoorna-caterers', N'Annapoorna Caterers', N'AN', N'caterers', N'chennai', N'adyar', N'Padma Raghavan', N'padma.raghavan',
 N'Traditional South Indian catering', N'Banana-leaf wedding feasts, upanayanam and corporate catering for 50 to 3,000 guests from pure-vegetarian kitchens.',
 N'Gandhi Nagar, Adyar', N'Near Adyar Depot', 2003, 70, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 390),
(N'shinemax-car-spa', N'ShineMax Car Spa', N'SM', N'car-wash', N'chennai', N'velachery', N'Prabhu Dhanasekaran', N'prabhu.dhanasekaran',
 N'Foam wash, detailing & ceramic coating', N'Foam wash, interior detailing, ceramic coating and paint protection film with free pickup and drop.',
 N'Velachery Main Road', N'Near Velachery MRTS', 2019, 11, N'Active', N'Pending', 0, N'Online', 1, 0, 0, 45),
-- ===== Delhi =====
(N'capital-electric-works', N'Capital Electric Works', N'CE', N'electrical', N'delhi', N'lajpat-nagar', N'Rajinder Singh Bedi', N'rajinder.bedi',
 N'Electrical repairs & installations', N'Repairs, fan and geyser installation, earthing and MCB upgrades for homes and showrooms in South Delhi.',
 N'Ring Road, Lajpat Nagar IV', N'Near Moolchand Metro', 2005, 13, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 1, 400),
(N'dr-arvind-khurana-ortho-clinic', N'Dr. Arvind Khurana Ortho Clinic', N'AK', N'doctors', N'delhi', N'saket', N'Dr. Arvind Khurana', N'arvind.khurana',
 N'Orthopaedic & joint replacement surgeon', N'Consultant orthopaedic surgeon for knee and hip replacement, sports injuries, arthroscopy and fracture care.',
 N'Press Enclave Road, Saket', N'Near Select Citywalk', 2002, 8, N'Active', N'Verified', 1, N'AvailableForBooking', 1, 1, 0, 420),
(N'dwarka-diagnostic-centre', N'Dwarka Diagnostic Centre', N'DD', N'diagnostics', N'delhi', N'dwarka', N'Dr. Manpreet Kaur', N'manpreet.kaur',
 N'Pathology, X-ray & ultrasound', N'Fully automated pathology lab with X-ray, ultrasound and ECG, plus free home sample collection across Dwarka sectors.',
 N'Sector 12, Dwarka', N'Near Dwarka Sector 12 Metro', 2011, 26, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 360),
(N'sharma-kapoor-law-offices', N'Sharma Kapoor Law Offices', N'KL', N'lawyers', N'delhi', N'connaught-place', N'Adv. Nidhi Kapoor', N'nidhi.kapoor',
 N'Corporate & dispute resolution', N'Commercial litigation, arbitration and consumer disputes before Delhi courts and tribunals.',
 N'Barakhamba Road, Connaught Place', N'Near Barakhamba Metro', 2001, 25, N'Active', N'Verified', 1, N'AvailableForCall', 1, 1, 0, 430),
(N'safehome-pest-control', N'SafeHome Pest Control', N'SF', N'pest-control', N'delhi', N'rohini', N'Vinod Chauhan', N'vinod.chauhan',
 N'Termite, cockroach & bed-bug control', N'Odourless, government-approved pest treatments for termites, cockroaches, bed bugs and mosquitoes with a 6-month warranty.',
 N'Sector 7, Rohini', N'Near Rithala Metro', 2013, 20, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 0, 1, 300),
(N'dilli-darbar-restaurant', N'Dilli Darbar Restaurant', N'DB', N'restaurants', N'delhi', N'connaught-place', N'Harpreet Singh Anand', N'harpreet.anand',
 N'North Indian & tandoor specialities', N'Butter chicken, dal makhani and tandoori platters in the heart of Connaught Place, with private dining for 40 guests.',
 N'Block N, Connaught Place', N'Near Rajiv Chowk Metro Gate 6', 1999, 40, N'Active', N'Verified', 1, N'Online', 1, 0, 0, 400),
(N'rangoli-weddings-and-events', N'Rangoli Weddings & Events', N'RW', N'event-planners', N'delhi', N'saket', N'Simran Oberoi', N'simran.oberoi',
 N'Destination weddings & décor', N'Wedding planning, destination weddings in Udaipur and Jaipur, floral décor and artist management.',
 N'Saket District Centre', N'Near DLF Avenue', 2010, 35, N'Active', N'Verified', 1, N'AvailableForChat', 1, 1, 0, 370),
(N'saferide-cab-services', N'SafeRide Cab Services', N'SR', N'taxi-services', N'delhi', N'dwarka', N'Sukhwinder Singh', N'sukhwinder.singh',
 N'Airport cabs & outstation', N'IGI Airport transfers, Delhi local packages and outstation trips to Agra, Jaipur and Haridwar.',
 N'Sector 6, Dwarka', N'Near Dwarka Sector 9 Metro', 2012, 45, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 330),
(N'gupta-notary-services', N'Gupta Notary & Documentation', N'GN', N'notary-services', N'delhi', N'rohini', N'Adv. Rakesh Gupta', N'rakesh.gupta',
 N'Notary, affidavits & stamp papers', N'Notarisation, affidavits, rent agreements, e-stamp papers and attestation services completed the same day.',
 N'Sector 3, Rohini', N'Near Rohini Court Complex', 2009, 4, N'Active', N'Verified', 0, N'AvailableForCall', 1, 0, 0, 350),
-- ===== Kolkata =====
(N'pathshala-home-tutors', N'Pathshala Home Tutors', N'PT', N'private-tutors', N'kolkata', N'salt-lake', N'Debashis Chatterjee', N'debashis.chatterjee',
 N'WBBSE, CBSE & ICSE tutors', N'Experienced tutors for Bengali, English, Maths and Science across boards, with home and online classes.',
 N'Sector 1, Salt Lake', N'Near City Centre 1', 2012, 28, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 1, 320),
(N'park-street-dental-care', N'Park Street Dental Care', N'PC', N'dental', N'kolkata', N'park-street', N'Dr. Sayantani Bose', N'sayantani.bose',
 N'Advanced dental care', N'Root canal treatment, implants, orthodontics and smile makeovers in central Kolkata.',
 N'Park Street', N'Near Park Street Metro', 2009, 9, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 1, 0, 350),
(N'banalata-bengali-kitchen', N'Banalata Bengali Kitchen', N'BK', N'restaurants', N'kolkata', N'ballygunge', N'Arindam Ghosh', N'arindam.ghosh',
 N'Authentic Bengali cuisine', N'Kosha mangsho, chingri malai curry and Bengali thalis cooked the traditional way in mustard oil.',
 N'Gariahat Road, Ballygunge', N'Near Gariahat Crossing', 2006, 22, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 340),
(N'anandam-interiors', N'Anandam Interiors', N'AI', N'interior-designers', N'kolkata', N'new-town', N'Sourav Dutta', N'sourav.dutta',
 N'Turnkey home interiors', N'Turnkey interiors for New Town and Rajarhat apartments with modular kitchens, wardrobes and puja units.',
 N'Action Area 1, New Town', N'Near Axis Mall', 2017, 16, N'Active', N'Verified', 0, N'AvailableForVideo', 1, 1, 1, 260),
(N'chilltech-ac-services', N'ChillTech AC Services', N'CT', N'ac-repair', N'kolkata', N'salt-lake', N'Partha Sarathi Das', N'partha.das',
 N'AC service & installation', N'AC servicing, installation and gas charging for homes and offices in Salt Lake and New Town.',
 N'Sector 5, Salt Lake', N'Near College More', 2015, 12, N'Active', N'Verified', 0, N'Busy', 1, 0, 1, 290),
-- ===== Ahmedabad =====
(N'shreeji-electricals', N'Shreeji Electricals', N'SJ', N'electrical', N'ahmedabad', N'navrangpura', N'Hitesh Patel', N'hitesh.patel',
 N'Electrical contractor & repairs', N'Residential and commercial electrical contracting, solar rooftop wiring and appliance repairs.',
 N'CG Road, Navrangpura', N'Near Municipal Market', 2008, 12, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 380),
(N'dr-hetal-shah-womens-clinic', N'Dr. Hetal Shah Women''s Clinic', N'HS', N'doctors', N'ahmedabad', N'satellite', N'Dr. Hetal Shah', N'hetal.shah',
 N'Gynaecologist & obstetrician', N'Pregnancy care, infertility evaluation, PCOS management and laparoscopic gynaecology.',
 N'Satellite Road', N'Near Shivranjani Cross Roads', 2007, 10, N'Active', N'Verified', 0, N'AvailableForVideo', 1, 1, 0, 400),
(N'akhada-fitness-studio', N'Akhada Fitness Studio', N'AF', N'fitness', N'ahmedabad', N'bodakdev', N'Jignesh Desai', N'jignesh.desai',
 N'Functional training & CrossFit', N'Functional training, CrossFit-style group classes and personal training with body-composition tracking.',
 N'Sindhu Bhavan Road, Bodakdev', N'Near Pakwan Cross Roads', 2018, 9, N'Active', N'Verified', 0, N'Online', 1, 1, 0, 240),
(N'sabarmati-realty', N'Sabarmati Realty', N'SB', N'real-estate-agents', N'ahmedabad', N'bodakdev', N'Mehul Shah', N'mehul.shah',
 N'Residential & commercial property', N'New launches, resale homes and commercial leasing along SG Highway, Bodakdev and Thaltej.',
 N'SG Highway, Bodakdev', N'Near Rajpath Club', 2010, 12, N'PendingApproval', N'Pending', 0, N'Offline', 0, 0, 0, 8),
(N'grip-tyres-and-alignment', N'Grip Tyres & Wheel Alignment', N'GT', N'tyres', N'ahmedabad', N'maninagar', N'Rajesh Solanki', N'rajesh.solanki',
 N'Tyres, alignment & balancing', N'Branded tyres, computerised wheel alignment, balancing and nitrogen filling for cars and SUVs.',
 N'Maninagar Cross Road', N'Near Kankaria Lake', 2010, 8, N'PendingApproval', N'Rejected', 0, N'Offline', 0, 0, 0, 20),
-- ===== Jaipur =====
(N'rajputana-royal-weddings', N'Rajputana Royal Weddings', N'RR', N'event-planners', N'jaipur', N'c-scheme', N'Vikram Singh Rathore', N'vikram.rathore',
 N'Palace & destination weddings', N'Royal palace weddings, haldi-mehendi functions and corporate events across Jaipur, Udaipur and Jodhpur.',
 N'Prithviraj Road, C-Scheme', N'Near Statue Circle', 2008, 40, N'Active', N'Verified', 1, N'AvailableForCall', 1, 1, 0, 390),
(N'pink-city-cabs', N'Pink City Cabs', N'PK', N'taxi-services', N'jaipur', N'vaishali-nagar', N'Mahendra Singh Shekhawat', N'mahendra.shekhawat',
 N'Sightseeing & airport taxis', N'Jaipur sightseeing tours, airport transfers and outstation trips to Ajmer, Pushkar and Ranthambore.',
 N'Queens Road, Vaishali Nagar', N'Near Vaishali Nagar Circle', 2015, 30, N'Active', N'Verified', 0, N'Online', 1, 0, 0, 300),
(N'rehabplus-physiotherapy', N'RehabPlus Physiotherapy', N'RP', N'physiotherapy', N'jaipur', N'malviya-nagar', N'Dr. Neha Saini', N'neha.saini',
 N'Neuro & ortho rehabilitation', N'Neuro-rehabilitation, post-stroke therapy, and back and neck pain treatment with modern electrotherapy.',
 N'Sector 3, Malviya Nagar', N'Near Gaurav Tower', 2016, 7, N'Active', N'Verified', 0, N'AvailableForBooking', 1, 1, 1, 270),
-- ===== Kochi =====
(N'sukhayu-ayurveda-spa', N'Sukhayu Ayurveda Spa', N'SY', N'spa', N'kochi', N'kakkanad', N'Dr. Anoop Menon', N'anoop.menon',
 N'Authentic Ayurvedic therapies', N'Abhyangam, shirodhara and panchakarma programmes administered by qualified Ayurveda physicians.',
 N'Seaport-Airport Road, Kakkanad', N'Near Infopark Phase 1', 2013, 14, N'Active', N'Verified', 1, N'AvailableForBooking', 1, 1, 0, 360),
(N'gurukul-coaching-centre', N'Gurukul Coaching Centre', N'GC', N'test-preparation', N'kochi', N'edappally', N'Thomas Mathew', N'thomas.mathew',
 N'NEET & KEAM coaching', N'NEET and KEAM coaching with daily practice papers, doubt-clearing sessions and residential batches.',
 N'NH 66, Edappally', N'Near Lulu Mall', 2010, 32, N'Active', N'Verified', 0, N'AvailableForChat', 1, 1, 0, 330),
(N'anchor-towing-and-roadside', N'Anchor Towing & Roadside Assistance', N'AT', N'towing', N'kochi', N'panampilly-nagar', N'Shibu Varghese', N'shibu.varghese',
 N'24x7 towing & breakdown support', N'Flatbed towing, battery jump-starts, flat-tyre help and accident recovery across Ernakulam district.',
 N'Panampilly Avenue', N'Near Panampilly Nagar Park', 2014, 16, N'Active', N'Verified', 0, N'Online', 1, 0, 1, 300);

/* ---------- Owner accounts ---------- */
IF OBJECT_ID('tempdb..#O') IS NOT NULL DROP TABLE #O;
SELECT b.OwnerEmail + N'@demo.callingbell.in' AS Email,
       b.OwnerName AS FullName,
       N'+91 ' + STUFF(CAST(7000000000 + ABS(CHECKSUM(b.OwnerEmail)) % 2999999999 AS nvarchar(10)), 6, 0, N' ') AS Phone,
       b.CitySlug,
       CASE WHEN b.SubSlug IN (N'doctors', N'private-tutors', N'fitness', N'yoga', N'physiotherapy', N'beauty-services') THEN N'ServiceProvider' ELSE N'BusinessOwner' END AS UserType,
       b.DaysAgo + 3 AS DaysAgo
INTO #O
FROM #B b;

MERGE dbo.AspNetUsers AS t
USING (SELECT o.*, c.Id AS CityId FROM #O o LEFT JOIN dbo.Cities c ON c.Slug = o.CitySlug) AS s
ON t.NormalizedEmail = UPPER(s.Email)
WHEN MATCHED AND (t.DisplayName <> s.FullName OR t.UserType <> s.UserType)
    THEN UPDATE SET DisplayName = s.FullName, UserType = s.UserType, ModifiedBy = N'seed', ModifiedOn = @Now
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp,
                 PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount,
                 DisplayName, UserType, CityId, IsActive, LastLoginOn, CreatedBy, CreatedOn, IsDeleted)
         VALUES (LOWER(CONVERT(nvarchar(36), NEWID())), s.Email, UPPER(s.Email), s.Email, UPPER(s.Email), 1, @PasswordHash,
                 UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')), LOWER(CONVERT(nvarchar(36), NEWID())),
                 s.Phone, 1, 0, 1, 0, s.FullName, s.UserType, s.CityId, 1,
                 DATEADD(HOUR, -(ABS(CHECKSUM(s.Email)) % 72), @Now), N'seed', DATEADD(DAY, -s.DaysAgo, @Now), 0);

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT DISTINCT u.Id, r.Id
FROM #O o
JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(o.Email)
JOIN dbo.AspNetRoles r ON r.NormalizedName IN (N'BUSINESSOWNER', CASE WHEN o.UserType = N'ServiceProvider' THEN N'SERVICEPROVIDER' END)
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

/* ---------- Businesses ---------- */
MERGE dbo.Businesses AS t
USING (
    SELECT b.*, u.Id AS OwnerUserId, sc.Id AS SubCategoryId, sc.CategoryId, c.Id AS CityId, c.Name AS CityName,
           a.Id AS AreaId, a.Name AS AreaName, a.Pincode,
           CAST(a.Latitude  + ((ABS(CHECKSUM(b.Slug)) % 200) - 100) / 20000.0 AS decimal(9,6)) AS Lat,
           CAST(a.Longitude + ((ABS(CHECKSUM(REVERSE(b.Slug))) % 200) - 100) / 20000.0 AS decimal(9,6)) AS Lng,
           N'+91 ' + STUFF(CAST(8000000000 + ABS(CHECKSUM(b.Slug)) % 1999999999 AS nvarchar(10)), 6, 0, N' ') AS Phone,
           CASE c.Slug
               WHEN N'hyderabad' THEN N'English, Hindi, Telugu'   WHEN N'bengaluru' THEN N'English, Kannada, Hindi'
               WHEN N'mumbai'    THEN N'English, Hindi, Marathi'  WHEN N'pune'      THEN N'English, Marathi, Hindi'
               WHEN N'chennai'   THEN N'English, Tamil'           WHEN N'delhi'     THEN N'English, Hindi, Punjabi'
               WHEN N'kolkata'   THEN N'English, Bengali, Hindi'  WHEN N'ahmedabad' THEN N'English, Gujarati, Hindi'
               WHEN N'jaipur'    THEN N'English, Hindi'           WHEN N'kochi'     THEN N'English, Malayalam'
               ELSE N'English, Hindi' END AS Languages
    FROM #B b
    JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(b.OwnerEmail + N'@demo.callingbell.in')
    JOIN dbo.SubCategories sc ON sc.Slug = b.SubSlug
    JOIN dbo.Cities c ON c.Slug = b.CitySlug
    JOIN dbo.Areas a ON a.CityId = c.Id AND a.Slug = b.AreaSlug
) AS s
ON t.Slug = s.Slug
WHEN MATCHED AND (t.Name <> s.Name OR ISNULL(t.Tagline, N'') <> s.Tagline OR t.Description <> s.Description OR t.Status <> s.Status
                  OR t.VerificationStatus <> s.Verification OR t.IsFeatured <> s.IsFeatured OR ISNULL(t.SubCategoryId, '00000000-0000-0000-0000-000000000000') <> s.SubCategoryId)
    THEN UPDATE SET Name = s.Name, Tagline = s.Tagline, Description = s.Description, Status = s.Status, VerificationStatus = s.Verification,
                    IsFeatured = s.IsFeatured, SubCategoryId = s.SubCategoryId, CategoryId = s.CategoryId,
                    ModifiedBy = N'seed', ModifiedOn = @Now
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, OwnerUserId, CategoryId, SubCategoryId, Name, Slug, Tagline, Description,
                 CityId, AreaId, City, Area, AddressLine, Landmark, Pincode, Latitude, Longitude,
                 PhoneNumber, WhatsAppNumber, Email, Website,
                 YearEstablished, TeamSize, Languages, ResponseTimeMinutes, AcceptsOnlineBooking, OffersVideoConsultation, OffersHomeService,
                 Status, VerificationStatus, VerifiedOn, IsFeatured, AvailabilityStatus, LastSeenOn,
                 AverageRating, ReviewCount, CreatedBy, CreatedOn, IsDeleted)
         VALUES (NEWID(), s.OwnerUserId, s.CategoryId, s.SubCategoryId, s.Name, s.Slug, s.Tagline, s.Description,
                 s.CityId, s.AreaId, s.CityName, s.AreaName, s.AddressLine, s.Landmark, s.Pincode, s.Lat, s.Lng,
                 s.Phone, s.Phone, s.Slug + N'@demo.callingbell.in',
                 CASE WHEN s.TeamSize >= 15 THEN N'https://' + s.Slug + N'.demo.callingbell.in' END,
                 s.YearEstablished, s.TeamSize, s.Languages, 5 + ABS(CHECKSUM(s.Slug)) % 55, s.AcceptsBooking, s.Video, s.HomeService,
                 s.Status, s.Verification,
                 CASE WHEN s.Verification = N'Verified' THEN DATEADD(DAY, -(s.DaysAgo - 4), @Now) END,
                 s.IsFeatured, s.Availability,
                 CASE WHEN s.Availability = N'Offline' THEN DATEADD(HOUR, -(2 + ABS(CHECKSUM(s.Slug)) % 40), @Now)
                      ELSE DATEADD(MINUTE, -(ABS(CHECKSUM(s.Slug)) % 9), @Now) END,
                 0, 0, N'seed', DATEADD(DAY, -s.DaysAgo, @Now), 0);

/* ---------- Working hours ---------- */
IF OBJECT_ID('tempdb..#HourPattern') IS NOT NULL DROP TABLE #HourPattern;
CREATE TABLE #HourPattern (Pattern nvarchar(20), DayOfWeek tinyint, OpenTime time(0) NULL, CloseTime time(0) NULL, IsClosed bit);
;WITH d AS (SELECT v.d FROM (VALUES (0),(1),(2),(3),(4),(5),(6)) v(d))
INSERT INTO #HourPattern
SELECT N'24x7',        d, '00:00', '23:59', 0 FROM d UNION ALL
SELECT N'clinic',      d, CASE WHEN d = 0 THEN '09:00' ELSE '08:00' END, CASE WHEN d = 0 THEN '13:00' ELSE '22:00' END, 0 FROM d UNION ALL
SELECT N'doctor',      d, CASE WHEN d = 0 THEN NULL ELSE '10:00' END, CASE WHEN d = 0 THEN NULL ELSE '20:00' END, CASE WHEN d = 0 THEN 1 ELSE 0 END FROM d UNION ALL
SELECT N'homeservice', d, '08:00', '21:00', 0 FROM d UNION ALL
SELECT N'salon',       d, CASE WHEN d = 2 THEN NULL ELSE '10:00' END, CASE WHEN d = 2 THEN NULL ELSE '21:00' END, CASE WHEN d = 2 THEN 1 ELSE 0 END FROM d UNION ALL
SELECT N'restaurant',  d, '11:30', '23:30', 0 FROM d UNION ALL
SELECT N'cafe',        d, '06:30', '22:00', 0 FROM d UNION ALL
SELECT N'office',      d, CASE WHEN d = 0 THEN NULL ELSE '10:00' END, CASE WHEN d = 0 THEN NULL WHEN d = 6 THEN '14:00' ELSE '19:00' END, CASE WHEN d = 0 THEN 1 ELSE 0 END FROM d UNION ALL
SELECT N'education',   d, CASE WHEN d = 0 THEN '09:00' ELSE '07:00' END, CASE WHEN d = 0 THEN '13:00' ELSE '21:00' END, 0 FROM d UNION ALL
SELECT N'fitness',     d, CASE WHEN d = 0 THEN '07:00' ELSE '06:00' END, CASE WHEN d = 0 THEN '12:00' ELSE '21:00' END, 0 FROM d UNION ALL
SELECT N'retail',      d, CASE WHEN d = 0 THEN '10:00' ELSE '09:30' END, CASE WHEN d = 0 THEN '14:00' ELSE '20:30' END, 0 FROM d;

;WITH bp AS (
    SELECT b.Id AS BusinessId,
           CASE sc.Slug
               WHEN N'hospitals' THEN N'24x7' WHEN N'pharmacy' THEN N'24x7' WHEN N'taxi-services' THEN N'24x7' WHEN N'towing' THEN N'24x7'
               WHEN N'clinics' THEN N'clinic' WHEN N'diagnostics' THEN N'clinic'
               WHEN N'doctors' THEN N'doctor' WHEN N'dental' THEN N'doctor' WHEN N'physiotherapy' THEN N'doctor'
               WHEN N'beauty-salons' THEN N'salon' WHEN N'spa' THEN N'salon'
               WHEN N'restaurants' THEN N'restaurant' WHEN N'caterers' THEN N'restaurant'
               WHEN N'cafes' THEN N'cafe'
               WHEN N'private-tutors' THEN N'education' WHEN N'test-preparation' THEN N'education' WHEN N'skill-training' THEN N'education' WHEN N'schools' THEN N'education'
               WHEN N'fitness' THEN N'fitness' WHEN N'yoga' THEN N'fitness'
               WHEN N'car-repair' THEN N'retail' WHEN N'tyres' THEN N'retail'
               WHEN N'electrical' THEN N'homeservice' WHEN N'plumbing' THEN N'homeservice' WHEN N'ac-repair' THEN N'homeservice'
               WHEN N'cleaning' THEN N'homeservice' WHEN N'painting' THEN N'homeservice' WHEN N'pest-control' THEN N'homeservice'
               WHEN N'carpenters' THEN N'homeservice' WHEN N'packers-movers' THEN N'homeservice' WHEN N'car-wash' THEN N'homeservice'
               WHEN N'beauty-services' THEN N'homeservice'
               ELSE N'office' END AS Pattern
    FROM dbo.Businesses b
    JOIN #B src ON src.Slug = b.Slug
    JOIN dbo.SubCategories sc ON sc.Id = b.SubCategoryId
)
MERGE dbo.BusinessHours AS t
USING (SELECT bp.BusinessId, hp.DayOfWeek, hp.OpenTime, hp.CloseTime, hp.IsClosed FROM bp JOIN #HourPattern hp ON hp.Pattern = bp.Pattern) AS s
ON t.BusinessId = s.BusinessId AND t.DayOfWeek = s.DayOfWeek
WHEN NOT MATCHED BY TARGET
    THEN INSERT (BusinessId, DayOfWeek, OpenTime, CloseTime, IsClosed) VALUES (s.BusinessId, s.DayOfWeek, s.OpenTime, s.CloseTime, s.IsClosed);

/* ---------- Logos, covers and gallery artwork ---------- */
IF OBJECT_ID('tempdb..#BArt') IS NOT NULL DROP TABLE #BArt;
;WITH info AS (
    SELECT b.Id, b.Slug, b.Name, b.Area, b.City, src.Initials, cat.ColorHex,
           REPLACE(UPPER(sc.Name), N'&', N'&amp;') AS SubLabel,
           ABS(CHECKSUM(b.Slug)) % 3 AS Variant,
           SUBSTRING(icon.Svg, CHARINDEX(N'>', icon.Svg) + 1, LEN(icon.Svg) - CHARINDEX(N'>', icon.Svg) - 6) AS IconMarkup
    FROM dbo.Businesses b
    JOIN #B src ON src.Slug = b.Slug
    JOIN dbo.SubCategories sc ON sc.Id = b.SubCategoryId
    JOIN dbo.Categories cat ON cat.Id = b.CategoryId
    CROSS APPLY (SELECT CAST(CAST(m.FileData AS varchar(max)) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS nvarchar(max)) AS Svg
                 FROM dbo.Media m WHERE m.EntityType = N'SubCategoryIcon' AND m.EntityId = sc.Id) icon
)
SELECT EntityType, EntityId, FileName, AltText, Svg
INTO #BArt
FROM (
    SELECT N'BusinessLogo' AS EntityType, i.Id AS EntityId, N'logo.svg' AS FileName, i.Name + N' logo' AS AltText,
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">',
                  N'<rect width="256" height="256" fill="', i.ColorHex, N'"/>',
                  N'<circle cx="', 210 - i.Variant * 30, N'" cy="40" r="90" fill="#FFFFFF" fill-opacity="0.12"/>',
                  N'<text x="128" y="160" text-anchor="middle" font-family="Inter, Segoe UI, Arial, sans-serif" font-size="96" font-weight="700" fill="#FFFFFF">', i.Initials, N'</text></svg>') AS Svg
    FROM info i
    UNION ALL
    SELECT N'BusinessCover', i.Id, N'cover.svg', i.Name + N' cover image',
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="480" viewBox="0 0 1200 480">',
                  N'<rect width="1200" height="480" fill="#0B1220"/>',
                  N'<circle cx="', 960 + i.Variant * 40, N'" cy="', 40 + i.Variant * 30, N'" r="320" fill="', i.ColorHex, N'" fill-opacity="0.32"/>',
                  N'<circle cx="1180" cy="460" r="200" fill="', i.ColorHex, N'" fill-opacity="0.2"/>',
                  N'<circle cx="', 120 + i.Variant * 60, N'" cy="60" r="90" fill="#FFFFFF" fill-opacity="0.03"/>',
                  N'<g transform="translate(860 110) scale(10)" fill="none" stroke="#FFFFFF" stroke-opacity="0.92" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g>',
                  N'</svg>')
    FROM info i
    UNION ALL
    SELECT N'BusinessGallery', i.Id, N'gallery-1.svg', i.Name + N' - front desk',
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800" viewBox="0 0 1200 800">',
                  N'<rect width="1200" height="800" fill="#FFFFFF"/><rect width="1200" height="800" fill="', i.ColorHex, N'" fill-opacity="0.08"/>',
                  N'<rect x="80" y="80" width="460" height="300" rx="16" fill="', i.ColorHex, N'" fill-opacity="0.10"/>',
                  N'<rect x="80" y="420" width="460" height="300" rx="16" fill="', i.ColorHex, N'" fill-opacity="0.06"/>',
                  N'<g transform="translate(660 220) scale(15)" fill="none" stroke="', i.ColorHex, N'" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g></svg>')
    FROM info i
    UNION ALL
    SELECT N'BusinessGallery', i.Id, N'gallery-2.svg', i.Name + N' - team at work',
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800" viewBox="0 0 1200 800">',
                  N'<rect width="1200" height="800" fill="#0B1220"/>',
                  N'<g stroke="', i.ColorHex, N'" stroke-opacity="0.18">',
                  N'<path d="M0 200H1200M0 400H1200M0 600H1200M300 0V800M600 0V800M900 0V800"/></g>',
                  N'<circle cx="600" cy="400" r="260" fill="', i.ColorHex, N'" fill-opacity="0.22"/>',
                  N'<g transform="translate(420 220) scale(15)" fill="none" stroke="#FFFFFF" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g></svg>')
    FROM info i
    UNION ALL
    SELECT N'BusinessGallery', i.Id, N'gallery-3.svg', i.Name + N' - services',
           CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800" viewBox="0 0 1200 800">',
                  N'<rect width="1200" height="800" fill="', i.ColorHex, N'"/>',
                  N'<circle cx="200" cy="160" r="240" fill="#FFFFFF" fill-opacity="0.10"/>',
                  N'<circle cx="1040" cy="680" r="300" fill="#FFFFFF" fill-opacity="0.08"/>',
                  N'<g transform="translate(420 220) scale(15)" fill="none" stroke="#FFFFFF" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round">', i.IconMarkup, N'</g></svg>')
    FROM info i
) x;

MERGE dbo.Media AS t
USING (SELECT EntityType, EntityId, FileName, AltText,
              CAST(CAST(Svg COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max)) AS varbinary(max)) AS Bytes
       FROM #BArt) AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = s.FileName
WHEN MATCHED AND (t.FileData <> s.Bytes OR ISNULL(t.AltText, N'') <> s.AltText)
    THEN UPDATE SET FileData = s.Bytes, ThumbnailData = s.Bytes, FileSize = DATALENGTH(s.Bytes), AltText = s.AltText,
                    IsActive = 1, ModifiedBy = N'seed', ModifiedOn = @Now
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, s.FileName, N'image/svg+xml', N'.svg', DATALENGTH(s.Bytes), s.Bytes, s.Bytes, s.AltText,
                 CASE WHEN s.FileName IN (N'logo.svg', N'cover.svg') THEN 1 ELSE 0 END, 1, 0, N'seed', @Now);

UPDATE b SET
    LogoUrl       = N'/api/media/' + LOWER(CONVERT(nvarchar(36), ml.MediaId)),
    CoverImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mc.MediaId))
FROM dbo.Businesses b
JOIN #B src ON src.Slug = b.Slug
JOIN dbo.Media ml ON ml.EntityType = N'BusinessLogo'  AND ml.EntityId = b.Id AND ml.FileName = N'logo.svg'
JOIN dbo.Media mc ON mc.EntityType = N'BusinessCover' AND mc.EntityId = b.Id AND mc.FileName = N'cover.svg';

MERGE dbo.BusinessImages AS t
USING (
    SELECT b.Id AS BusinessId,
           N'/api/media/' + LOWER(CONVERT(nvarchar(36), m.MediaId)) AS Url,
           m.AltText,
           CASE m.FileName WHEN N'cover.svg' THEN 0 WHEN N'gallery-1.svg' THEN 1 WHEN N'gallery-2.svg' THEN 2 ELSE 3 END AS SortOrder,
           CASE m.FileName WHEN N'cover.svg' THEN N'Storefront' WHEN N'gallery-1.svg' THEN N'Front desk' WHEN N'gallery-2.svg' THEN N'Our team at work' ELSE N'Services we offer' END AS Caption,
           b.CreatedOn
    FROM dbo.Businesses b
    JOIN #B src ON src.Slug = b.Slug
    JOIN dbo.Media m ON m.EntityId = b.Id AND m.EntityType IN (N'BusinessCover', N'BusinessGallery')
) AS s
ON t.BusinessId = s.BusinessId AND t.SortOrder = s.SortOrder
WHEN MATCHED AND t.ImageUrl <> s.Url
    THEN UPDATE SET ImageUrl = s.Url, ThumbnailUrl = s.Url + N'/thumbnail', MobileImageUrl = s.Url, DesktopImageUrl = s.Url, AltText = s.AltText,
                    ModifiedBy = N'seed', ModifiedOn = @Now
WHEN NOT MATCHED BY TARGET
    THEN INSERT (BusinessId, ImageUrl, ThumbnailUrl, MobileImageUrl, DesktopImageUrl, AltText, Caption, IsPrimary, SortOrder, CreatedBy, CreatedOn)
         VALUES (s.BusinessId, s.Url, s.Url + N'/thumbnail', s.Url, s.Url, s.AltText, s.Caption, CASE WHEN s.SortOrder = 0 THEN 1 ELSE 0 END, s.SortOrder, N'seed', s.CreatedOn);

COMMIT TRANSACTION;
PRINT '04_Businesses.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #B, #O, #HourPattern, #BArt;
GO
