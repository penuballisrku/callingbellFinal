/* =====================================================================================
   Calling Bell - 07_Reviews.sql
   Customer reviews with ratings, owner replies and moderation states, then recalculates
   Businesses.AverageRating / ReviewCount.

   Requires 08_Users.sql (customers) - see RunAll.sql for execution order.
   Generation is deterministic (CHECKSUM-based), so re-running produces the same data;
   the unique key (BusinessId, CustomerUserId) prevents duplicates.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Now datetimeoffset = SYSDATETIMEOFFSET();

IF OBJECT_ID('tempdb..#R') IS NOT NULL DROP TABLE #R;
CREATE TABLE #R (CatSlug nvarchar(140), Rating tinyint, Title nvarchar(150), Comment nvarchar(2000));
INSERT INTO #R VALUES
(N'*', 5, N'Excellent service', N'Professional, punctual and courteous. Exactly what I was looking for.'),
(N'*', 5, N'Highly recommended', N'Smooth experience from booking to completion. Will recommend to friends and family.'),
(N'*', 4, N'Good experience', N'Overall a good experience with clear communication and fair pricing.'),
(N'*', 3, N'It was okay', N'Service was okay. There is room for improvement in follow-up.'),
(N'home-services', 5, N'Quick and professional', N'The technician arrived within the promised slot, explained the problem clearly and fixed it in under an hour. Very neat work and fair pricing.'),
(N'home-services', 5, N'Honest pricing', N'They quoted upfront and did not add any hidden charges. Even cleaned up after finishing the job. Will definitely call them again.'),
(N'home-services', 5, N'Saved our weekend', N'Called them on a Sunday morning and someone reached in 40 minutes. Problem sorted the same day.'),
(N'home-services', 4, N'Good work, slight delay', N'Work quality was good and the staff were polite. They came about 30 minutes after the slot, but kept me informed.'),
(N'home-services', 4, N'Reliable service', N'Second time using them. Consistent quality and reasonable rates. Booking through Calling Bell was easy.'),
(N'home-services', 3, N'Average experience', N'The issue was fixed but I had to follow up twice for the bill. Work itself was okay.'),
(N'home-services', 2, N'Needed a repeat visit', N'The problem came back after two days. They did come again without charge, but it was inconvenient.'),
(N'home-services', 1, N'Did not turn up', N'Booked a slot but nobody came and the phone was not answered. Had to call someone else.'),
(N'healthcare', 5, N'Very thorough doctor', N'Took time to understand my history and explained the treatment clearly. No unnecessary tests were prescribed.'),
(N'healthcare', 5, N'Clean and well organised', N'Minimal waiting time, polite staff and a very clean facility. Reports were shared on WhatsApp the same evening.'),
(N'healthcare', 5, N'Excellent video consultation', N'The video consultation started on time and the e-prescription arrived within minutes. Very convenient for my parents.'),
(N'healthcare', 4, N'Good care, busy clinic', N'Doctor is excellent but evenings are crowded. Booking a slot online helped a lot.'),
(N'healthcare', 4, N'Helpful staff', N'Front desk staff helped with the insurance paperwork. Overall a good experience.'),
(N'healthcare', 3, N'Long wait', N'Treatment was fine but I waited almost an hour beyond my appointment time.'),
(N'healthcare', 2, N'Billing not clear', N'Consultation was okay but the final bill had charges that were not explained beforehand.'),
(N'healthcare', 1, N'Poor coordination', N'Appointment was rescheduled twice without notice. Not happy with the coordination.'),
(N'education', 5, N'Marks improved a lot', N'Within three months my son''s maths score went from 62 to 88. The tutor is patient and very structured.'),
(N'education', 5, N'Excellent faculty', N'Concepts are taught from the basics and the weekly tests keep students on track. Doubt sessions are very helpful.'),
(N'education', 5, N'Great for working parents', N'Regular progress reports and flexible timings. The online classes work really well.'),
(N'education', 4, N'Good teaching, slightly pricey', N'Teaching quality is very good, though fees are slightly on the higher side.'),
(N'education', 4, N'Well-organised classes', N'Study material is good and classes start on time. Would like more practice papers.'),
(N'education', 3, N'Mixed experience', N'Some teachers are excellent, others less so. Batch sizes could be smaller.'),
(N'education', 2, N'Frequent tutor changes', N'The tutor changed twice in two months, which disturbed my daughter''s preparation.'),
(N'education', 1, N'Not as promised', N'The trial class was good but regular classes were irregular. Asked for a refund.'),
(N'beauty-wellness', 5, N'Loved the result', N'The stylist understood exactly what I wanted. Great products and a very hygienic set-up.'),
(N'beauty-wellness', 5, N'Best trainer so far', N'Knowledgeable and motivating. I have lost 6 kg in two months with proper diet guidance.'),
(N'beauty-wellness', 5, N'Relaxing and professional', N'Therapists were professional and the ambience was calm. Felt completely refreshed.'),
(N'beauty-wellness', 4, N'Good service', N'Nice experience overall. A short wait despite booking, but the quality made up for it.'),
(N'beauty-wellness', 4, N'Value for money', N'Good quality at reasonable prices. Will be back for my next appointment.'),
(N'beauty-wellness', 3, N'Okay experience', N'Service was decent but felt a bit rushed towards the end.'),
(N'beauty-wellness', 2, N'Not happy with the colour', N'The colour came out darker than discussed. They offered a correction session.'),
(N'beauty-wellness', 1, N'Expected better hygiene', N'Towels were not fresh and the staff were inattentive. Expected much better.'),
(N'legal-services', 5, N'Clear and practical advice', N'Explained my property dispute options in simple language and gave a realistic timeline. Very professional.'),
(N'legal-services', 5, N'Responsive and thorough', N'Documents were drafted quickly and every query was answered on the same day.'),
(N'legal-services', 5, N'Smooth incorporation', N'Handled our company incorporation end to end. Smooth and transparent process.'),
(N'legal-services', 4, N'Knowledgeable advocate', N'Very knowledgeable. Getting an appointment can take a few days.'),
(N'legal-services', 4, N'Good guidance', N'Helpful consultation with clear next steps. Fees were reasonable.'),
(N'legal-services', 3, N'Slow follow-up', N'Advice was good but follow-up communication was slow.'),
(N'legal-services', 2, N'Delays in drafting', N'The agreement took a week longer than promised.'),
(N'legal-services', 1, N'Not satisfied', N'Felt the consultation was rushed and generic.'),
(N'architecture-interiors', 5, N'Our dream home', N'From 3D designs to final handover everything was on schedule. The modular kitchen is stunning.'),
(N'architecture-interiors', 5, N'Thoughtful design', N'They understood our family''s needs and made great use of every square foot.'),
(N'architecture-interiors', 5, N'Transparent and on budget', N'Itemised quotation with no surprises. The site supervisor was always reachable.'),
(N'architecture-interiors', 4, N'Great design, minor delays', N'Design quality is excellent. Execution took two weeks longer than planned.'),
(N'architecture-interiors', 4, N'Good quality materials', N'Materials and finish are good. A few snags were fixed promptly after handover.'),
(N'architecture-interiors', 3, N'Decent work', N'Design was good but coordination with the site team needs improvement.'),
(N'architecture-interiors', 2, N'Finishing issues', N'Several finishing issues needed rework and multiple follow-ups.'),
(N'architecture-interiors', 1, N'Missed timelines', N'Project ran two months late without clear communication.'),
(N'food-dining', 5, N'Authentic flavours', N'Some of the best food we have had in a long time. Generous portions and quick service.'),
(N'food-dining', 5, N'Perfect for family dinners', N'Spacious seating, courteous staff and delicious food. The kids loved it.'),
(N'food-dining', 5, N'Excellent catering', N'Catered our housewarming for 150 guests. Food was fresh, hot and on time.'),
(N'food-dining', 4, N'Tasty food', N'Food was very tasty. Weekend wait times are long, so reserve a table.'),
(N'food-dining', 4, N'Good value', N'Good portions at fair prices. Desserts could be better.'),
(N'food-dining', 3, N'Hit and miss', N'Starters were great but the main course was too salty.'),
(N'food-dining', 2, N'Slow service', N'Food was okay but service was very slow on a weekday afternoon.'),
(N'food-dining', 1, N'Disappointing', N'Our order was wrong and the staff were not apologetic.'),
(N'events-weddings', 5, N'Flawless wedding', N'They managed 600 guests across three functions without a single hiccup. The décor was breathtaking.'),
(N'events-weddings', 5, N'Stunning photos', N'Candid shots captured every emotion. The album was delivered before the promised date.'),
(N'events-weddings', 5, N'Stress-free planning', N'Vendor coordination, timelines and budgets were all handled. We could simply enjoy our day.'),
(N'events-weddings', 4, N'Beautiful décor', N'Décor looked lovely. Set-up finished just before guests arrived, which was stressful.'),
(N'events-weddings', 4, N'Professional team', N'Professional and creative team. Pricing was a little high.'),
(N'events-weddings', 3, N'Okay overall', N'The event went fine, though a few promised items were missing.'),
(N'events-weddings', 2, N'Communication gaps', N'Had to repeat requirements multiple times to different coordinators.'),
(N'events-weddings', 1, N'Not recommended', N'Vendors were changed at the last minute without informing us.'),
(N'real-estate', 5, N'Found the perfect flat', N'Shortlisted genuine options within our budget and handled the paperwork smoothly.'),
(N'real-estate', 5, N'Very trustworthy', N'Verified every document before we paid the token amount. Great for first-time buyers.'),
(N'real-estate', 5, N'Hassle-free renting', N'Tenant screening and rent collection are fully handled. Ideal for NRIs.'),
(N'real-estate', 4, N'Good options', N'Showed good properties. Some listings were already taken by the time we visited.'),
(N'real-estate', 4, N'Helpful advisor', N'Patient and knowledgeable about the locality.'),
(N'real-estate', 3, N'Took a while', N'Took longer than expected to find a suitable property.'),
(N'real-estate', 2, N'Felt rushed', N'Felt pressured to decide quickly.'),
(N'real-estate', 1, N'Misleading listing', N'Photos did not match the actual property.'),
(N'travel-transport', 5, N'On time, every time', N'Driver reached 15 minutes early for my 4 am airport drop. Clean car and safe driving.'),
(N'travel-transport', 5, N'Smooth relocation', N'Everything was packed carefully and nothing was damaged. Unpacking was done neatly too.'),
(N'travel-transport', 5, N'Great trip', N'Well-planned itinerary and a courteous driver. Will book again for our next holiday.'),
(N'travel-transport', 4, N'Comfortable ride', N'Comfortable car and polite driver. The AC could have been better.'),
(N'travel-transport', 4, N'Good service', N'Fair pricing and an on-time pickup.'),
(N'travel-transport', 3, N'Bit late', N'Driver was late by 20 minutes but the ride itself was fine.'),
(N'travel-transport', 2, N'Unexpected extra charges', N'Was charged extra toll and parking that was not mentioned earlier.'),
(N'travel-transport', 1, N'Cancelled last minute', N'Booking was cancelled an hour before pickup.'),
(N'automotive', 5, N'Car runs like new', N'Periodic service was thorough and they explained every replaced part with photos.'),
(N'automotive', 5, N'Rescued at midnight', N'Battery died on the highway at midnight. They reached in 35 minutes and got me going.'),
(N'automotive', 5, N'Spotless detailing', N'Interior detailing was outstanding. Worth every rupee.'),
(N'automotive', 4, N'Good job', N'Work was done well. Took a few hours longer than estimated.'),
(N'automotive', 4, N'Fair pricing', N'Honest estimate and genuine parts.'),
(N'automotive', 3, N'Average', N'Service was okay but the car was returned without a wash.'),
(N'automotive', 2, N'Recurring issue', N'The noise came back within a week.'),
(N'automotive', 1, N'Overcharged', N'Final bill was much higher than the estimate.'),
(N'pet-care', 5, N'Our dog loves them', N'Bruno is usually anxious at clinics, but the staff were so gentle with him. Clear advice and no unnecessary tests.'),
(N'pet-care', 5, N'Caring and patient', N'They took time to explain the vaccination schedule and sent reminders on WhatsApp. Highly recommend for first-time pet parents.'),
(N'pet-care', 4, N'Good grooming, slight wait', N'Grooming was neat and our Shih Tzu came back happy. Had to wait 20 minutes beyond our slot.'),
(N'pet-care', 4, N'Trustworthy', N'Got daily photo updates while our cat stayed with them. Would use again.'),
(N'pet-care', 3, N'Okay experience', N'Treatment was fine but the clinic was crowded on a Sunday.'),
(N'pet-care', 2, N'Pricing not clear', N'Final bill had charges that were not mentioned at the counter.'),
(N'pet-care', 1, N'Missed appointment', N'Groomer did not turn up and nobody called to inform us.'),
(N'finance-tax', 5, N'Hassle-free ITR filing', N'Filed my return with capital gains in two days and explained every deduction. Refund came in three weeks.'),
(N'finance-tax', 5, N'Reliable for our startup', N'They handled incorporation, GST and ROC filings for us. Always on time and very responsive on email.'),
(N'finance-tax', 4, N'Knowledgeable team', N'Helped us reply to a GST notice properly. Fees were reasonable.'),
(N'finance-tax', 4, N'Good advice', N'Compared multiple health plans and recommended one that fit our budget.'),
(N'finance-tax', 3, N'Average follow-up', N'Work was done, but I had to keep following up for documents.'),
(N'finance-tax', 2, N'Delayed filing', N'GST return was filed late and we had to pay a small late fee.'),
(N'finance-tax', 1, N'Not responsive', N'Paid the advance but they stopped replying for two weeks.'),
(N'appliance-repair', 5, N'Fixed in one visit', N'Technician diagnosed the washing machine problem quickly, carried the spare part and fixed it in one visit.'),
(N'appliance-repair', 5, N'Genuine parts', N'Replaced my phone screen in under an hour with a proper warranty card. Display looks original.'),
(N'appliance-repair', 4, N'Good service', N'Fridge is cooling well again. Arrived a little late but called ahead.'),
(N'appliance-repair', 4, N'Fair pricing', N'Inspection charge was adjusted in the final bill as promised.'),
(N'appliance-repair', 3, N'Two visits needed', N'Problem was fixed only on the second visit as the part was not available.'),
(N'appliance-repair', 2, N'Issue came back', N'The same error code appeared again after ten days.'),
(N'appliance-repair', 1, N'Overcharged for spares', N'Charged far more than the market price for a simple part.'),
(N'it-digital', 5, N'Great website, delivered on time', N'They built our clinic website in three weeks. Fast, mobile-friendly and we started getting enquiries from Google.'),
(N'it-digital', 5, N'Visible results', N'Our Google Business Profile calls doubled in two months. Monthly reports are clear and honest.'),
(N'it-digital', 4, N'Neat CCTV installation', N'Clean wiring and the app setup works well. Took a day longer than planned.'),
(N'it-digital', 4, N'Creative designs', N'Liked the logo concepts. A couple of revision rounds were needed.'),
(N'it-digital', 3, N'Okay work', N'Website is fine but small changes take too long.'),
(N'it-digital', 2, N'Missed deadlines', N'Launch was delayed by a month without clear communication.'),
(N'it-digital', 1, N'Poor support', N'After payment, support requests went unanswered.'),
(N'construction-renovation', 5, N'No more leaks', N'Terrace waterproofing held up perfectly through the monsoon. Team was clean and professional.'),
(N'construction-renovation', 5, N'Transparent contractor', N'Weekly photo updates and the BOQ matched the final bill. Our house was handed over on schedule.'),
(N'construction-renovation', 4, N'Good solar installation', N'Panels installed neatly and net metering was approved in a month.'),
(N'construction-renovation', 4, N'Quality work', N'Railing finish is excellent. Installation took an extra day.'),
(N'construction-renovation', 3, N'Average finishing', N'Tile work is okay but a few joints needed redoing.'),
(N'construction-renovation', 2, N'Delays', N'Work stretched well beyond the promised timeline.'),
(N'construction-renovation', 1, N'Left the site messy', N'Debris was left behind and we had to arrange the cleanup.'),
(N'fashion-tailoring', 5, N'Perfect fitting', N'Blouse fitting was perfect in the first trial itself. Delivered two days before the function.'),
(N'fashion-tailoring', 5, N'Beautiful work', N'The chikankari on my lehenga is exquisite. Everyone at the wedding asked where it was from.'),
(N'fashion-tailoring', 4, N'Clean laundry, on time', N'Clothes came back fresh and well ironed. Pickup was punctual.'),
(N'fashion-tailoring', 4, N'Good stitching', N'Good finish, needed a small alteration after delivery.'),
(N'fashion-tailoring', 3, N'Okay', N'Work is decent but delivery was a day late.'),
(N'fashion-tailoring', 2, N'Fitting issues', N'Needed three trials to get the fit right.'),
(N'fashion-tailoring', 1, N'Damaged garment', N'A silk saree came back with a stain after dry cleaning.'),
(N'astrology-pooja', 5, N'Pooja done beautifully', N'Pandit ji explained every ritual during our griha pravesh and brought all the samagri. Very peaceful ceremony.'),
(N'astrology-pooja', 5, N'Insightful consultation', N'Detailed and practical guidance without pushing expensive remedies.'),
(N'astrology-pooja', 4, N'Helpful vastu tips', N'Simple remedies without any demolition. Report was easy to follow.'),
(N'astrology-pooja', 4, N'Punctual', N'Arrived on time for the muhurtham and completed the pooja well.'),
(N'astrology-pooja', 3, N'Average', N'Consultation felt a bit rushed.'),
(N'astrology-pooja', 2, N'Started late', N'Pooja started 45 minutes late, which affected our muhurtham.'),
(N'astrology-pooja', 1, N'Not as promised', N'Promised samagri was incomplete and we had to arrange items at the last minute.');

IF OBJECT_ID('tempdb..#Reply') IS NOT NULL DROP TABLE #Reply;
CREATE TABLE #Reply (Positive bit, Idx int, ReplyText nvarchar(500));
INSERT INTO #Reply VALUES
(1, 0, N'Thank you for your kind words! We look forward to serving you again.'),
(1, 1, N'Thanks for taking the time to share your feedback. It means a lot to our team.'),
(1, 2, N'We are glad we could help. Do reach out anytime you need us.'),
(0, 0, N'Sorry about the experience. We have shared your feedback with the team and are improving our scheduling.'),
(0, 1, N'We apologise for the inconvenience. Our manager will call you to make this right.'),
(0, 2, N'Thank you for the feedback. We have addressed the issue and would love another chance to serve you.');

-- Number the review texts per (category, rating), including the generic pool
IF OBJECT_ID('tempdb..#Pool') IS NOT NULL DROP TABLE #Pool;
SELECT c.Slug AS CatSlug, r.Rating, r.Title, r.Comment,
       ROW_NUMBER() OVER (PARTITION BY c.Slug, r.Rating ORDER BY r.CatSlug DESC, r.Title) - 1 AS Idx,
       COUNT(*) OVER (PARTITION BY c.Slug, r.Rating) AS Cnt
INTO #Pool
FROM dbo.Categories c
JOIN #R r ON r.CatSlug IN (c.Slug, N'*');

IF OBJECT_ID('tempdb..#Cust') IS NOT NULL DROP TABLE #Cust;
SELECT Id, DisplayName, ROW_NUMBER() OVER (ORDER BY NormalizedEmail) - 1 AS Idx
INTO #Cust
FROM dbo.AspNetUsers WHERE UserType = N'Customer' AND IsActive = 1;
DECLARE @CustCount int = (SELECT COUNT(*) FROM #Cust);

IF OBJECT_ID('tempdb..#N') IS NOT NULL DROP TABLE #N;
SELECT TOP (60) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k INTO #N FROM sys.all_objects;

;WITH biz AS (
    SELECT b.Id, b.Slug, b.CreatedOn, c.Slug AS CatSlug, sc.Slug AS SubSlug,
           DATEDIFF(DAY, b.CreatedOn, @Now) AS AgeDays,
           ABS(CHECKSUM(b.Slug)) % 25 AS Quality,
           CASE WHEN b.Status = N'Active' THEN 6 + ABS(CHECKSUM(b.Slug + N'#n')) % 18 ELSE 1 END
             * CASE WHEN sc.Slug IN (N'hospitals', N'restaurants', N'taxi-services') THEN 2 ELSE 1 END AS ReviewTarget
    FROM dbo.Businesses b
    JOIN dbo.Categories c ON c.Id = b.CategoryId
    JOIN dbo.SubCategories sc ON sc.Id = b.SubCategoryId
    WHERE DATEDIFF(DAY, b.CreatedOn, @Now) > 14
),
draw AS (
    SELECT biz.*, n.k,
           ABS(CHECKSUM(biz.Slug, n.k, N'rating')) % 100 AS RatingRoll,
           ABS(CHECKSUM(biz.Slug, n.k, N'text')) AS TextRoll,
           ABS(CHECKSUM(biz.Slug, n.k, N'state')) % 100 AS StateRoll,
           ABS(CHECKSUM(biz.Slug, n.k, N'age')) % (biz.AgeDays - 10) AS DaysAgo,
           (ABS(CHECKSUM(biz.Slug)) + n.k * 37) % @CustCount AS CustIdx
    FROM biz JOIN #N n ON n.k < biz.ReviewTarget
),
rated AS (
    SELECT d.*,
           CAST(CASE WHEN d.RatingRoll < 45 + d.Quality THEN 5
                     WHEN d.RatingRoll < 72 + d.Quality / 2 THEN 4
                     WHEN d.RatingRoll < 90 THEN 3
                     WHEN d.RatingRoll < 96 THEN 2
                     ELSE 1 END AS tinyint) AS Rating
    FROM draw d
)
INSERT INTO dbo.Reviews (BusinessId, CustomerUserId, Rating, Title, Comment, Status, OwnerReply, RepliedOn, HelpfulCount, IsVerifiedVisit, ReportReason, CreatedBy, CreatedOn)
SELECT r.Id, cu.Id, r.Rating, p.Title, p.Comment,
       CASE WHEN r.StateRoll < 2 AND r.DaysAgo < 20 THEN N'Pending'
            WHEN r.StateRoll IN (2, 3) THEN N'Flagged'
            WHEN r.StateRoll = 4 THEN N'Rejected'
            ELSE N'Published' END,
       CASE WHEN r.StateRoll % 100 >= 45 AND r.DaysAgo > 1 THEN rp.ReplyText END,
       CASE WHEN r.StateRoll % 100 >= 45 AND r.DaysAgo > 1 THEN DATEADD(HOUR, 20, DATEADD(DAY, -r.DaysAgo, @Now)) END,
       r.TextRoll % 14,
       CASE WHEN r.TextRoll % 10 < 6 THEN 1 ELSE 0 END,
       CASE WHEN r.StateRoll = 2 THEN N'Reported by the business: reviewer was never a customer'
            WHEN r.StateRoll = 3 THEN N'Reported by a user: contains a personal phone number'
            WHEN r.StateRoll = 4 THEN N'Removed: abusive language' END,
       cu.Id,
       DATEADD(MINUTE, -(r.TextRoll % 600), DATEADD(DAY, -r.DaysAgo, @Now))
FROM rated r
JOIN #Cust cu ON cu.Idx = r.CustIdx
JOIN #Pool p ON p.CatSlug = r.CatSlug AND p.Rating = r.Rating AND p.Idx = r.TextRoll % p.Cnt
JOIN #Reply rp ON rp.Positive = CASE WHEN r.Rating >= 4 THEN 1 ELSE 0 END AND rp.Idx = r.TextRoll % 3
WHERE NOT EXISTS (SELECT 1 FROM dbo.Reviews x WHERE x.BusinessId = r.Id AND x.CustomerUserId = cu.Id);

/* ---------- Recalculate denormalised rating aggregates ---------- */
UPDATE b SET
    AverageRating = ISNULL(agg.AvgRating, 0),
    ReviewCount   = ISNULL(agg.Cnt, 0)
FROM dbo.Businesses b
OUTER APPLY (
    SELECT CAST(AVG(CAST(r.Rating AS decimal(5,2))) AS decimal(3,2)) AS AvgRating, COUNT(*) AS Cnt
    FROM dbo.Reviews r WHERE r.BusinessId = b.Id AND r.Status = N'Published' AND r.IsDeleted = 0
) agg;

COMMIT TRANSACTION;
PRINT '07_Reviews.sql completed';
-- Temp tables live for the whole sqlcmd session; drop them so later scripts in RunAll.sql compile cleanly.
DROP TABLE IF EXISTS #R, #Reply, #Pool, #Cust, #N;
GO
