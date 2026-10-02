/* =====================================================================================
   Calling Bell - 03_Categories.sql
   Category taxonomy (top-level categories + sub-categories) and their artwork.

   * Merges onto the categories/sub-categories that already existed (matched by Slug),
     so their Ids are preserved.
   * All artwork is SVG generated here and stored in dbo.Media; entities reference it via
     ImageUrl / IconUrl / BannerUrl = /api/media/{MediaId}.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------- Icon library (24x24 stroke icons) ---------- */
IF OBJECT_ID('tempdb..#Icon') IS NOT NULL DROP TABLE #Icon;
CREATE TABLE #Icon (IconKey nvarchar(40) PRIMARY KEY, Markup nvarchar(2000) NOT NULL);
INSERT INTO #Icon (IconKey, Markup) VALUES
(N'heart-pulse', N'<path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"/><path d="M3.22 12H9.5l.5-1 2 4.5 2-7 1.5 3.5h5.27"/>'),
(N'stethoscope', N'<path d="M4.8 2.3A.3.3 0 1 0 5 2H4a2 2 0 0 0-2 2v5a6 6 0 0 0 6 6 6 6 0 0 0 6-6V4a2 2 0 0 0-2-2h-1a.2.2 0 1 0 .3.3"/><path d="M8 15v1a6 6 0 0 0 6 6 6 6 0 0 0 6-6v-4"/><circle cx="20" cy="10" r="2"/>'),
(N'tooth',       N'<path d="M7 3c-2.5 0-4 2-4 4.5 0 3 1.5 4.5 2 7 .5 2.5 1 6.5 3 6.5s2-4 4-4 2 4 4 4 2.5-4 3-6.5c.5-2.5 2-4 2-7C21 5 19.5 3 17 3c-2 0-3 1-5 1S9 3 7 3z"/>'),
(N'flask',       N'<path d="M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2h12.4a1.5 1.5 0 0 0 1.3-2L14 9V3"/><path d="M7 15h10"/>'),
(N'hospital',    N'<rect x="3" y="4" width="18" height="17" rx="2"/><path d="M12 8v6M9 11h6M9 21v-3h6v3"/>'),
(N'pill',        N'<path d="m10.5 20.5 10-10a4.95 4.95 0 1 0-7-7l-10 10a4.95 4.95 0 1 0 7 7Z"/><path d="m8.5 8.5 7 7"/>'),
(N'clinic',      N'<path d="M3 21h18M5 21V8l7-5 7 5v13"/><path d="M12 9v6M9 12h6"/>'),
(N'home',        N'<path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M9 22V12h6v10"/>'),
(N'zap',         N'<path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z"/>'),
(N'wrench',      N'<path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>'),
(N'snowflake',   N'<path d="M12 2v20M2 12h20M4.93 4.93l14.14 14.14M19.07 4.93 4.93 19.07"/><path d="m9 3 3 3 3-3M9 21l3-3 3 3M3 9l3 3-3 3M21 9l-3 3 3 3"/>'),
(N'sparkles',    N'<path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/><path d="M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z"/>'),
(N'roller',      N'<rect x="2" y="2" width="16" height="6" rx="2"/><path d="M10 16v-2a2 2 0 0 1 2-2h8a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2"/><rect x="8" y="16" width="4" height="6" rx="1"/>'),
(N'bug',         N'<path d="m8 2 1.88 1.88M14.12 3.88 16 2M9 7.13v-1a3 3 0 1 1 6 0v1"/><path d="M12 20c-3.3 0-6-2.7-6-6v-3a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v3c0 3.3-2.7 6-6 6M12 20v-9M6.53 9C4.6 8.8 3 7.1 3 5M6 13H2M3 21c0-2.1 1.7-3.9 3.8-4M20.97 5c0 2.1-1.6 3.8-3.5 4M22 13h-4M17.2 17c2.1.1 3.8 1.9 3.8 4"/>'),
(N'hammer',      N'<path d="m15 12-8.5 8.5a2.12 2.12 0 0 1-3-3L12 9"/><path d="M17.64 15 22 10.64M20.91 11.7l-1.25-1.25c-.6-.6-.93-1.4-.93-2.25v-.86L16.01 4.6a5.56 5.56 0 0 0-3.94-1.64H9l.92.82A6.18 6.18 0 0 1 12 8.4v1.56l2 2h2.47l2.26 1.91"/>'),
(N'graduation',  N'<path d="M22 10 12 5 2 10l10 5 10-5z"/><path d="M6 12v5c3 3 9 3 12 0v-5"/>'),
(N'school',      N'<path d="M14 22v-4a2 2 0 1 0-4 0v4"/><path d="m18 10 4 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-8l4-2"/><path d="M18 5v17M6 5v17"/><path d="m4 6 8-4 8 4"/><circle cx="12" cy="9" r="2"/>'),
(N'laptop-code', N'<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M2 20h20M9 8l-2 2 2 2M15 8l2 2-2 2"/>'),
(N'book-open',   N'<path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/>'),
(N'scissors',    N'<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M20 4 8.12 15.88M14.47 14.48 20 20M8.12 8.12 12 12"/>'),
(N'leaf',        N'<path d="M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.48 19 2c1 2 2 4.18 2 8 0 5.5-4.78 10-10 10Z"/><path d="M2 21c0-3 1.85-5.36 5.08-6C9.5 14.52 12 13 13 12"/>'),
(N'dumbbell',    N'<path d="M6.5 6.5v11M17.5 6.5v11M3 9.5v5M21 9.5v5M6.5 12h11"/>'),
(N'yoga',        N'<circle cx="12" cy="4.5" r="2"/><path d="M4 10.5c3 1 5.5 1.2 8 1.2s5-.2 8-1.2M12 11.7v4.3l-4.5 5M12 16l4.5 5"/>'),
(N'activity',    N'<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>'),
(N'brush',       N'<path d="m9.06 11.9 8.07-8.06a2.85 2.85 0 1 1 4.03 4.03l-8.06 8.08"/><path d="M7.07 14.94c-1.66 0-3 1.35-3 3.02 0 1.33-2.5 1.52-2 2.02 1.08 1.1 2.49 2.02 4 2.02 2.2 0 4-1.8 4-4.04a3.01 3.01 0 0 0-3-3.02z"/>'),
(N'car',         N'<path d="M19 17h2c.6 0 1-.4 1-1v-3c0-.9-.7-1.7-1.5-1.9L18 10l-2.7-3.6A2 2 0 0 0 13.7 6H7.3a2 2 0 0 0-1.6.8L3 10.5l-1.1.6A2 2 0 0 0 1 12.8V16c0 .6.4 1 1 1h2"/><circle cx="7" cy="17" r="2"/><circle cx="17" cy="17" r="2"/><path d="M9 17h6"/>'),
(N'droplets',    N'<path d="M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5c-.5 2.5-2 4.9-4 6.5C6 11.1 5 13 5 15a7 7 0 0 0 7 7z"/>'),
(N'truck',       N'<path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/><path d="M15 18H9"/><path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.62l-3.48-4.35A1 1 0 0 0 17.52 8H14"/><circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/>'),
(N'tyre',        N'<circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="4"/><path d="M12 2v6M12 16v6M2 12h6M16 12h6"/>'),
(N'scale',       N'<path d="m16 16 3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z"/><path d="m2 16 3-8 3 8c-.87.65-1.92 1-3 1s-2.13-.35-3-1Z"/><path d="M7 21h10M12 3v18M3 7h2c2 0 5-1 7-2 2 1 5 2 7 2h2"/>'),
(N'briefcase',   N'<rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/>'),
(N'stamp',       N'<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M8 18c1.5-2 3-2 4 0s2.5 2 4 0"/>'),
(N'utensils',    N'<path d="M3 2v7c0 1.1.9 2 2 2h4a2 2 0 0 0 2-2V2M7 2v20M21 15V2a5 5 0 0 0-5 5v6c0 1.1.9 2 2 2h3zm0 0v7"/>'),
(N'coffee',      N'<path d="M17 8h1a4 4 0 1 1 0 8h-1"/><path d="M3 8h14v9a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4Z"/><path d="M6 2v2M10 2v2M14 2v2"/>'),
(N'chef-hat',    N'<path d="M17 21a1 1 0 0 0 1-1v-5.35c0-.46.32-.84.73-1.04a4 4 0 0 0-2.13-7.59 5 5 0 0 0-9.19 0 4 4 0 0 0-2.13 7.59c.41.2.72.58.72 1.04V20a1 1 0 0 0 1 1Z"/><path d="M6 17h12"/>'),
(N'calendar',    N'<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18M9 16l2 2 4-4"/>'),
(N'camera',      N'<path d="M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z"/><circle cx="12" cy="13" r="3"/>'),
(N'flower',      N'<circle cx="12" cy="12" r="3"/><path d="M12 9a3 3 0 1 1 3-3 3 3 0 0 1 3 3 3 3 0 0 1-3 3 3 3 0 0 1 3 3 3 3 0 0 1-3 3 3 3 0 0 1-3 3 3 3 0 0 1-3-3 3 3 0 0 1-3-3 3 3 0 0 1 3-3 3 3 0 0 1-3-3 3 3 0 0 1 3-3 3 3 0 0 1 3 3"/>'),
(N'building',    N'<path d="M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z"/><path d="M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2M10 6h4M10 10h4M10 14h4M10 18h4"/>'),
(N'key',         N'<circle cx="7.5" cy="15.5" r="5.5"/><path d="m21 2-9.6 9.6M15.5 7.5l3 3L22 7l-3-3"/>'),
(N'ruler',       N'<path d="M21.3 15.3a2.4 2.4 0 0 1 0 3.4l-2.6 2.6a2.4 2.4 0 0 1-3.4 0L2.7 8.7a2.41 2.41 0 0 1 0-3.4l2.6-2.6a2.41 2.41 0 0 1 3.4 0Z"/><path d="m14.5 12.5 2-2M11.5 9.5l2-2M8.5 6.5l2-2M17.5 15.5l2-2"/>'),
(N'sofa',        N'<path d="M20 9V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v3"/><path d="M2 16a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5a2 2 0 0 0-4 0v2H6v-2a2 2 0 0 0-4 0Z"/><path d="M4 18v2M20 18v2"/>'),
(N'taxi',        N'<path d="M10 2h4M5 11l1.5-4.5A2 2 0 0 1 8.4 5h7.2a2 2 0 0 1 1.9 1.5L19 11"/><rect x="3" y="11" width="18" height="6" rx="2"/><path d="M5 17v2M19 17v2"/><circle cx="7.5" cy="14" r="1"/><circle cx="16.5" cy="14" r="1"/>'),
(N'plane',       N'<path d="M17.8 19.2 16 11l3.5-3.5C21 6 21.5 4 21 3c-1-.5-3 0-4.5 1.5L13 8 4.8 6.2c-.5-.1-.9.1-1.1.5l-.3.5c-.2.5-.1 1 .3 1.3L9 12l-2 3H4l-1 1 3 2 2 3 1-1v-3l3-2 3.5 5.3c.3.4.8.5 1.3.3l.5-.2c.4-.3.6-.7.5-1.2z"/>'),
(N'package',     N'<path d="M16.5 9.4 7.55 4.24M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><path d="M3.27 6.96 12 12.01l8.73-5.05M12 22.08V12"/>');

/* ---------- Top-level categories ---------- */
IF OBJECT_ID('tempdb..#Cat') IS NOT NULL DROP TABLE #Cat;
CREATE TABLE #Cat (Slug nvarchar(140) PRIMARY KEY, Name nvarchar(120), Description nvarchar(1000), ColorHex nvarchar(9), TintHex nvarchar(9), IconKey nvarchar(40), SortOrder int, IsFeatured bit);
INSERT INTO #Cat VALUES
(N'home-services',          N'Home Services',          N'Electricians, plumbers, AC technicians, cleaners, painters and carpenters for every home.', N'#DC6803', N'#FDF2E7', N'home',        1, 1),
(N'healthcare',             N'Healthcare',             N'Doctors, clinics, hospitals, dentists, diagnostic labs and pharmacies near you.',         N'#0E9384', N'#E7F5F3', N'heart-pulse', 2, 1),
(N'education',              N'Education',              N'Home tutors, coaching centres, schools and career-focused skill training.',             N'#444CE7', N'#EEEFFD', N'graduation',  3, 1),
(N'beauty-wellness',        N'Beauty & Wellness',      N'Salons, at-home beauty services, spas, fitness trainers, yoga and physiotherapy.',       N'#DD2590', N'#FCE9F4', N'sparkles',    4, 1),
(N'legal-services',         N'Legal Services',         N'Advocates, legal consultants and notary services for individuals and businesses.',       N'#6938EF', N'#F1ECFE', N'scale',       5, 1),
(N'architecture-interiors', N'Architecture & Interiors', N'Architects and interior designers for homes, offices and commercial spaces.',          N'#4E5BA6', N'#EEF0F7', N'ruler',       6, 1),
(N'food-dining',            N'Food & Dining',          N'Restaurants, cafés, bakeries and caterers for every occasion.',                          N'#E04F16', N'#FDEEE8', N'utensils',    7, 1),
(N'events-weddings',        N'Events & Weddings',      N'Event planners, photographers and decorators for weddings and celebrations.',            N'#C11574', N'#FBE8F2', N'calendar',    8, 1),
(N'real-estate',            N'Real Estate',            N'RERA-registered property advisors and rental management services.',                      N'#099250', N'#E7F5EE', N'building',    9, 1),
(N'travel-transport',       N'Travel & Transport',     N'Taxi services, tour operators and packers & movers.',                                    N'#0086C9', N'#E6F3FA', N'taxi',       10, 1),
(N'automotive',             N'Automotive',             N'Car servicing, car wash and detailing, tyres and 24x7 roadside assistance.',             N'#1570EF', N'#E8F1FD', N'car',        11, 0);

MERGE dbo.Categories AS t
USING #Cat AS s
ON t.Slug = s.Slug
WHEN MATCHED AND (t.Name <> s.Name OR ISNULL(t.Description, N'') <> s.Description OR ISNULL(t.ColorHex, N'') <> s.ColorHex OR t.SortOrder <> s.SortOrder OR t.IsFeatured <> s.IsFeatured)
    THEN UPDATE SET Name = s.Name, Description = s.Description, ColorHex = s.ColorHex, SortOrder = s.SortOrder,
                    IsFeatured = s.IsFeatured, AltText = s.Name + N' services', ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, Name, Slug, Description, ColorHex, SortOrder, IsFeatured, AltText, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.Name, s.Slug, s.Description, s.ColorHex, s.SortOrder, s.IsFeatured, s.Name + N' services', 1, 0, N'seed', DATEADD(DAY, -520, SYSDATETIMEOFFSET()));

/* ---------- Sub-categories ---------- */
IF OBJECT_ID('tempdb..#Sub') IS NOT NULL DROP TABLE #Sub;
CREATE TABLE #Sub (Slug nvarchar(140), CategorySlug nvarchar(140), Name nvarchar(120), Description nvarchar(1000), IconKey nvarchar(40), SortOrder int, IsFeatured bit, IsActive bit, PRIMARY KEY (CategorySlug, Slug));
INSERT INTO #Sub VALUES
(N'electrical',         N'home-services', N'Electricians',          N'Wiring, MCB and switchboard repairs, fan and light fitting, inverter installation.', N'zap',        1, 1, 1),
(N'plumbing',           N'home-services', N'Plumbers',              N'Leak repairs, blockage removal, bathroom fittings and water tank cleaning.',      N'wrench',     2, 1, 1),
(N'ac-repair',          N'home-services', N'AC Repair',             N'AC servicing, gas refill, installation and annual maintenance contracts.',        N'snowflake',  3, 1, 1),
(N'cleaning',           N'home-services', N'Home Cleaning',         N'Deep cleaning, sofa shampooing, kitchen and bathroom cleaning.',                  N'sparkles',   4, 0, 1),
(N'painting',           N'home-services', N'Painters',              N'Interior and exterior painting, textures and waterproofing.',                     N'roller',     5, 0, 1),
(N'pest-control',       N'home-services', N'Pest Control',          N'Termite, cockroach, bed-bug and mosquito treatments.',                            N'bug',        6, 0, 1),
(N'carpenters',         N'home-services', N'Carpenters',            N'Custom furniture, wardrobes, repairs and polishing.',                             N'hammer',     7, 0, 1),
(N'doctors',            N'healthcare',    N'Doctors',               N'General physicians and specialists for in-clinic, video and home consultations.', N'stethoscope',1, 1, 1),
(N'clinics',            N'healthcare',    N'Clinics',               N'Neighbourhood OPD clinics, vaccinations and minor procedures.',                   N'clinic',     2, 1, 1),
(N'hospitals',          N'healthcare',    N'Hospitals',             N'Multispeciality hospitals with emergency and inpatient care.',                    N'hospital',   3, 1, 1),
(N'dental',             N'healthcare',    N'Dentists',              N'Dental check-ups, root canals, implants, braces and aligners.',                   N'tooth',      4, 0, 1),
(N'diagnostics',        N'healthcare',    N'Diagnostic Labs',       N'Blood tests, health packages, imaging and home sample collection.',               N'flask',      5, 0, 1),
(N'pharmacy',           N'healthcare',    N'Pharmacies',            N'Prescription medicines and wellness products with home delivery.',                N'pill',       6, 0, 1),
(N'private-tutors',     N'education',     N'Tutors',                N'Home and online tutors for school boards and competitive exams.',                 N'graduation', 1, 1, 1),
(N'test-preparation',   N'education',     N'Coaching Centres',      N'JEE, NEET, KCET and KEAM coaching with mock test series.',                        N'book-open',  2, 0, 1),
(N'skill-training',     N'education',     N'Skill Training',        N'Career-focused bootcamps in software, data and cloud.',                           N'laptop-code',3, 0, 1),
(N'schools',            N'education',     N'Schools',               N'Pre-primary and primary schools, day care and summer camps.',                     N'school',     4, 0, 1),
(N'beauty-salons',      N'beauty-wellness', N'Salons',              N'Haircuts, colouring, hair treatments, facials and bridal makeup.',                N'scissors',   1, 1, 1),
(N'beauty-services',    N'beauty-wellness', N'Beauty Services',     N'Waxing, facials, manicure-pedicure and makeup at your doorstep.',                 N'brush',      2, 1, 1),
(N'fitness',            N'beauty-wellness', N'Fitness Trainers',    N'Certified personal trainers for strength, fat loss and conditioning.',            N'dumbbell',   3, 1, 1),
(N'yoga',               N'beauty-wellness', N'Yoga Trainers',       N'Hatha, Vinyasa, prenatal and therapeutic yoga classes.',                          N'yoga',       4, 1, 1),
(N'spa',                N'beauty-wellness', N'Spa & Ayurveda',      N'Ayurvedic therapies and relaxing massages.',                                      N'leaf',       5, 0, 1),
(N'physiotherapy',      N'beauty-wellness', N'Physiotherapists',    N'Orthopaedic, sports and neuro rehabilitation at clinic or home.',                 N'activity',   6, 0, 1),
(N'lawyers',            N'legal-services', N'Lawyers',              N'Advocates for property, family, civil, criminal and corporate matters.',          N'scale',      1, 1, 1),
(N'legal-consultants',  N'legal-services', N'Legal Consultants',    N'GST, company law, contracts and trademark advisory.',                             N'briefcase',  2, 0, 1),
(N'notary-services',    N'legal-services', N'Notary Services',      N'Notarisation, affidavits, rent agreements and e-stamp papers.',                   N'stamp',      3, 0, 1),
(N'architects',         N'architecture-interiors', N'Architects',         N'Residential and commercial architecture with approvals support.',          N'ruler',      1, 1, 1),
(N'interior-designers', N'architecture-interiors', N'Interior Designers', N'Modular kitchens, wardrobes and full-home interiors.',                      N'sofa',       2, 1, 1),
(N'restaurants',        N'food-dining',   N'Restaurants',           N'Family dining, regional cuisines and party hall bookings.',                       N'utensils',   1, 1, 1),
(N'cafes',              N'food-dining',   N'Cafés & Bakeries',      N'Breakfast, coffee, bakes and quick bites.',                                       N'coffee',     2, 0, 1),
(N'caterers',           N'food-dining',   N'Caterers',              N'Wedding, festival and corporate catering.',                                       N'chef-hat',   3, 0, 1),
(N'event-planners',     N'events-weddings', N'Event Planners',      N'Wedding planning, destination weddings and corporate events.',                    N'calendar',   1, 1, 1),
(N'photographers',      N'events-weddings', N'Photographers',       N'Candid wedding, pre-wedding, maternity and product photography.',                 N'camera',     2, 0, 1),
(N'decorators',         N'events-weddings', N'Decorators',          N'Floral, stage, mandap and theme décor.',                                          N'flower',     3, 0, 1),
(N'real-estate-agents', N'real-estate',   N'Real Estate',           N'Buy, sell and rent homes with RERA-registered advisors.',                         N'key',        1, 1, 1),
(N'property-management',N'real-estate',   N'Property Management',   N'Tenant management and maintenance for landlords and NRIs.',                       N'building',   2, 0, 1),
(N'taxi-services',      N'travel-transport', N'Taxi Services',      N'Airport transfers, local packages and outstation cabs.',                          N'taxi',       1, 1, 1),
(N'tours-travels',      N'travel-transport', N'Tours & Travels',    N'Holiday packages, treks and pilgrimage tours.',                                   N'plane',      2, 0, 1),
(N'packers-movers',     N'travel-transport', N'Packers & Movers',   N'Household and office relocation, local and intercity.',                           N'package',    3, 0, 1),
(N'car-repair',         N'automotive',    N'Car Repair',            N'Periodic service, denting-painting and mechanical repairs.',                      N'car',        1, 0, 1),
(N'car-wash',           N'automotive',    N'Car Wash & Detailing',  N'Foam wash, interior detailing and ceramic coating.',                              N'droplets',   2, 0, 1),
(N'towing',             N'automotive',    N'Towing & Roadside',     N'Flatbed towing, jump-starts and breakdown assistance.',                           N'truck',      3, 0, 1),
(N'tyres',              N'automotive',    N'Tyres & Alignment',     N'Tyre replacement, wheel alignment and balancing.',                                N'tyre',       4, 0, 1);

MERGE dbo.SubCategories AS t
USING (SELECT s.*, c.Id AS CategoryId FROM #Sub s JOIN dbo.Categories c ON c.Slug = s.CategorySlug) AS s
ON t.Slug = s.Slug AND t.CategoryId = s.CategoryId
WHEN MATCHED AND (t.Name <> s.Name OR ISNULL(t.Description, N'') <> s.Description OR t.SortOrder <> s.SortOrder OR t.IsFeatured <> s.IsFeatured OR t.IsActive <> s.IsActive)
    THEN UPDATE SET Name = s.Name, Description = s.Description, SortOrder = s.SortOrder, IsFeatured = s.IsFeatured,
                    IsActive = s.IsActive, AltText = s.Name + N' near you', ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, CategoryId, Name, Slug, Description, SortOrder, IsFeatured, AltText, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.CategoryId, s.Name, s.Slug, s.Description, s.SortOrder, s.IsFeatured, s.Name + N' near you', s.IsActive, 0, N'seed', DATEADD(DAY, -520, SYSDATETIMEOFFSET()));

/* ---------- Artwork ---------- */
IF OBJECT_ID('tempdb..#Art') IS NOT NULL DROP TABLE #Art;
CREATE TABLE #Art (EntityType nvarchar(64), EntityId uniqueidentifier, FileName nvarchar(260), AltText nvarchar(300), Svg nvarchar(max));

-- Category card image (640x400), icon (24x24) and wide banner (1600x400)
INSERT INTO #Art
SELECT N'Category', c.Id, c.Slug + N'.svg', s.Name + N' category illustration',
       CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="640" height="400" viewBox="0 0 640 400">',
              N'<rect width="640" height="400" fill="', s.TintHex, N'"/>',
              N'<circle cx="540" cy="70" r="190" fill="', s.ColorHex, N'" fill-opacity="0.07"/>',
              N'<circle cx="70" cy="390" r="150" fill="', s.ColorHex, N'" fill-opacity="0.06"/>',
              N'<g transform="translate(230 110) scale(7.5)" fill="none" stroke="', s.ColorHex, N'" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round">', i.Markup, N'</g></svg>')
FROM #Cat s JOIN dbo.Categories c ON c.Slug = s.Slug JOIN #Icon i ON i.IconKey = s.IconKey
UNION ALL
SELECT N'CategoryIcon', c.Id, c.Slug + N'-icon.svg', s.Name + N' icon',
       CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="', s.ColorHex, N'" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">', i.Markup, N'</svg>')
FROM #Cat s JOIN dbo.Categories c ON c.Slug = s.Slug JOIN #Icon i ON i.IconKey = s.IconKey
UNION ALL
SELECT N'CategoryBanner', c.Id, c.Slug + N'-banner.svg', s.Name + N' banner',
       CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="400" viewBox="0 0 1600 400">',
              N'<rect width="1600" height="400" fill="#0B1220"/>',
              N'<circle cx="1380" cy="60" r="300" fill="', s.ColorHex, N'" fill-opacity="0.28"/>',
              N'<circle cx="1560" cy="380" r="180" fill="', s.ColorHex, N'" fill-opacity="0.18"/>',
              N'<g transform="translate(1260 80) scale(10)" fill="none" stroke="#FFFFFF" stroke-opacity="0.9" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round">', i.Markup, N'</g></svg>')
FROM #Cat s JOIN dbo.Categories c ON c.Slug = s.Slug JOIN #Icon i ON i.IconKey = s.IconKey;

-- Sub-category card image and icon, coloured by parent category
INSERT INTO #Art
SELECT N'SubCategory', sc.Id, s.Slug + N'.svg', s.Name + N' illustration',
       CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="640" height="400" viewBox="0 0 640 400">',
              N'<rect width="640" height="400" fill="', c.TintHex, N'"/>',
              N'<circle cx="', 480 + (s.SortOrder * 37) % 120, N'" cy="80" r="180" fill="', c.ColorHex, N'" fill-opacity="0.08"/>',
              N'<circle cx="90" cy="', 330 + (s.SortOrder * 23) % 60, N'" r="130" fill="', c.ColorHex, N'" fill-opacity="0.06"/>',
              N'<g transform="translate(230 110) scale(7.5)" fill="none" stroke="', c.ColorHex, N'" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round">', i.Markup, N'</g></svg>')
FROM #Sub s JOIN #Cat c ON c.Slug = s.CategorySlug
JOIN dbo.Categories cat ON cat.Slug = s.CategorySlug
JOIN dbo.SubCategories sc ON sc.Slug = s.Slug AND sc.CategoryId = cat.Id
JOIN #Icon i ON i.IconKey = s.IconKey
UNION ALL
SELECT N'SubCategoryIcon', sc.Id, s.Slug + N'-icon.svg', s.Name + N' icon',
       CONCAT(N'<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="', c.ColorHex, N'" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">', i.Markup, N'</svg>')
FROM #Sub s JOIN #Cat c ON c.Slug = s.CategorySlug
JOIN dbo.Categories cat ON cat.Slug = s.CategorySlug
JOIN dbo.SubCategories sc ON sc.Slug = s.Slug AND sc.CategoryId = cat.Id
JOIN #Icon i ON i.IconKey = s.IconKey;

MERGE dbo.Media AS t
USING (SELECT EntityType, EntityId, FileName, AltText,
              CAST(CAST(Svg COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max)) AS varbinary(max)) AS Bytes
       FROM #Art) AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = s.FileName
WHEN MATCHED AND (t.FileData <> s.Bytes OR ISNULL(t.AltText, N'') <> s.AltText)
    THEN UPDATE SET FileData = s.Bytes, ThumbnailData = s.Bytes, FileSize = DATALENGTH(s.Bytes), AltText = s.AltText,
                    IsActive = 1, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, s.FileName, N'image/svg+xml', N'.svg', DATALENGTH(s.Bytes), s.Bytes, s.Bytes, s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

UPDATE c SET
    ImageUrl  = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mi.MediaId)),
    IconUrl   = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mc.MediaId)),
    BannerUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mb.MediaId))
FROM dbo.Categories c
JOIN dbo.Media mi ON mi.EntityType = N'Category'       AND mi.EntityId = c.Id AND mi.FileName = c.Slug + N'.svg'
JOIN dbo.Media mc ON mc.EntityType = N'CategoryIcon'   AND mc.EntityId = c.Id AND mc.FileName = c.Slug + N'-icon.svg'
JOIN dbo.Media mb ON mb.EntityType = N'CategoryBanner' AND mb.EntityId = c.Id AND mb.FileName = c.Slug + N'-banner.svg';

UPDATE sc SET
    ImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mi.MediaId)),
    IconUrl  = N'/api/media/' + LOWER(CONVERT(nvarchar(36), mc.MediaId))
FROM dbo.SubCategories sc
JOIN dbo.Media mi ON mi.EntityType = N'SubCategory'     AND mi.EntityId = sc.Id AND mi.FileName = sc.Slug + N'.svg'
JOIN dbo.Media mc ON mc.EntityType = N'SubCategoryIcon' AND mc.EntityId = sc.Id AND mc.FileName = sc.Slug + N'-icon.svg';

COMMIT TRANSACTION;
PRINT '03_Categories.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #Icon, #Cat, #Sub, #Art;
GO
