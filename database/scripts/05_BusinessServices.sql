/* =====================================================================================
   Calling Bell - 05_BusinessServices.sql
   Service catalogue for every business, built from realistic per-sub-category templates.
   Prices vary per business (deterministically) to look like independent rate cards.
   Idempotent: unique (BusinessId, Name).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#T') IS NOT NULL DROP TABLE #T;
CREATE TABLE #T (SubSlug nvarchar(140), Name nvarchar(160), Description nvarchar(1000), Price decimal(12,2), PriceUnit nvarchar(40), DurationMinutes int, ServiceType nvarchar(32), IsPopular bit);
INSERT INTO #T VALUES
-- Healthcare
(N'doctors', N'In-clinic Consultation',      N'Detailed consultation with examination, prescription and follow-up advice.', 600, N'per visit', 20, N'InStore', 1),
(N'doctors', N'Video Consultation',          N'Secure video consultation with e-prescription shared on WhatsApp and e-mail.', 500, N'per session', 15, N'Online', 1),
(N'doctors', N'Home Visit Consultation',     N'Doctor visit at home for senior citizens and patients with limited mobility.', 1500, N'per visit', 45, N'AtHome', 0),
(N'doctors', N'Annual Health Review',        N'Comprehensive review of reports, medications and lifestyle plan for the year.', 2500, N'per package', 60, N'InStore', 0),
(N'doctors', N'Follow-up Consultation',      N'Review visit within 15 days of the first consultation.', 300, N'per visit', 15, N'InStore', 0),
(N'clinics', N'Doctor Consultation',         N'Walk-in or scheduled OPD consultation with a general physician.', 400, N'per visit', 15, N'InStore', 1),
(N'clinics', N'Child Vaccination',           N'IAP-schedule vaccinations with digital vaccination records.', 1200, N'per dose', 15, N'InStore', 1),
(N'clinics', N'Dressing & Minor Procedure',  N'Wound dressing, suture removal and minor procedures.', 500, N'per visit', 30, N'InStore', 0),
(N'clinics', N'Home Sample Collection',      N'Phlebotomist visit for blood and urine sample collection.', 250, N'per visit', 20, N'AtHome', 0),
(N'clinics', N'Teleconsultation',            N'Phone or video consultation with the duty doctor.', 350, N'per session', 15, N'Online', 0),
(N'hospitals', N'Specialist OPD Consultation', N'Consultation with cardiology, orthopaedics, gynaecology or general medicine specialists.', 900, N'per visit', 20, N'InStore', 1),
(N'hospitals', N'Executive Health Check-up', N'80+ parameters including ECG, echo, ultrasound and physician review.', 5500, N'per package', 180, N'InStore', 1),
(N'hospitals', N'Teleconsultation with Specialist', N'Video consultation with a hospital specialist.', 700, N'per session', 20, N'Online', 0),
(N'hospitals', N'Maternity Consultation',    N'Antenatal check-up with an obstetrician, including scan review.', 1000, N'per visit', 30, N'InStore', 0),
(N'dental', N'Dental Check-up & Cleaning',   N'Oral examination, scaling and polishing.', 1200, N'per visit', 45, N'InStore', 1),
(N'dental', N'Root Canal Treatment',         N'Single-sitting rotary RCT with digital X-ray.', 6500, N'per tooth', 90, N'InStore', 1),
(N'dental', N'Teeth Whitening',              N'In-office laser whitening up to 8 shades brighter.', 8000, N'per session', 60, N'InStore', 0),
(N'dental', N'Dental Implant Consultation',  N'CBCT-based implant planning and treatment estimate.', 800, N'per visit', 30, N'InStore', 0),
(N'dental', N'Clear Aligner Assessment',     N'3D scan and smile simulation for clear aligners.', 1500, N'per visit', 45, N'InStore', 0),
(N'diagnostics', N'Full Body Health Check',  N'72 tests including CBC, lipid, liver, kidney, thyroid and HbA1c.', 2999, N'per package', 30, N'InStore', 1),
(N'diagnostics', N'Home Blood Sample Collection', N'Certified phlebotomist visit with reports in 6 hours.', 199, N'per visit', 15, N'AtHome', 1),
(N'diagnostics', N'MRI Brain',               N'1.5T MRI with radiologist report in 24 hours.', 6500, N'per scan', 45, N'InStore', 0),
(N'diagnostics', N'Thyroid Profile (T3, T4, TSH)', N'Fasting not required. Same-day digital report.', 499, N'per test', 10, N'InStore', 0),
(N'diagnostics', N'Ultrasound Abdomen',      N'Whole abdomen ultrasound by a consultant radiologist.', 1400, N'per scan', 30, N'InStore', 0),
(N'pharmacy', N'Prescription Home Delivery', N'Upload a prescription and get medicines delivered in 30 minutes.', 49, N'per order', 30, N'AtHome', 1),
(N'pharmacy', N'BP & Sugar Check',           N'Blood pressure and random blood sugar check by a pharmacist.', 100, N'per visit', 10, N'InStore', 0),
(N'pharmacy', N'Monthly Medicine Refill',    N'Automatic monthly refill of regular medicines with reminders.', 99, N'per month', 15, N'Online', 0),
-- Home services
(N'electrical', N'Electrician Visit & Inspection', N'Diagnosis of electrical faults; visiting charge adjusted against the final bill.', 199, N'per visit', 30, N'AtHome', 1),
(N'electrical', N'Ceiling Fan Installation', N'Installation of a ceiling fan including canopy and regulator fitting.', 249, N'per fan', 30, N'AtHome', 1),
(N'electrical', N'MCB / DB Repair',          N'Repair or replacement of MCBs, RCCB and distribution boards.', 399, N'per job', 45, N'AtHome', 0),
(N'electrical', N'Inverter Installation',    N'Inverter and battery installation with load balancing.', 799, N'per unit', 60, N'AtHome', 0),
(N'electrical', N'Full House Wiring',        N'Concealed rewiring with ISI-marked FRLS cables for a 2 BHK.', 18000, N'starting at', 480, N'AtHome', 0),
(N'plumbing', N'Plumber Visit',              N'Inspection and minor repairs; visiting charge adjusted in the bill.', 199, N'per visit', 30, N'AtHome', 1),
(N'plumbing', N'Tap & Mixer Repair',         N'Repair or replacement of taps, mixers and health faucets.', 249, N'per job', 30, N'AtHome', 1),
(N'plumbing', N'Blockage Removal',           N'Drain, sink and toilet blockage removal with machine.', 499, N'per job', 45, N'AtHome', 0),
(N'plumbing', N'Overhead Tank Cleaning',     N'Mechanised cleaning and disinfection of water tanks up to 1,000 L.', 899, N'per tank', 90, N'AtHome', 0),
(N'plumbing', N'Bathroom Fittings Installation', N'Installation of WC, wash basin, shower and accessories.', 1499, N'per bathroom', 180, N'AtHome', 0),
(N'ac-repair', N'AC General Service',        N'Filter, coil and drain cleaning with performance check.', 499, N'per AC', 45, N'AtHome', 1),
(N'ac-repair', N'AC Gas Refill',             N'Leak test and complete gas refill (R32/R410A).', 2499, N'per AC', 60, N'AtHome', 1),
(N'ac-repair', N'Split AC Installation',     N'Installation with up to 3 ft copper piping and stand.', 1499, N'per AC', 120, N'AtHome', 0),
(N'ac-repair', N'AC Jet Deep Cleaning',      N'High-pressure jet cleaning of indoor and outdoor units.', 799, N'per AC', 60, N'AtHome', 0),
(N'ac-repair', N'Annual Maintenance Contract', N'Three preventive services and priority breakdown support for a year.', 2999, N'per year', 60, N'AtHome', 0),
(N'cleaning', N'Full Home Deep Cleaning (2 BHK)', N'Floors, kitchen, bathrooms, windows and furniture with machines.', 4499, N'per home', 360, N'AtHome', 1),
(N'cleaning', N'Bathroom Deep Cleaning',     N'Tile descaling, fixtures polishing and disinfection.', 699, N'per bathroom', 90, N'AtHome', 1),
(N'cleaning', N'Sofa Shampooing',            N'Shampoo and vacuum extraction for a 5-seater sofa.', 1199, N'per sofa set', 120, N'AtHome', 0),
(N'cleaning', N'Kitchen Deep Cleaning',      N'Chimney, hob, cabinets and tile degreasing.', 1599, N'per kitchen', 180, N'AtHome', 0),
(N'painting', N'Painting Consultation & Estimate', N'Site visit, colour consultation and detailed estimate.', 199, N'per visit', 30, N'AtHome', 1),
(N'painting', N'Interior Painting (2 BHK)',  N'Two coats of premium emulsion with putty touch-up.', 22000, N'starting at', 480, N'AtHome', 1),
(N'painting', N'Texture Wall Design',        N'Designer texture finish for a feature wall.', 3500, N'per wall', 240, N'AtHome', 0),
(N'painting', N'Terrace Waterproofing',      N'Polymer waterproof coating with 5-year warranty.', 45, N'per sq ft', 240, N'AtHome', 0),
(N'pest-control', N'Cockroach Control',      N'Odourless gel treatment for kitchen and bathrooms.', 999, N'per home', 60, N'AtHome', 1),
(N'pest-control', N'Termite Treatment',      N'Drill-fill-seal anti-termite treatment with warranty.', 3999, N'per home', 180, N'AtHome', 1),
(N'pest-control', N'Bed Bug Treatment',      N'Two-visit bed bug treatment for mattresses and furniture.', 1799, N'per home', 120, N'AtHome', 0),
(N'pest-control', N'Mosquito Control',       N'Indoor and outdoor fogging and larvicide treatment.', 899, N'per home', 60, N'AtHome', 0),
(N'carpenters', N'Carpenter Visit',          N'Inspection and minor repairs.', 199, N'per visit', 30, N'AtHome', 1),
(N'carpenters', N'Furniture Repair',         N'Repair of hinges, channels, joints and drawers.', 499, N'per job', 60, N'AtHome', 1),
(N'carpenters', N'Door Lock Installation',   N'Installation of mortise or digital door locks.', 349, N'per door', 45, N'AtHome', 0),
(N'carpenters', N'Custom Wardrobe',          N'BWP plywood wardrobe with laminate finish.', 1350, N'per sq ft', 480, N'AtHome', 0),
-- Education
(N'private-tutors', N'Home Tuition - Classes 6 to 10', N'Maths and Science tuition at home, 3 sessions a week.', 6000, N'per month', 60, N'AtHome', 1),
(N'private-tutors', N'Online Tuition - Classes 11 & 12', N'Physics, Chemistry and Maths with recorded sessions.', 8000, N'per month', 60, N'Online', 1),
(N'private-tutors', N'Trial Class',          N'One trial session to assess the student and plan learning.', 199, N'per session', 60, N'Online', 0),
(N'private-tutors', N'Board Exam Crash Course', N'Revision and sample-paper practice before board exams.', 12000, N'per course', 90, N'Online', 0),
(N'test-preparation', N'JEE Main + Advanced (2-Year)', N'Two-year classroom programme with weekly tests.', 165000, N'per programme', 180, N'InStore', 1),
(N'test-preparation', N'NEET Programme',      N'Biology-focused programme with NCERT mastery and mock tests.', 120000, N'per programme', 180, N'InStore', 1),
(N'test-preparation', N'Mock Test Series',    N'Weekly all-India pattern mock tests with analytics.', 6000, N'per year', 180, N'Online', 0),
(N'test-preparation', N'Crash Course',        N'Eight-week intensive revision before the exam.', 25000, N'per course', 180, N'InStore', 0),
(N'skill-training', N'Full-Stack Web Development Bootcamp', N'24-week bootcamp in React, Node.js and cloud deployment.', 65000, N'per course', 120, N'Online', 1),
(N'skill-training', N'Data Science & AI Programme', N'Python, statistics, ML and GenAI with capstone projects.', 75000, N'per course', 120, N'Online', 1),
(N'skill-training', N'AWS Certification Prep', N'Solutions Architect Associate preparation with labs.', 25000, N'per course', 90, N'Online', 0),
(N'skill-training', N'Career Counselling Session', N'One-on-one session to plan a tech career switch.', 499, N'per session', 30, N'Online', 0),
(N'schools', N'Pre-Primary Admission',       N'Annual tuition fee for Playgroup to UKG.', 85000, N'per year', 60, N'InStore', 1),
(N'schools', N'Primary Admission',           N'Annual tuition fee for Grades 1 to 5.', 110000, N'per year', 60, N'InStore', 1),
(N'schools', N'After-School Day Care',       N'Supervised day care with meals and homework support.', 6500, N'per month', 240, N'InStore', 0),
(N'schools', N'Summer Camp',                 N'Four-week camp with art, robotics and sports.', 7500, N'per camp', 240, N'InStore', 0),
-- Beauty & wellness
(N'beauty-salons', N'Haircut & Styling',     N'Consultation, wash, cut and blow-dry by a senior stylist.', 499, N'per session', 45, N'InStore', 1),
(N'beauty-salons', N'Global Hair Colour',    N'Ammonia-free global colour with toner.', 2999, N'per session', 120, N'InStore', 1),
(N'beauty-salons', N'Keratin Treatment',     N'Smoothening keratin treatment for frizz-free hair.', 4999, N'per session', 180, N'InStore', 0),
(N'beauty-salons', N'Classic Facial',        N'Cleansing, exfoliation, massage and mask.', 1299, N'per session', 60, N'InStore', 0),
(N'beauty-salons', N'Bridal Makeup',         N'HD bridal makeup with hairstyling and draping.', 15000, N'per event', 180, N'InStore', 0),
(N'beauty-services', N'Full Arms & Legs Waxing', N'Rica waxing with single-use spatulas.', 799, N'per session', 60, N'AtHome', 1),
(N'beauty-services', N'Fruit Facial at Home', N'Fruit facial with cleanup and massage.', 999, N'per session', 60, N'AtHome', 1),
(N'beauty-services', N'Manicure & Pedicure', N'Spa manicure and pedicure with nail shaping.', 899, N'per session', 75, N'AtHome', 0),
(N'beauty-services', N'Party Makeup',        N'HD party makeup with hairstyling.', 2999, N'per event', 90, N'AtHome', 0),
(N'beauty-services', N'Bridal HD Makeup',    N'Bridal HD makeup with trial session.', 18000, N'per event', 180, N'AtHome', 0),
(N'spa', N'Abhyangam Full-Body Massage',     N'Traditional Ayurvedic oil massage by two therapists.', 2200, N'per session', 60, N'InStore', 1),
(N'spa', N'Shirodhara',                      N'Continuous warm medicated oil therapy for stress relief.', 2800, N'per session', 60, N'InStore', 1),
(N'spa', N'Swedish Massage',                 N'Relaxing full-body massage with aromatic oils.', 2500, N'per session', 60, N'InStore', 0),
(N'spa', N'Panchakarma (7 Days)',            N'Physician-supervised detox programme.', 24000, N'per programme', 120, N'InStore', 0),
(N'fitness', N'Personal Training Session',   N'One-on-one session with a certified trainer.', 900, N'per session', 60, N'AtHome', 1),
(N'fitness', N'Monthly Coaching (12 sessions)', N'Three sessions a week with progress tracking.', 9500, N'per month', 60, N'AtHome', 1),
(N'fitness', N'Online Fitness Coaching',     N'Live online sessions with a customised plan.', 3500, N'per month', 45, N'Online', 0),
(N'fitness', N'Diet & Nutrition Plan',       N'Personalised Indian diet plan by a registered dietitian.', 1999, N'per plan', 30, N'Online', 0),
(N'yoga', N'Group Yoga Class',               N'Monthly membership for morning or evening batches.', 2500, N'per month', 60, N'InStore', 1),
(N'yoga', N'Private Yoga Session',           N'One-on-one session at home.', 1200, N'per session', 60, N'AtHome', 1),
(N'yoga', N'Prenatal Yoga',                  N'Safe, guided yoga for expecting mothers.', 3000, N'per month', 60, N'InStore', 0),
(N'yoga', N'Online Live Yoga',               N'Live sessions on video, five days a week.', 1500, N'per month', 60, N'Online', 0),
(N'physiotherapy', N'Physiotherapy Session', N'Assessment and treatment at the clinic.', 800, N'per session', 45, N'InStore', 1),
(N'physiotherapy', N'Home Physiotherapy',    N'Physiotherapist visit at home with portable equipment.', 1200, N'per session', 45, N'AtHome', 1),
(N'physiotherapy', N'Sports Injury Assessment', N'Detailed assessment with a return-to-sport plan.', 1000, N'per visit', 60, N'InStore', 0),
(N'physiotherapy', N'Post-Surgery Rehab (10 sessions)', N'Rehabilitation package after knee or hip surgery.', 9000, N'per package', 45, N'InStore', 0),
-- Legal
(N'lawyers', N'Legal Consultation',          N'In-office consultation with a practising advocate.', 2000, N'per session', 45, N'InStore', 1),
(N'lawyers', N'Video Legal Consultation',    N'Video consultation with document review.', 1500, N'per session', 30, N'Online', 1),
(N'lawyers', N'Legal Notice Drafting',       N'Drafting and dispatch of a legal notice.', 5000, N'per notice', 120, N'Online', 0),
(N'lawyers', N'Property Document Verification', N'Title search and legal opinion for a property.', 7500, N'per property', 240, N'InStore', 0),
(N'legal-consultants', N'GST Registration & Advisory', N'GST registration with three months of advisory.', 3500, N'per engagement', 60, N'Online', 1),
(N'legal-consultants', N'Company Incorporation', N'Private limited incorporation with DSC, DIN and PAN/TAN.', 9999, N'per company', 120, N'Online', 1),
(N'legal-consultants', N'Trademark Registration', N'Search, filing and follow-up for one class.', 6500, N'per mark', 60, N'Online', 0),
(N'legal-consultants', N'Contract Review',   N'Review and redlining of commercial contracts.', 4000, N'per contract', 90, N'Online', 0),
(N'notary-services', N'Affidavit Notarisation', N'Drafting and notarisation of affidavits.', 300, N'per document', 15, N'InStore', 1),
(N'notary-services', N'Rent Agreement with E-Stamp', N'Rent agreement drafting with e-stamp paper.', 999, N'per agreement', 30, N'InStore', 1),
(N'notary-services', N'Document Attestation', N'Attestation of certificates and copies.', 200, N'per document', 15, N'InStore', 0),
-- Architecture & interiors
(N'architects', N'Architecture Consultation', N'Site analysis and concept discussion with the principal architect.', 3000, N'per session', 60, N'Consultation', 1),
(N'architects', N'Residential Design',        N'Complete architectural drawings and approvals support.', 85, N'per sq ft', 120, N'Consultation', 1),
(N'architects', N'3D Elevation Design',      N'Photorealistic 3D elevation with two revisions.', 25000, N'per design', 120, N'Online', 0),
(N'architects', N'Site Supervision',         N'Weekly site visits and quality checks during construction.', 30000, N'per month', 120, N'AtHome', 0),
(N'interior-designers', N'Design Consultation', N'Home visit, measurements and design brief.', 1500, N'per session', 60, N'Consultation', 1),
(N'interior-designers', N'Modular Kitchen',  N'L-shaped modular kitchen with soft-close hardware.', 165000, N'starting at', 120, N'AtHome', 1),
(N'interior-designers', N'2 BHK Full Interiors', N'Kitchen, wardrobes, TV unit, false ceiling and lighting.', 650000, N'starting at', 120, N'AtHome', 0),
(N'interior-designers', N'False Ceiling',    N'Gypsum false ceiling with cove lighting.', 120, N'per sq ft', 240, N'AtHome', 0),
-- Food & dining
(N'restaurants', N'Table Reservation',       N'Reserve a table for up to six guests.', 0, N'free', 90, N'InStore', 1),
(N'restaurants', N'Family Meal Combo (4 pax)', N'Chef''s selection of starters, mains and dessert for four.', 1499, N'per combo', 90, N'InStore', 1),
(N'restaurants', N'Party Hall Booking',      N'Private hall for up to 60 guests with a set menu.', 25000, N'per event', 240, N'InStore', 0),
(N'cafes', N'Breakfast Combo',               N'Two idlis, vada, mini dosa and filter coffee.', 180, N'per person', 30, N'InStore', 1),
(N'cafes', N'Filter Coffee Flask (1 L)',     N'Fresh decoction coffee for office meetings.', 350, N'per flask', 15, N'InStore', 0),
(N'caterers', N'Wedding Feast',              N'Traditional banana-leaf meal with 21 items.', 650, N'per plate', 240, N'AtHome', 1),
(N'caterers', N'Corporate Lunch',            N'Packed or buffet lunch for offices.', 320, N'per plate', 120, N'AtHome', 1),
(N'caterers', N'Tasting Session',            N'Menu tasting for up to four people.', 1500, N'per session', 60, N'InStore', 0),
-- Events
(N'event-planners', N'Planning Consultation', N'Discovery meeting, budget planning and vendor recommendations.', 2000, N'per session', 60, N'Consultation', 1),
(N'event-planners', N'Wedding Planning Package', N'End-to-end planning and coordination for a two-day wedding.', 250000, N'starting at', 120, N'Consultation', 1),
(N'event-planners', N'Birthday & Anniversary Party', N'Venue, décor, entertainment and catering coordination.', 45000, N'starting at', 120, N'AtHome', 0),
(N'event-planners', N'Corporate Offsite',    N'Venue, travel and activity planning for teams.', 150000, N'starting at', 120, N'Consultation', 0),
(N'photographers', N'Candid Wedding Photography', N'Two photographers, edited photos and a premium album.', 85000, N'per event', 480, N'AtHome', 1),
(N'photographers', N'Pre-Wedding Shoot',     N'Half-day shoot at two locations with 50 edited photos.', 35000, N'per shoot', 360, N'AtHome', 1),
(N'photographers', N'Maternity & Newborn Shoot', N'Studio shoot with props and 30 edited photos.', 15000, N'per shoot', 180, N'InStore', 0),
(N'decorators', N'Mandap Floral Décor',       N'Fresh-flower mandap with stage and entrance décor.', 75000, N'starting at', 480, N'AtHome', 1),
(N'decorators', N'Birthday Theme Décor',      N'Themed backdrop, balloons and table décor.', 9500, N'per event', 180, N'AtHome', 1),
(N'decorators', N'Balloon Décor',             N'Balloon arch and room décor.', 4500, N'per event', 120, N'AtHome', 0),
-- Real estate
(N'real-estate-agents', N'Guided Site Visit', N'Accompanied visits to shortlisted properties.', 0, N'free', 60, N'AtHome', 1),
(N'real-estate-agents', N'Property Valuation', N'Market valuation report for sale or loan purposes.', 3500, N'per property', 90, N'Consultation', 1),
(N'real-estate-agents', N'Home Loan Assistance', N'Loan eligibility check and bank coordination.', 2500, N'per case', 60, N'Consultation', 0),
(N'property-management', N'Rental Management', N'Rent collection, maintenance and tenant communication.', 2500, N'per month', 60, N'Consultation', 1),
(N'property-management', N'Tenant Screening & Agreement', N'Background check, agreement and police verification.', 5000, N'per tenant', 60, N'Consultation', 1),
(N'property-management', N'Property Inspection Visit', N'Detailed inspection with photo report.', 1500, N'per visit', 60, N'AtHome', 0),
-- Travel & transport
(N'taxi-services', N'Airport Transfer (Sedan)', N'Fixed-fare pickup or drop to the airport.', 1199, N'per trip', 60, N'AtHome', 1),
(N'taxi-services', N'Local 8 hrs / 80 km',     N'Sedan with driver for local travel.', 2499, N'per package', 480, N'AtHome', 1),
(N'taxi-services', N'Outstation Sedan',        N'One-way or round trip, driver allowance extra.', 13, N'per km', 480, N'AtHome', 0),
(N'taxi-services', N'Tempo Traveller (12 seater)', N'For family trips and group travel.', 28, N'per km', 480, N'AtHome', 0),
(N'tours-travels', N'Weekend Trek Package',    N'Guided trek with transport, breakfast and first aid.', 2499, N'per person', 480, N'InStore', 1),
(N'tours-travels', N'Konkan Holiday (3N/4D)',  N'Hotel stay, sightseeing and AC coach travel.', 14999, N'per person', 480, N'InStore', 1),
(N'tours-travels', N'Pilgrimage Tour',         N'Shirdi and Ashtavinayak darshan tour.', 5499, N'per person', 480, N'InStore', 0),
(N'packers-movers', N'1 BHK Local Shifting',   N'Packing, loading, transport and unpacking within the city.', 8999, N'per move', 360, N'AtHome', 1),
(N'packers-movers', N'2 BHK Local Shifting',   N'Multi-layer packing with two trucks if needed.', 13999, N'per move', 480, N'AtHome', 1),
(N'packers-movers', N'Intercity Relocation',   N'Door-to-door relocation with transit insurance.', 32000, N'starting at', 480, N'AtHome', 0),
(N'packers-movers', N'Car Transport',          N'Enclosed car carrier transport between cities.', 9500, N'per car', 480, N'AtHome', 0),
-- Automotive
(N'car-repair', N'Periodic Car Service',       N'Engine oil, filters, 40-point inspection and wash.', 3999, N'per service', 240, N'InStore', 1),
(N'car-repair', N'Denting & Painting',         N'Dent removal and paint for one panel.', 2800, N'per panel', 480, N'InStore', 1),
(N'car-repair', N'Clutch Overhaul',            N'Clutch plate, pressure plate and bearing replacement.', 6500, N'per job', 360, N'InStore', 0),
(N'car-wash', N'Foam Car Wash',                N'Exterior foam wash, tyre polish and vacuum.', 499, N'per car', 45, N'InStore', 1),
(N'car-wash', N'Interior Detailing',           N'Steam cleaning of seats, roof and carpets.', 1999, N'per car', 150, N'InStore', 1),
(N'car-wash', N'Ceramic Coating',              N'9H ceramic coating with 3-year warranty.', 18000, N'per car', 480, N'InStore', 0),
(N'car-wash', N'Doorstep Car Wash',            N'Waterless wash at your parking spot.', 599, N'per car', 45, N'AtHome', 0),
(N'towing', N'Flatbed Towing (City)',          N'Flatbed towing within city limits.', 2499, N'per trip', 90, N'AtHome', 1),
(N'towing', N'Battery Jump-Start',             N'Jump-start at your location.', 699, N'per visit', 30, N'AtHome', 1),
(N'towing', N'Flat Tyre Assistance',           N'Spare wheel change or puncture repair on site.', 499, N'per visit', 30, N'AtHome', 0),
(N'tyres', N'Wheel Alignment & Balancing',     N'Computerised 3D alignment and balancing.', 799, N'per car', 45, N'InStore', 1),
(N'tyres', N'Tyre Fitting',                    N'Fitting of a new tyre with valve change.', 300, N'per tyre', 20, N'InStore', 1),
(N'tyres', N'Nitrogen Filling',                N'Nitrogen filling for all four tyres.', 200, N'per car', 15, N'InStore', 0);

;WITH src AS (
    SELECT b.Id AS BusinessId, b.Slug, t.*, sc.ImageUrl,
           0.90 + (ABS(CHECKSUM(b.Slug)) % 25) / 100.0 AS Factor
    FROM dbo.Businesses b
    JOIN dbo.SubCategories sc ON sc.Id = b.SubCategoryId
    JOIN #T t ON t.SubSlug = sc.Slug
)
MERGE dbo.BusinessServices AS t
USING (
    SELECT BusinessId, Name, Description, PriceUnit, DurationMinutes, ServiceType, IsPopular, ImageUrl,
           CAST(CASE
                    WHEN Price = 0     THEN 0
                    WHEN Price < 150   THEN ROUND(Price * Factor, 0)
                    WHEN Price < 10000 THEN ROUND(Price * Factor / 10, 0) * 10 - 1
                    ELSE ROUND(Price * Factor / 500, 0) * 500
                END AS decimal(12,2)) AS Price
    FROM src
) AS s
ON t.BusinessId = s.BusinessId AND t.Name = s.Name
WHEN MATCHED AND (t.Description <> s.Description OR ISNULL(t.PriceUnit, N'') <> s.PriceUnit OR ISNULL(t.ImageUrl, N'') <> ISNULL(s.ImageUrl, N''))
    THEN UPDATE SET Description = s.Description, PriceUnit = s.PriceUnit, ImageUrl = s.ImageUrl, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Id, BusinessId, Name, Description, Price, PriceUnit, DurationMinutes, Type, ImageUrl, IsPopular, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.BusinessId, s.Name, s.Description, s.Price, s.PriceUnit, s.DurationMinutes, s.ServiceType, s.ImageUrl, s.IsPopular, 1, 0, N'seed',
                 (SELECT DATEADD(DAY, 1, b.CreatedOn) FROM dbo.Businesses b WHERE b.Id = s.BusinessId));

COMMIT TRANSACTION;
PRINT '05_BusinessServices.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #T;
GO
