"""
Generates database/scripts/15_MarketingMedia.sql - the photos and video posters used by the
"List your business" page, stored in dbo.Media and linked to dbo.MarketingContent rows by Code.

Sources (all free for commercial use, no attribution required - credits are still shown in the UI):
  * Photos: Unsplash (Unsplash License, https://unsplash.com/license)
  * Video posters: Mixkit (Mixkit Free License, https://mixkit.co/license/)

Usage (from the repo root):   pip install pillow
                               python database/tools/generate_marketing_media.py
"""
from __future__ import annotations

import io
import pathlib
import urllib.request

from PIL import Image

OUT = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "15_MarketingMedia.sql"
UNSPLASH = "https://images.unsplash.com/{id}?w=1440&q=80&fm=jpg&fit=max"
MIXKIT_POSTER = "https://assets.mixkit.co/videos/{id}/{id}-thumb-720-3.jpg"

# MarketingContent.Code -> source image
SOURCES: list[tuple[str, str]] = [
    ("LYB-HERO", UNSPLASH.format(id="photo-1695398170358-99749f64c887")),        # Samyuktha Nair
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


if __name__ == "__main__":
    main()
