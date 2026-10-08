"""
Generates database/scripts/15_MarketingMedia.sql - the photos and video posters used by the
"List your business" page, stored in dbo.Media and linked to dbo.MarketingContent rows by Code.

Sources (all free for commercial use, no attribution required - credits are still shown in the UI):
  * Photos: Unsplash (Unsplash License, https://unsplash.com/license)
  * Video posters: Mixkit (Mixkit Free License, https://mixkit.co/license/)

Also generates database/scripts/26_CountryImages.sql - the hero photo shown to visitors browsing each country, and the neutral
default for every other country. Sources: Wikimedia Commons (CC0 / CC BY / CC BY-SA; author and licence are credited in the UI)
and Unsplash.

Usage (from the repo root):   pip install pillow
                               python database/tools/generate_marketing_media.py            # both scripts
                               python database/tools/generate_marketing_media.py countries  # 26_CountryImages.sql only
"""
from __future__ import annotations

import io
import json
import pathlib
import sys
import urllib.parse
import urllib.request

from PIL import Image

OUT = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "15_MarketingMedia.sql"
UNSPLASH = "https://images.unsplash.com/{id}?w=1440&q=80&fm=jpg&fit=max"
MIXKIT_POSTER = "https://assets.mixkit.co/videos/{id}/{id}-thumb-720-3.jpg"

# MarketingContent.Code -> source image (LYB-HERO's photos depend on the country: see COUNTRY_SOURCES)
SOURCES: list[tuple[str, str]] = [
    ("LYB-OVERVIEW", UNSPLASH.format(id="photo-1577962917302-cd874c4e31d2")),    # Smartworks Coworking
    ("LYB-GAL-RETAIL", UNSPLASH.format(id="photo-1751901173169-1ca6df2a5f11")),  # Anil Reddy
    ("LYB-GAL-FOOD", UNSPLASH.format(id="photo-1765644818677-11f5295955ec")),    # Akshay Mehta
    ("LYB-GAL-HEALTH", UNSPLASH.format(id="photo-1637059824899-a441006a6875")),  # vaibhav vivian
    ("LYB-GAL-HOME", UNSPLASH.format(id="photo-1660330589693-99889d60181e")),    # Raze Solar
    ("LYB-GAL-SHOP", UNSPLASH.format(id="photo-1753184863498-72e77c60888b")),    # Saad Ahmad
    ("LYB-GAL-CONSULT", UNSPLASH.format(id="photo-1659353888906-adb3e0041693")), # Fotos
    ("LYB-VID-MOBILE", MIXKIT_POSTER.format(id=49287)),
    ("LYB-VID-ONLINE", MIXKIT_POSTER.format(id=42652)),
    ("LYB-VID-NUMBERS", MIXKIT_POSTER.format(id=4533)),
]

FULL_WIDTH, FULL_QUALITY = 1280, 68
THUMB_WIDTH, THUMB_QUALITY = 640, 66
MAX_PORTRAIT_RATIO = 1.25  # crop tall photos to 4:5 so cards stay compact


def fetch(url: str) -> Image.Image:
    req = urllib.request.Request(url, headers={"User-Agent": "CallingBell-seed/1.0"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return Image.open(io.BytesIO(resp.read())).convert("RGB")


def encode(img: Image.Image, width: int, quality: int) -> bytes:
    copy = img.copy()
    copy.thumbnail((width, width * 2), Image.LANCZOS)
    buf = io.BytesIO()
    copy.save(buf, "JPEG", quality=quality, optimize=True, progressive=True)
    return buf.getvalue()


def prepare(img: Image.Image) -> Image.Image:
    w, h = img.size
    if h / w > MAX_PORTRAIT_RATIO:
        new_h = int(w * MAX_PORTRAIT_RATIO)
        top = int((h - new_h) * 0.3)  # keep faces, which sit in the upper third
        img = img.crop((0, top, w, top + new_h))
    return img


def main() -> None:
    rows = []
    for code, url in SOURCES:
        img = prepare(fetch(url))
        full, thumb = encode(img, FULL_WIDTH, FULL_QUALITY), encode(img, THUMB_WIDTH, THUMB_QUALITY)
        rows.append((code, full, thumb))
        print(f"{code:18} {len(full) // 1024:>4} KB / {len(thumb) // 1024:>3} KB")

    values = ",\n".join(f"(N'{code}',\n 0x{full.hex().upper()},\n 0x{thumb.hex().upper()})" for code, full, thumb in rows)
    OUT.write_text(f"""/* =====================================================================================
   Calling Bell - 15_MarketingMedia.sql            *** GENERATED - do not edit by hand ***
   Regenerate with: python database/tools/generate_marketing_media.py
   Photos (Unsplash License) and video posters (Mixkit Free License) for the "List your business"
   page, stored in dbo.Media (EntityType = 'MarketingContent') and served by /api/media/{{id}}.
   FileData = 1280px desktop image, ThumbnailData = 640px mobile/thumbnail image.
   Requires 14_MarketingContent.sql. Idempotent: MERGE on (EntityType, EntityId, FileName).
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#Mm') IS NOT NULL DROP TABLE #Mm;
CREATE TABLE #Mm (Code nvarchar(60) PRIMARY KEY, FileData varbinary(max) NOT NULL, ThumbnailData varbinary(max) NOT NULL);
INSERT INTO #Mm (Code, FileData, ThumbnailData) VALUES
{values};

IF EXISTS (SELECT 1 FROM #Mm m WHERE NOT EXISTS (SELECT 1 FROM dbo.MarketingContent c WHERE c.Code = m.Code))
    THROW 50015, 'Marketing media references content that does not exist. Run 14_MarketingContent.sql first.', 1;

MERGE dbo.Media AS t
USING (SELECT c.Id AS EntityId, c.AltText, m.FileData, m.ThumbnailData
       FROM #Mm m JOIN dbo.MarketingContent c ON c.Code = m.Code) AS s
ON t.EntityType = N'MarketingContent' AND t.EntityId = s.EntityId AND t.FileName = N'photo.jpg'
WHEN MATCHED AND (t.FileData <> s.FileData OR t.ThumbnailData <> s.ThumbnailData OR ISNULL(t.AltText, N'') <> ISNULL(s.AltText, N''))
    THEN UPDATE SET FileData = s.FileData, ThumbnailData = s.ThumbnailData, FileSize = DATALENGTH(s.FileData), AltText = s.AltText,
                    IsActive = 1, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), N'MarketingContent', s.EntityId, N'photo.jpg', N'image/jpeg', N'.jpg', DATALENGTH(s.FileData), s.FileData, s.ThumbnailData,
                 s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

UPDATE c SET
    ImageUrl        = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    DesktopImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    ThumbnailUrl    = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    MobileImageUrl  = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
FROM dbo.MarketingContent c
JOIN #Mm m ON m.Code = c.Code
JOIN dbo.Media md ON md.EntityType = N'MarketingContent' AND md.EntityId = c.Id AND md.FileName = N'photo.jpg'
WHERE ISNULL(c.ImageUrl, N'') <> N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId));

COMMIT TRANSACTION;
PRINT '15_MarketingMedia.sql completed';
DROP TABLE IF EXISTS #Mm;
GO
""", encoding="utf-8")
    print(f"Wrote {OUT} ({OUT.stat().st_size // 1024} KB)")


# ---------------------------------------------------------------------------------------------------------------------------------
# Country photos (26_CountryImages.sql)
# ---------------------------------------------------------------------------------------------------------------------------------

COUNTRY_OUT = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "26_CountryImages.sql"
COMMONS = "commons:"


def commons_url(title: str, width: int = 1440) -> str:
    """A resized copy of a Wikimedia Commons file, from the Commons API (no key needed)."""
    query = urllib.parse.urlencode({"action": "query", "format": "json", "titles": title, "prop": "imageinfo",
                                    "iiprop": "url", "iiurlwidth": str(width)})
    req = urllib.request.Request("https://commons.wikimedia.org/w/api.php?" + query, headers={"User-Agent": "CallingBell-seed/1.0"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        page = next(iter(json.load(resp)["query"]["pages"].values()))
    return page["imageinfo"][0]["thumburl"]


def commons_page(title: str) -> str:
    return "https://commons.wikimedia.org/wiki/" + urllib.parse.quote(title.replace(" ", "_"), safe=":(),_-")


# (MarketingContent.Code, CountryCode - or None for the block's own photo, shown in every other country -, source, alt text, credit,
# credit URL). Licences checked on each Commons file page: CC0 needs no credit; CC BY / CC BY-SA need author and licence (MediaCredit).
COUNTRY_SOURCES: list[tuple[str, str | None, str, str, str, str]] = [
    ("LYB-HERO", None, COMMONS + "File:Neon Open Sign.jpg",
     "Lit OPEN sign in a shop window", "Photo: Aaron Pruzaniec, CC BY 2.0, via Wikimedia Commons", commons_page("File:Neon Open Sign.jpg")),
    ("LYB-HERO", "IN", UNSPLASH.format(id="photo-1695398170358-99749f64c887"),
     "Shopkeeper smiling outside his store on a busy Indian street", "Photo: Samyuktha Nair on Unsplash", "https://unsplash.com/photos/r4YUKKh96rM"),
    ("LYB-HERO", "CA", COMMONS + "File:Avenue Saint-Viateur - panoramio.jpg",
     "Corner shop on Avenue Saint-Viateur in Montreal in winter", "Photo: 4net, CC BY 3.0, via Wikimedia Commons",
     commons_page("File:Avenue Saint-Viateur - panoramio.jpg")),
    ("LYB-HERO", "US", COMMONS + "File:13th Avenue,Boro Park.jpg",
     "Row of neighbourhood shops and a bakery on 13th Avenue, Brooklyn", "Photo: Robert S, CC0, via Wikimedia Commons",
     commons_page("File:13th Avenue,Boro Park.jpg")),
    ("LYB-HERO", "GB", COMMONS + "File:Covid-19 pandemic open corner store, Philip Lane, Tottenham, London, England 1.jpg",
     "Corner grocer with fruit and vegetables outside on a London street", "Photo: Acabashi, CC BY-SA 4.0, via Wikimedia Commons",
     commons_page("File:Covid-19 pandemic open corner store, Philip Lane, Tottenham, London, England 1.jpg")),
    ("LYB-HERO", "AU", COMMONS + "File:AUS Melbourne, Boroondara, Burwood Road 021.jpg",
     "Shopfronts along Burwood Road in Melbourne at sunset", "Photo: -wuppertaler, CC BY 4.0, via Wikimedia Commons",
     commons_page("File:AUS Melbourne, Boroondara, Burwood Road 021.jpg")),
    ("LYB-HERO", "AE", COMMONS + "File:Dubai seasoning shop (48393056856).jpg",
     "Spices and dried flowers for sale at a shop in Dubai's spice souk", "Photo: Raita Futo, CC BY 2.0, via Wikimedia Commons",
     commons_page("File:Dubai seasoning shop (48393056856).jpg")),
]


def sql(text: str | None) -> str:
    return "NULL" if text is None else "N'" + text.replace("'", "''") + "'"


def countries() -> None:
    rows = []
    for code, country, source, alt, credit, credit_url in COUNTRY_SOURCES:
        url = commons_url(source[len(COMMONS):]) if source.startswith(COMMONS) else source
        img = prepare(fetch(url))
        full, thumb = encode(img, FULL_WIDTH, FULL_QUALITY), encode(img, THUMB_WIDTH, THUMB_QUALITY)
        rows.append((code, country, alt, credit, credit_url, full, thumb))
        print(f"{code:10} {country or '--':3} {len(full) // 1024:>4} KB / {len(thumb) // 1024:>3} KB")

    values = ",\n".join(
        f"(N'{code}', {sql(country)}, {sql(alt)}, {sql(credit)}, {sql(credit_url)},\n 0x{full.hex().upper()},\n 0x{thumb.hex().upper()})"
        for code, country, alt, credit, credit_url, full, thumb in rows)
    COUNTRY_OUT.write_text(COUNTRY_SQL.replace("{values}", values), encoding="utf-8")
    print(f"Wrote {COUNTRY_OUT} ({COUNTRY_OUT.stat().st_size // 1024} KB)")


COUNTRY_SQL = """/* =====================================================================================
   Calling Bell - 26_CountryImages.sql             *** GENERATED - do not edit by hand ***
   Regenerate with: python database/tools/generate_marketing_media.py countries
   Photos for the country a visitor is browsing (dbo.MarketingContentImages), e.g. a Montreal
   storefront on the "List your business" hero for Canada. The block's own photo (CountryCode
   NULL below) is the neutral default shown in every other country.
   Wikimedia Commons (CC0 / CC BY / CC BY-SA) and Unsplash photos; author and licence in MediaCredit.
   Stored in dbo.Media (FileData = 1280px desktop, ThumbnailData = 640px mobile), served by /api/media/{id}.
   * Creates dbo.MarketingContentImages when missing (no EF migration needed).
   * Requires 14_MarketingContent.sql. Idempotent: MERGE on (content, country) and on the Media row.
   ===================================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MarketingContentImages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MarketingContentImages
    (
        Id                 uniqueidentifier NOT NULL CONSTRAINT PK_MarketingContentImages PRIMARY KEY CONSTRAINT DF_MarketingContentImages_Id DEFAULT NEWSEQUENTIALID(),
        MarketingContentId uniqueidentifier NOT NULL CONSTRAINT FK_MarketingContentImages_MarketingContent REFERENCES dbo.MarketingContent (Id),
        CountryCode        nchar(2)         NOT NULL,
        ImageUrl           nvarchar(500)    NOT NULL,
        ThumbnailUrl       nvarchar(500)    NULL,
        MobileImageUrl     nvarchar(500)    NULL,
        DesktopImageUrl    nvarchar(500)    NULL,
        AltText            nvarchar(300)    NULL,
        MediaCredit        nvarchar(200)    NULL,
        MediaCreditUrl     nvarchar(500)    NULL,
        IsActive           bit              NOT NULL CONSTRAINT DF_MarketingContentImages_IsActive DEFAULT (1),
        IsDeleted          bit              NOT NULL CONSTRAINT DF_MarketingContentImages_IsDeleted DEFAULT (0),
        CreatedBy          nvarchar(450)    NULL,
        CreatedOn          datetimeoffset   NOT NULL CONSTRAINT DF_MarketingContentImages_CreatedOn DEFAULT SYSDATETIMEOFFSET(),
        ModifiedBy         nvarchar(450)    NULL,
        ModifiedOn         datetimeoffset   NULL
    );
    CREATE UNIQUE INDEX IX_MarketingContentImages_Content_Country ON dbo.MarketingContentImages (MarketingContentId, CountryCode) WHERE IsDeleted = 0;
END
GO

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#Ci') IS NOT NULL DROP TABLE #Ci;
CREATE TABLE #Ci (Code nvarchar(60) NOT NULL, CountryCode nchar(2) NULL, AltText nvarchar(300) NOT NULL, MediaCredit nvarchar(200) NOT NULL,
                  MediaCreditUrl nvarchar(500) NOT NULL, FileData varbinary(max) NOT NULL, ThumbnailData varbinary(max) NOT NULL);
INSERT INTO #Ci (Code, CountryCode, AltText, MediaCredit, MediaCreditUrl, FileData, ThumbnailData) VALUES
{values};

IF EXISTS (SELECT 1 FROM #Ci m WHERE NOT EXISTS (SELECT 1 FROM dbo.MarketingContent c WHERE c.Code = m.Code))
    THROW 50026, 'Country images reference content that does not exist. Run 14_MarketingContent.sql first.', 1;

-- 1. One row per (content, country).
MERGE dbo.MarketingContentImages AS t
USING (SELECT c.Id AS MarketingContentId, m.CountryCode, m.AltText, m.MediaCredit, m.MediaCreditUrl
       FROM #Ci m JOIN dbo.MarketingContent c ON c.Code = m.Code WHERE m.CountryCode IS NOT NULL) AS s
ON t.MarketingContentId = s.MarketingContentId AND t.CountryCode = s.CountryCode AND t.IsDeleted = 0
WHEN MATCHED AND (ISNULL(t.AltText, N'') <> s.AltText OR ISNULL(t.MediaCredit, N'') <> s.MediaCredit
                  OR ISNULL(t.MediaCreditUrl, N'') <> s.MediaCreditUrl OR t.IsActive = 0)
    THEN UPDATE SET AltText = s.AltText, MediaCredit = s.MediaCredit, MediaCreditUrl = s.MediaCreditUrl, IsActive = 1,
                    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MarketingContentId, CountryCode, ImageUrl, AltText, MediaCredit, MediaCreditUrl, CreatedBy)
         VALUES (s.MarketingContentId, s.CountryCode, N'', s.AltText, s.MediaCredit, s.MediaCreditUrl, N'seed');

-- 2. The photos: a country's belongs to its MarketingContentImages row; the default to the MarketingContent row itself.
IF OBJECT_ID('tempdb..#Target') IS NOT NULL DROP TABLE #Target;
SELECT CASE WHEN m.CountryCode IS NULL THEN N'MarketingContent' ELSE N'MarketingContentImage' END AS EntityType,
       CASE WHEN m.CountryCode IS NULL THEN c.Id ELSE i.Id END AS EntityId, m.AltText, m.FileData, m.ThumbnailData
INTO #Target
FROM #Ci m
JOIN dbo.MarketingContent c ON c.Code = m.Code
LEFT JOIN dbo.MarketingContentImages i ON i.MarketingContentId = c.Id AND i.CountryCode = m.CountryCode AND i.IsDeleted = 0;

MERGE dbo.Media AS t
USING #Target AS s
ON t.EntityType = s.EntityType AND t.EntityId = s.EntityId AND t.FileName = N'photo.jpg'
WHEN MATCHED AND (t.FileData <> s.FileData OR t.ThumbnailData <> s.ThumbnailData OR ISNULL(t.AltText, N'') <> s.AltText OR t.IsActive = 0)
    THEN UPDATE SET FileData = s.FileData, ThumbnailData = s.ThumbnailData, FileSize = DATALENGTH(s.FileData), AltText = s.AltText,
                    IsActive = 1, ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET
    THEN INSERT (MediaId, EntityType, EntityId, FileName, ContentType, FileExtension, FileSize, FileData, ThumbnailData, AltText, IsPrimary, IsActive, IsDeleted, CreatedBy, CreatedOn)
         VALUES (NEWID(), s.EntityType, s.EntityId, N'photo.jpg', N'image/jpeg', N'.jpg', DATALENGTH(s.FileData), s.FileData, s.ThumbnailData,
                 s.AltText, 1, 1, 0, N'seed', SYSDATETIMEOFFSET());

-- 3. Point the rows at their photos.
UPDATE i SET
    ImageUrl        = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    DesktopImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    ThumbnailUrl    = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    MobileImageUrl  = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
FROM dbo.MarketingContentImages i
JOIN dbo.Media md ON md.EntityType = N'MarketingContentImage' AND md.EntityId = i.Id AND md.FileName = N'photo.jpg'
WHERE i.ImageUrl <> N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId));

UPDATE c SET
    ImageUrl        = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    DesktopImageUrl = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)),
    ThumbnailUrl    = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    MobileImageUrl  = N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId)) + N'/thumbnail',
    AltText = m.AltText, MediaCredit = m.MediaCredit, MediaCreditUrl = m.MediaCreditUrl,
    ModifiedBy = N'seed', ModifiedOn = SYSDATETIMEOFFSET()
FROM dbo.MarketingContent c
JOIN #Ci m ON m.Code = c.Code AND m.CountryCode IS NULL
JOIN dbo.Media md ON md.EntityType = N'MarketingContent' AND md.EntityId = c.Id AND md.FileName = N'photo.jpg'
WHERE ISNULL(c.ImageUrl, N'') <> N'/api/media/' + LOWER(CONVERT(nvarchar(36), md.MediaId))
   OR ISNULL(c.AltText, N'') <> m.AltText OR ISNULL(c.MediaCredit, N'') <> m.MediaCredit OR ISNULL(c.MediaCreditUrl, N'') <> m.MediaCreditUrl;

COMMIT TRANSACTION;
PRINT '26_CountryImages.sql completed';
DROP TABLE IF EXISTS #Ci;
DROP TABLE IF EXISTS #Target;
GO
"""


if __name__ == "__main__":
    if sys.argv[1:] != ["countries"]:
        main()
    countries()
