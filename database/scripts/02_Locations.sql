/* =====================================================================================
   Calling Bell - 02_Locations.sql
   States, cities (with database-stored SVG artwork) and localities with real pincodes.
   Idempotent: MERGE on Slug / (CityId, Slug).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

MERGE dbo.States AS t
USING (VALUES
    (N'Telangana',      N'TG', N'telangana',      1),
    (N'Karnataka',      N'KA', N'karnataka',      2),
    (N'Maharashtra',    N'MH', N'maharashtra',    3),
    (N'Tamil Nadu',     N'TN', N'tamil-nadu',     4),
    (N'Delhi',          N'DL', N'delhi',          5),
    (N'West Bengal',    N'WB', N'west-bengal',    6),
    (N'Gujarat',        N'GJ', N'gujarat',        7),
    (N'Rajasthan',      N'RJ', N'rajasthan',      8),
    (N'Kerala',         N'KL', N'kerala',         9),
    (N'Uttar Pradesh',  N'UP', N'uttar-pradesh', 10)
) AS s (Name, Code, Slug, SortOrder)
ON t.Slug = s.Slug
WHEN MATCHED AND (t.Name <> s.Name OR t.Code <> s.Code OR t.SortOrder <> s.SortOrder)
    THEN UPDATE SET Name = s.Name, Code = s.Code, SortOrder = s.SortOrder, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (Name, Code, Slug, SortOrder, IsActive, CreatedBy, CreatedOn)
         VALUES (s.Name, s.Code, s.Slug, s.SortOrder, 1, N'seed', DATEADD(DAY, -540, SYSDATETIMEOFFSET()));

MERGE dbo.Cities AS t
USING (
    SELECT st.Id AS StateId, v.Name, v.Slug, v.Lat, v.Lng, v.IsPopular, v.SortOrder, v.IsActive
    FROM (VALUES
        (N'telangana',     N'Hyderabad', N'hyderabad', 17.385044, 78.486671, 1,  1, 1),
        (N'karnataka',     N'Bengaluru', N'bengaluru', 12.971599, 77.594566, 1,  2, 1),
        (N'maharashtra',   N'Mumbai',    N'mumbai',    19.075984, 72.877656, 1,  3, 1),
        (N'maharashtra',   N'Pune',      N'pune',      18.520430, 73.856743, 1,  4, 1),
        (N'tamil-nadu',    N'Chennai',   N'chennai',   13.082680, 80.270718, 1,  5, 1),
        (N'delhi',         N'New Delhi', N'delhi',     28.613939, 77.209021, 1,  6, 1),
        (N'west-bengal',   N'Kolkata',   N'kolkata',   22.572646, 88.363895, 0,  7, 1),
        (N'gujarat',       N'Ahmedabad', N'ahmedabad', 23.022505, 72.571362, 0,  8, 1),
        (N'rajasthan',     N'Jaipur',    N'jaipur',    26.912434, 75.787270, 0,  9, 1),
        (N'kerala',        N'Kochi',     N'kochi',      9.931233, 76.267304, 0, 10, 1),
        -- Launching soon: configured but not yet live for customers
        (N'uttar-pradesh', N'Lucknow',   N'lucknow',   26.846694, 80.946166, 0, 11, 0)
    ) v (StateSlug, Name, Slug, Lat, Lng, IsPopular, SortOrder, IsActive)
    JOIN dbo.States st ON st.Slug = v.StateSlug
) AS s
ON t.Slug = s.Slug
WHEN MATCHED AND (t.Name <> s.Name OR t.IsPopular <> s.IsPopular OR t.SortOrder <> s.SortOrder OR t.IsActive <> s.IsActive OR t.StateId <> s.StateId)
    THEN UPDATE SET StateId = s.StateId, Name = s.Name, Latitude = s.Lat, Longitude = s.Lng, IsPopular = s.IsPopular,
                    SortOrder = s.SortOrder, IsActive = s.IsActive, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (StateId, Name, Slug, Latitude, Longitude, IsPopular, SortOrder, IsActive, CreatedBy, CreatedOn)
         VALUES (s.StateId, s.Name, s.Slug, s.Lat, s.Lng, s.IsPopular, s.SortOrder, s.IsActive, N'seed', DATEADD(DAY, -540, SYSDATETIMEOFFSET()));

MERGE dbo.Areas AS t
USING (
    SELECT c.Id AS CityId, v.Name, v.Slug, v.Pincode, v.Lat, v.Lng
    FROM (VALUES
        (N'hyderabad', N'Banjara Hills',   N'banjara-hills',    N'500034', 17.412600, 78.448200),
        (N'hyderabad', N'Jubilee Hills',   N'jubilee-hills',    N'500033', 17.432500, 78.407300),
        (N'hyderabad', N'Madhapur',        N'madhapur',         N'500081', 17.448300, 78.391500),
        (N'hyderabad', N'Gachibowli',      N'gachibowli',       N'500032', 17.440100, 78.348900),
        (N'hyderabad', N'Kukatpally',      N'kukatpally',       N'500072', 17.484900, 78.413800),
        (N'hyderabad', N'Kondapur',        N'kondapur',         N'500084', 17.460000, 78.357000),
        (N'hyderabad', N'Ameerpet',        N'ameerpet',         N'500016', 17.437500, 78.448300),
        (N'hyderabad', N'Secunderabad',    N'secunderabad',     N'500003', 17.439900, 78.498300),
        (N'bengaluru', N'Koramangala',     N'koramangala',      N'560034', 12.935200, 77.624500),
        (N'bengaluru', N'Indiranagar',     N'indiranagar',      N'560038', 12.978400, 77.640800),
        (N'bengaluru', N'HSR Layout',      N'hsr-layout',       N'560102', 12.911600, 77.647400),
        (N'bengaluru', N'Whitefield',      N'whitefield',       N'560066', 12.969800, 77.750000),
        (N'bengaluru', N'Jayanagar',       N'jayanagar',        N'560041', 12.925000, 77.593800),
        (N'bengaluru', N'Malleshwaram',    N'malleshwaram',     N'560003', 13.003500, 77.570900),
        (N'bengaluru', N'Electronic City', N'electronic-city',  N'560100', 12.845200, 77.660200),
        (N'mumbai',    N'Andheri West',    N'andheri-west',     N'400058', 19.136400, 72.829600),
        (N'mumbai',    N'Bandra West',     N'bandra-west',      N'400050', 19.059600, 72.829500),
        (N'mumbai',    N'Powai',           N'powai',            N'400076', 19.117600, 72.906000),
        (N'mumbai',    N'Malad West',      N'malad-west',       N'400064', 19.187400, 72.848400),
        (N'mumbai',    N'Dadar West',      N'dadar-west',       N'400028', 19.017800, 72.847800),
        (N'mumbai',    N'Chembur',         N'chembur',          N'400071', 19.052200, 72.900500),
        (N'pune',      N'Kothrud',         N'kothrud',          N'411038', 18.507400, 73.807700),
        (N'pune',      N'Baner',           N'baner',            N'411045', 18.559000, 73.786800),
        (N'pune',      N'Viman Nagar',     N'viman-nagar',      N'411014', 18.567900, 73.914300),
        (N'pune',      N'Hinjewadi',       N'hinjewadi',        N'411057', 18.591300, 73.738900),
        (N'pune',      N'Aundh',           N'aundh',            N'411007', 18.558000, 73.807500),
        (N'chennai',   N'T. Nagar',        N't-nagar',          N'600017', 13.041800, 80.234100),
        (N'chennai',   N'Adyar',           N'adyar',            N'600020', 13.001200, 80.256500),
        (N'chennai',   N'Velachery',       N'velachery',        N'600042', 12.981500, 80.218000),
        (N'chennai',   N'Anna Nagar',      N'anna-nagar',       N'600040', 13.085000, 80.210100),
        (N'chennai',   N'Sholinganallur',  N'omr-sholinganallur', N'600119', 12.901000, 80.227900),
        (N'delhi',     N'Connaught Place', N'connaught-place',  N'110001', 28.631500, 77.216700),
        (N'delhi',     N'Saket',           N'saket',            N'110017', 28.524500, 77.206600),
        (N'delhi',     N'Dwarka',          N'dwarka',           N'110075', 28.592100, 77.046000),
        (N'delhi',     N'Lajpat Nagar',    N'lajpat-nagar',     N'110024', 28.567700, 77.243300),
        (N'delhi',     N'Rohini',          N'rohini',           N'110085', 28.749500, 77.056500),
        (N'kolkata',   N'Salt Lake',       N'salt-lake',        N'700091', 22.586700, 88.417100),
        (N'kolkata',   N'Park Street',     N'park-street',      N'700016', 22.553000, 88.352000),
        (N'kolkata',   N'Ballygunge',      N'ballygunge',       N'700019', 22.528000, 88.365000),
        (N'kolkata',   N'New Town',        N'new-town',         N'700156', 22.579700, 88.475700),
        (N'ahmedabad', N'Navrangpura',     N'navrangpura',      N'380009', 23.036500, 72.561100),
        (N'ahmedabad', N'Satellite',       N'satellite',        N'380015', 23.030000, 72.517000),
        (N'ahmedabad', N'Bodakdev',        N'bodakdev',         N'380054', 23.039500, 72.506600),
        (N'ahmedabad', N'Maninagar',       N'maninagar',        N'380008', 22.996200, 72.603100),
        (N'jaipur',    N'Malviya Nagar',   N'malviya-nagar',    N'302017', 26.854900, 75.824300),
        (N'jaipur',    N'Vaishali Nagar',  N'vaishali-nagar',   N'302021', 26.911500, 75.727000),
        (N'jaipur',    N'C-Scheme',        N'c-scheme',         N'302001', 26.906500, 75.801000),
        (N'kochi',     N'Kakkanad',        N'kakkanad',         N'682030', 10.015900, 76.341900),
        (N'kochi',     N'Edappally',       N'edappally',        N'682024', 10.026100, 76.308300),
        (N'kochi',     N'Panampilly Nagar',N'panampilly-nagar', N'682036',  9.966700, 76.292500),
        (N'lucknow',   N'Gomti Nagar',     N'gomti-nagar',      N'226010', 26.850000, 81.000000),
        (N'lucknow',   N'Hazratganj',      N'hazratganj',       N'226001', 26.850900, 80.946200)
    ) v (CitySlug, Name, Slug, Pincode, Lat, Lng)
    JOIN dbo.Cities c ON c.Slug = v.CitySlug
) AS s
ON t.CityId = s.CityId AND t.Slug = s.Slug
WHEN MATCHED AND (t.Name <> s.Name OR t.Pincode <> s.Pincode)
    THEN UPDATE SET Name = s.Name, Pincode = s.Pincode, Latitude = s.Lat, Longitude = s.Lng, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (CityId, Name, Slug, Pincode, Latitude, Longitude, IsActive, CreatedBy, CreatedOn)
         VALUES (s.CityId, s.Name, s.Slug, s.Pincode, s.Lat, s.Lng, 1, N'seed', DATEADD(DAY, -540, SYSDATETIMEOFFSET()));

/* ---------- City artwork (SVG stored in dbo.Media, referenced by Cities.ImageUrl) ---------- */
;WITH art AS (
    SELECT c.Id, c.Slug, c.Name,
           CONCAT(
             N'<svg xmlns="http://www.w3.org/2000/svg" width="800" height="500" viewBox="0 0 800 500">',
             N'<rect width="800" height="500" fill="#0B1220"/>',
             N'<circle cx="', 160 + (c.SortOrder * 97) % 480, N'" cy="', 120 + (c.SortOrder * 31) % 60, N'" r="64" fill="#F4A62C" fill-opacity="0.85"/>',
             N'<path d="M0 500V340h50v-40h60v60h40v-90h70v60h30v-40h60v60h50V230h20v-30h20v30h20v120h60v-50h70v20h40v-70h80v90h50v-50h80v210z" fill="#15233B"/>',
             N'<path d="M0 500v-100h70v-30h60v40h70v-60h60v40h70v-10h70v40h70v-60h70v40h70v-30h70v40h70v-30h50v120z" fill="#1E2F4D"/>',
             N'<g fill="#F4A62C" fill-opacity="0.35">',
             N'<rect x="372" y="250" width="6" height="8"/><rect x="384" y="250" width="6" height="8"/><rect x="372" y="270" width="6" height="8"/>',
             N'<rect x="604" y="280" width="6" height="8"/><rect x="616" y="296" width="6" height="8"/><rect x="164" y="300" width="6" height="8"/></g>',
             N'</svg>') AS Svg
    FROM dbo.Cities c
)
MERGE dbo.Media AS t
USING (SELECT N'City' AS EntityType, Id AS EntityId, Slug + N'.svg' AS FileName, Name + N' skyline illustration' AS AltText,
              CAST(CAST(Svg COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max)) AS varbinary(max)) AS Bytes
       FROM art) AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = s.FileName
WHEN MATCHED AND t.FileData <> s.Bytes
    THEN UPDATE SET FileData = s.Bytes, ThumbnailData = s.Bytes, FileSize = DATALENGTH(s.Bytes), AltText = s.AltText, ModifiedOn = SYSDATETIMEOFFSET(), ModifiedBy = N'seed'
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, s.FileName, N'image/svg+xml', N'.svg', DATALENGTH(s.Bytes), s.Bytes, s.Bytes, s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

UPDATE c SET ImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), m.MediaId))
FROM dbo.Cities c
JOIN dbo.Media m ON m.EntityType = N'City' AND m.EntityId = c.Id AND m.FileName = c.Slug + N'.svg'
WHERE ISNULL(c.ImageUrl, N'') <> N'/api/media/' + LOWER(CONVERT(nvarchar(36), m.MediaId));

COMMIT TRANSACTION;
PRINT '02_Locations.sql completed';
GO
