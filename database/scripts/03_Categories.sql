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
(N'package',     N'<path d="M16.5 9.4 7.55 4.24M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><path d="M3.27 6.96 12 12.01l8.73-5.05M12 22.08V12"/>'),
(N'paw',         N'<circle cx="11" cy="4" r="2"/><circle cx="18" cy="8" r="2"/><circle cx="20" cy="16" r="2"/><path d="M9 10a5 5 0 0 1 5 5v3.5a3.5 3.5 0 0 1-6.84 1.05Q6.52 17.48 4.46 16.84A3.5 3.5 0 0 1 5.5 10Z"/>'),
(N'calculator',  N'<rect x="4" y="2" width="16" height="20" rx="2"/><path d="M8 6h8M16 14v4M16 10h.01M12 10h.01M8 10h.01M12 14h.01M8 14h.01M12 18h.01M8 18h.01"/>'),
(N'receipt',     N'<path d="M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1Z"/><path d="M16 8h-6a2 2 0 1 0 0 4h4a2 2 0 1 1 0 4H8M12 17.5v-11"/>'),
(N'shield',      N'<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/>'),
(N'landmark',    N'<path d="M3 22h18M6 18v-7M10 18v-7M14 18v-7M18 18v-7M12 2l8 5H4z"/>'),
(N'plug',        N'<path d="M12 22v-5M9 8V2M15 8V2M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z"/>'),
(N'washer',      N'<path d="M3 6h3M17 6h.01"/><rect x="3" y="2" width="18" height="20" rx="2"/><circle cx="12" cy="13" r="5"/><path d="M12 18a2.5 2.5 0 0 0 0-5 2.5 2.5 0 0 1 0-5"/>'),
(N'fridge',      N'<path d="M5 6a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6Z"/><path d="M5 10h14M15 7v6"/>'),
(N'smartphone',  N'<rect x="5" y="2" width="14" height="20" rx="2"/><path d="M12 18h.01"/>'),
(N'laptop',      N'<path d="M20 16V7a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v9m16 0H4m16 0 1.28 2.55a1 1 0 0 1-.9 1.45H3.62a1 1 0 0 1-.9-1.45L4 16"/>'),
(N'monitor',     N'<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>'),
(N'code',        N'<path d="m16 18 6-6-6-6M8 6l-6 6 6 6"/>'),
(N'megaphone',   N'<path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/>'),
(N'pen-tool',    N'<path d="M15.7 21.3a1 1 0 0 1-1.4 0l-1.6-1.6a1 1 0 0 1 0-1.4l5.6-5.6a1 1 0 0 1 1.4 0l1.6 1.6a1 1 0 0 1 0 1.4Z"/><path d="m18 13-1.4-6.9a1 1 0 0 0-.8-.8L2.3 2.1M2.3 2.3l7.3 7.3"/><circle cx="11" cy="11" r="2"/>'),
(N'scan-eye',    N'<path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2"/><circle cx="12" cy="12" r="1"/><path d="M18.94 12.34a1 1 0 0 0 0-.68 7.5 7.5 0 0 0-13.88 0 1 1 0 0 0 0 .68 7.5 7.5 0 0 0 13.88 0"/>'),
(N'hard-hat',    N'<path d="M10 10V5a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v5"/><path d="M14 6a6 6 0 0 1 6 6v3M4 15v-3a6 6 0 0 1 6-6"/><rect x="2" y="15" width="20" height="4" rx="1"/>'),
(N'umbrella',    N'<path d="M22 12a10 10 0 0 0-20 0Z"/><path d="M12 12v8a2 2 0 0 0 4 0M12 2v1"/>'),
(N'sun',         N'<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>'),
(N'flame',       N'<path d="M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.07-2.14-.22-4.05 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.15.43-2.29 1-3a2.5 2.5 0 0 0 2.5 2.5z"/>'),
(N'grid',        N'<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18"/>'),
(N'shirt',       N'<path d="M20.38 3.46 16 2a4 4 0 0 1-8 0L3.62 3.46a2 2 0 0 0-1.34 2.23l.58 3.47a1 1 0 0 0 .99.84H6v10c0 1.1.9 2 2 2h8a2 2 0 0 0 2-2V10h2.15a1 1 0 0 0 .99-.84l.58-3.47a2 2 0 0 0-1.34-2.23z"/>'),
(N'bag',         N'<path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z"/><path d="M3 6h18M16 10a4 4 0 0 1-8 0"/>'),
(N'star',        N'<path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/>'),
(N'moon-star',   N'<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/><path d="M20 3v4M22 5h-4"/>'),
(N'compass',     N'<circle cx="12" cy="12" r="10"/><path d="m16.24 7.76-1.8 5.4a2 2 0 0 1-1.27 1.27l-5.4 1.8 1.8-5.4a2 2 0 0 1 1.27-1.27z"/>'),
(N'music',       N'<path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>'),
(N'footprints',  N'<path d="M4 16v-2.38C4 11.5 2.97 10.5 3 8c.03-2.72 1.49-6 4.5-6C9.37 2 10 3.8 10 5.5c0 3.11-2 5.66-2 8.68V16a2 2 0 1 1-4 0ZM20 20v-2.38c0-2.12 1.03-3.12 1-5.62-.03-2.72-1.49-6-4.5-6C14.63 6 14 7.8 14 9.5c0 3.11 2 5.66 2 8.68V20a2 2 0 1 0 4 0ZM16 17h4M4 13h4"/>'),
(N'languages',   N'<path d="m5 8 6 6M4 14l6-6 2-3M2 5h12M7 2h1M22 22l-5-10-5 10M14 18h6"/>'),
(N'mic',         N'<path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2M12 19v3"/>'),
(N'eye',         N'<path d="M2.06 12.35a1 1 0 0 1 0-.7 10.75 10.75 0 0 1 19.88 0 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-19.88 0"/><circle cx="12" cy="12" r="3"/>'),
(N'sprout',      N'<path d="M7 20h10M10 20c5.5-2.5.8-6.4 3-10"/><path d="M9.5 9.4c1.1.8 1.8 2.2 2.3 3.7-2 .4-3.5.4-4.8-.3-1.2-.6-2.3-1.9-3-4.2 2.8-.5 4.4 0 5.5.8zM14.1 6a7 7 0 0 0-1.1 4c1.9-.1 3.3-.6 4.3-1.4 1-1 1.6-2.3 1.7-4.6-2.7.1-4 1-4.9 2z"/>'),
(N'award',       N'<circle cx="12" cy="8" r="6"/><path d="M15.48 12.89 17 22l-5-3-5 3 1.52-9.11"/>'),
(N'printer',     N'<path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/>'),
(N'bed',         N'<path d="M2 4v16M2 8h18a2 2 0 0 1 2 2v10M2 17h20M6 8v9"/>'),
(N'baby',        N'<path d="M9 12h.01M15 12h.01M10 16c.5.3 1.2.5 2 .5s1.5-.2 2-.5"/><path d="M19 6.3a9 9 0 0 1 1.8 3.9 2 2 0 0 1 0 3.6 9 9 0 0 1-17.6 0 2 2 0 0 1 0-3.6A9 9 0 0 1 12 3c2 0 3.5 1.1 3.5 2.5s-.9 2.5-2 2.5c-.8 0-1.5-.4-1.5-1"/>'),
(N'lock',        N'<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>'),
(N'trophy',      N'<path d="M6 9H4.5a2.5 2.5 0 0 1 0-5H6M18 9h1.5a2.5 2.5 0 0 0 0-5H18M4 22h16M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22"/><path d="M18 2H6v7a6 6 0 0 0 12 0V2Z"/>'),
(N'file-text',   N'<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4M10 9H8M16 13H8M16 17H8"/>'),
(N'recycle',     N'<path d="M7 19H4.815a1.83 1.83 0 0 1-1.57-.881 1.785 1.785 0 0 1-.004-1.784L7.196 9.5M11 19h8.203a1.83 1.83 0 0 0 1.556-.89 1.784 1.784 0 0 0 0-1.775l-1.226-2.12M14 16l-3 3 3 3M8.293 13.596 7.196 9.5 3.1 10.598M9.344 5.811l1.093-1.892A1.83 1.83 0 0 1 11.985 3a1.784 1.784 0 0 1 1.546.888l3.943 6.843M13.378 9.633l4.096 1.098 1.097-4.096"/>'),
(N'ambulance',   N'<path d="M10 10H6M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2M19 18h2a1 1 0 0 0 1-1v-3.28a1 1 0 0 0-.684-.948l-1.923-.641a1 1 0 0 1-.578-.502l-1.539-3.076A1 1 0 0 0 16.382 8H14M8 8v4M9 18h6"/><circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/>'),
(N'bike',        N'<circle cx="18.5" cy="17.5" r="3.5"/><circle cx="5.5" cy="17.5" r="3.5"/><circle cx="15" cy="5" r="1"/><path d="M12 17.5V14l-3-3 4-3 2 3h2"/>'),
(N'cake',        N'<path d="M20 21v-8a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8M4 16s.5-1 2-1 2.5 2 4 2 2.5-2 4-2 2.5 2 4 2 2-1 2-1M2 21h20M7 8v3M12 8v3M17 8v3M7 4h.01M12 4h.01M17 4h.01"/>'),
(N'waves',       N'<path d="M2 6c.6.5 1.2 1 2.5 1C7 7 7 5 9.5 5c2.6 0 2.4 2 5 2 2.5 0 2.5-2 5-2 1.3 0 1.9.5 2.5 1M2 12c.6.5 1.2 1 2.5 1 2.5 0 2.5-2 5-2 2.6 0 2.4 2 5 2 2.5 0 2.5-2 5-2 1.3 0 1.9.5 2.5 1M2 18c.6.5 1.2 1 2.5 1 2.5 0 2.5-2 5-2 2.6 0 2.4 2 5 2 2.5 0 2.5-2 5-2 1.3 0 1.9.5 2.5 1"/>'),
(N'battery',     N'<rect x="2" y="7" width="16" height="10" rx="2"/><path d="M22 11v2M6 11v2M10 11v2"/>'),
(N'tractor',     N'<path d="m10 11 11 .9a1 1 0 0 1 .8 1.1l-.665 4.158a1 1 0 0 1-.988.842H20M16 18h-5M18 5a1 1 0 0 0-1 1v5.573M3 4h8.129a1 1 0 0 1 .99.863L13 11.246M4 11V4M7 15h.01M8 10.1V4"/><circle cx="18" cy="18" r="2"/><circle cx="7" cy="15" r="5"/>');

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
(N'automotive',             N'Automotive',             N'Car servicing, car wash and detailing, tyres and 24x7 roadside assistance.',             N'#1570EF', N'#E8F1FD', N'car',        11, 0),
(N'pet-care',               N'Pet Care',               N'Veterinary clinics, pet grooming, boarding and certified dog trainers.',                 N'#CA8504', N'#FEF7E6', N'paw',        12, 1),
(N'finance-tax',            N'Finance & Tax',          N'Chartered accountants, GST and income-tax filing, insurance and loan advisors.',          N'#3F621A', N'#EFF4EA', N'calculator', 13, 1),
(N'appliance-repair',       N'Appliance & Gadget Repair', N'Washing machine, refrigerator, RO purifier, mobile and laptop repairs at your doorstep.', N'#475467', N'#EEF0F3', N'plug',  14, 1),
(N'it-digital',             N'IT & Digital Services',  N'Website development, digital marketing, graphic design and CCTV installation.',         N'#0E7090', N'#E7F2F5', N'monitor',    15, 1),
(N'construction-renovation',N'Construction & Renovation', N'Civil contractors, waterproofing, rooftop solar, fabrication and flooring specialists.', N'#B42318', N'#FBE9E8', N'hard-hat', 16, 1),
(N'fashion-tailoring',      N'Fashion & Tailoring',    N'Tailors, designer boutiques and laundry & dry-cleaning services.',                       N'#A15C07', N'#F8EFE6', N'shirt',      17, 0),
(N'astrology-pooja',        N'Astrology & Pooja',      N'Astrologers, pandits for pooja and ceremonies, and vastu consultants.',                  N'#9F1AB1', N'#F8E8FA', N'moon-star',  18, 0),
(N'hotels-stays',           N'Hotels & Stays',         N'Hotels, homestays, service apartments and weekend resorts.',                             N'#C4320A', N'#FEF3EE', N'bed',        19, 0),
(N'sports-hobbies',         N'Sports & Hobbies',       N'Swimming, cricket, badminton, martial arts and chess coaching.',                         N'#2E90FA', N'#EFF8FF', N'trophy',     20, 0),
(N'family-care',            N'Child & Elder Care',     N'Babysitters, day care, elder care attendants, home nursing, maids and cooks.',           N'#EE46BC', N'#FDF2FA', N'baby',       21, 0),
(N'business-services',      N'Business Services',      N'Printing, courier, packaging, translation and scrap recycling for homes and offices.',   N'#344054', N'#F2F4F7', N'printer',    22, 0),
(N'documentation',          N'Documents & Govt Services', N'Passport, RTO, PAN and Aadhaar assistance and certificate registrations.',             N'#6172F3', N'#EEF4FF', N'file-text',  23, 0),
(N'agriculture',            N'Agriculture & Farming',  N'Tractor hire, seeds and fertilisers, borewells, drip irrigation and dairy suppliers.',   N'#4CA30D', N'#F3FEE7', N'tractor',    24, 0);

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
(N'tyres',              N'automotive',    N'Tyres & Alignment',     N'Tyre replacement, wheel alignment and balancing.',                                N'tyre',       4, 0, 1),
-- Additions to existing categories
(N'gardening',          N'home-services', N'Gardening & Landscaping', N'Garden maintenance, terrace gardens, lawn laying and vertical gardens.',       N'sprout',     8, 0, 1),
(N'eye-care',           N'healthcare',    N'Eye Clinics & Opticians', N'Eye check-ups, spectacles, contact lenses and LASIK consultations.',          N'eye',        7, 0, 1),
(N'ayurveda-homeopathy',N'healthcare',    N'Ayurveda & Homeopathy',   N'BAMS and BHMS doctors for chronic conditions, Panchakarma and wellness.',     N'leaf',       8, 0, 1),
(N'music-classes',      N'education',     N'Music Classes',           N'Carnatic, Hindustani, keyboard, guitar and drums for kids and adults.',       N'music',      5, 1, 1),
(N'dance-classes',      N'education',     N'Dance Classes',           N'Bharatanatyam, Kathak, Bollywood and contemporary dance studios.',            N'footprints', 6, 0, 1),
(N'language-classes',   N'education',     N'Language Classes',        N'Spoken English, IELTS, German, French and Japanese coaching.',                N'languages',  7, 0, 1),
(N'bridal-makeup',      N'beauty-wellness', N'Bridal Makeup Artists', N'HD and airbrush bridal makeup, hairstyling and draping for every ceremony.',   N'brush',      7, 0, 1),
(N'dj-sound',           N'events-weddings', N'DJ & Sound',            N'DJs, sound and lighting rentals for sangeets, receptions and parties.',       N'mic',        4, 0, 1),
-- Pet care
(N'veterinarians',      N'pet-care',      N'Veterinarians',           N'Pet clinics for vaccinations, check-ups, surgery and emergency care.',        N'paw',        1, 1, 1),
(N'pet-grooming',       N'pet-care',      N'Pet Grooming',            N'Bathing, haircuts, nail trimming and tick treatment at salon or home.',       N'scissors',   2, 1, 1),
(N'pet-boarding',       N'pet-care',      N'Pet Boarding & Daycare',  N'Supervised boarding, daycare and dog walking while you are away.',            N'home',       3, 0, 1),
(N'dog-trainers',       N'pet-care',      N'Dog Trainers',            N'Puppy training, obedience and behaviour correction by certified trainers.',   N'award',      4, 0, 1),
-- Finance & tax
(N'chartered-accountants', N'finance-tax', N'Chartered Accountants',  N'Audit, accounting, company compliance and business advisory.',                N'calculator', 1, 1, 1),
(N'tax-consultants',    N'finance-tax',   N'GST & Tax Consultants',   N'GST registration and returns, ITR filing and tax notices.',                   N'receipt',    2, 1, 1),
(N'insurance-advisors', N'finance-tax',   N'Insurance Advisors',      N'Health, term life, motor and business insurance with claim support.',         N'shield',     3, 0, 1),
(N'loan-advisors',      N'finance-tax',   N'Loan Advisors',           N'Home, business and loan-against-property assistance with partner banks.',     N'landmark',   4, 0, 1),
-- Appliance & gadget repair
(N'washing-machine-repair', N'appliance-repair', N'Washing Machine Repair', N'Front-load, top-load and semi-automatic repairs for all major brands.', N'washer',     1, 1, 1),
(N'refrigerator-repair', N'appliance-repair', N'Refrigerator Repair',  N'Cooling issues, gas charging, compressor and thermostat repairs.',            N'fridge',     2, 0, 1),
(N'ro-purifier-service', N'appliance-repair', N'RO Water Purifier',    N'RO installation, filter and membrane replacement, and AMC plans.',           N'droplets',   3, 0, 1),
(N'mobile-repair',      N'appliance-repair', N'Mobile Repair',        N'Screen and battery replacement, water damage and board-level repairs.',       N'smartphone', 4, 1, 1),
(N'laptop-repair',      N'appliance-repair', N'Laptop & Computer Repair', N'Laptop screen, keyboard, SSD upgrades, data recovery and OS installation.', N'laptop',  5, 0, 1),
-- IT & digital
(N'web-developers',     N'it-digital',    N'Website Developers',      N'Business websites, e-commerce stores and web applications.',                  N'code',       1, 1, 1),
(N'digital-marketing',  N'it-digital',    N'Digital Marketing',       N'SEO, Google Ads, social media management and local listing optimisation.',    N'megaphone',  2, 1, 1),
(N'graphic-designers',  N'it-digital',    N'Graphic Designers',       N'Logos, brand identity, packaging and social media creatives.',                N'pen-tool',   3, 0, 1),
(N'cctv-installation',  N'it-digital',    N'CCTV & Security Systems', N'CCTV cameras, video door phones, biometric access and alarm systems.',        N'scan-eye',   4, 0, 1),
-- Construction & renovation
(N'civil-contractors',  N'construction-renovation', N'Civil Contractors', N'Independent houses, extensions and turnkey construction with BOQ estimates.', N'hard-hat', 1, 1, 1),
(N'waterproofing',      N'construction-renovation', N'Waterproofing',     N'Terrace, bathroom and basement waterproofing with warranty.',             N'umbrella',   2, 0, 1),
(N'solar-installation', N'construction-renovation', N'Rooftop Solar',     N'On-grid rooftop solar with net metering and PM Surya Ghar subsidy support.', N'sun',     3, 1, 1),
(N'fabrication',        N'construction-renovation', N'Fabrication & Welding', N'MS and SS gates, grills, railings and roofing sheds.',                N'flame',      4, 0, 1),
(N'flooring',           N'construction-renovation', N'Tiles & Flooring',  N'Vitrified tiles, granite, marble polishing and wooden flooring.',          N'grid',       5, 0, 1),
-- Fashion & tailoring
(N'tailors',            N'fashion-tailoring', N'Tailors',             N'Blouse stitching, alterations, kurtas and men''s suits.',                     N'scissors',   1, 0, 1),
(N'boutiques',          N'fashion-tailoring', N'Designer Boutiques',  N'Bridal lehengas, custom sarees and ethnic wear designs.',                     N'bag',        2, 0, 1),
(N'laundry',            N'fashion-tailoring', N'Laundry & Dry Cleaning', N'Wash and iron, dry cleaning, shoe and curtain cleaning with pickup.',      N'shirt',      3, 0, 1),
-- Astrology & pooja
(N'astrologers',        N'astrology-pooja', N'Astrologers',           N'Vedic astrology, horoscope matching and muhurtham consultations.',            N'star',       1, 0, 1),
(N'pandits',            N'astrology-pooja', N'Pandits & Pooja Services', N'Griha pravesh, Satyanarayan pooja, homams and wedding rituals.',            N'flame',      2, 0, 1),
(N'vastu-consultants',  N'astrology-pooja', N'Vastu Consultants',     N'Vastu analysis for homes, plots, offices and factories.',                     N'compass',    3, 0, 1),
-- Hotels & stays
(N'hotels',             N'hotels-stays',  N'Hotels',                  N'Budget, business and premium hotels with verified guest reviews.',           N'bed',        1, 0, 1),
(N'homestays',          N'hotels-stays',  N'Homestays & Villas',      N'Family-run homestays and private villas for short stays.',                   N'home',       2, 0, 1),
(N'service-apartments', N'hotels-stays',  N'Service Apartments',      N'Furnished apartments with housekeeping for longer stays.',                   N'building',   3, 0, 1),
(N'resorts',            N'hotels-stays',  N'Resorts',                 N'Weekend getaways and day-outing resorts near the city.',                     N'sun',        4, 0, 1),
-- Sports & hobbies
(N'swimming-classes',   N'sports-hobbies', N'Swimming Classes',       N'Learn-to-swim and stroke-correction coaching for kids and adults.',          N'waves',      1, 0, 1),
(N'cricket-academies',  N'sports-hobbies', N'Cricket Academies',      N'Net sessions and coaching camps by certified cricket coaches.',              N'trophy',     2, 0, 1),
(N'sports-courts',      N'sports-hobbies', N'Badminton & Sports Courts', N'Hourly booking of badminton, box-cricket and turf courts.',                N'activity',   3, 0, 1),
(N'martial-arts',       N'sports-hobbies', N'Martial Arts & Self-defence', N'Karate, taekwondo, kickboxing and self-defence classes.',               N'award',      4, 0, 1),
(N'chess-classes',      N'sports-hobbies', N'Chess Classes',          N'Beginner to tournament-level chess coaching, online and offline.',          N'grid',       5, 0, 1),
-- Child & elder care
(N'babysitters',        N'family-care',   N'Babysitters & Nannies',   N'Verified babysitters and full-time nannies for infants and toddlers.',       N'baby',       1, 0, 1),
(N'daycare-creches',    N'family-care',   N'Day Care & Creches',      N'Day care and creches with meals, activities and CCTV access.',              N'school',     2, 0, 1),
(N'elder-care',         N'family-care',   N'Elder Care & Attendants', N'Trained attendants and companions for senior citizens at home.',             N'heart-pulse',3, 0, 1),
(N'home-nursing',       N'family-care',   N'Home Nursing',            N'Qualified nurses for post-surgery care, injections and wound dressing.',    N'stethoscope',4, 0, 1),
(N'maids-cooks',        N'family-care',   N'Maids & Cooks',           N'Background-verified house help and home cooks, part-time or full-time.',    N'home',       5, 0, 1),
-- Business services
(N'printing-press',     N'business-services', N'Printing & Flex Banners', N'Visiting cards, brochures, flex banners and signage printing.',          N'printer',    1, 0, 1),
(N'courier-services',   N'business-services', N'Courier & Parcel',    N'Domestic and international courier with doorstep pickup.',                   N'package',    2, 0, 1),
(N'packaging-suppliers',N'business-services', N'Packaging Suppliers', N'Corrugated boxes, tapes and custom packaging for businesses.',               N'package',    3, 0, 1),
(N'translation-services',N'business-services', N'Translation & Typing', N'Certified translation, typing and documentation support.',                N'file-text',  4, 0, 1),
(N'scrap-dealers',      N'business-services', N'Scrap Dealers & Recycling', N'Doorstep pickup of paper, metal, e-waste and old furniture.',          N'recycle',    5, 0, 1),
-- Documents & government services
(N'passport-visa',      N'documentation', N'Passport & Visa Assistance', N'Passport applications, renewals and visa documentation support.',      N'file-text',  1, 0, 1),
(N'rto-services',       N'documentation', N'RTO & Driving Licence Agents', N'Driving licence, vehicle registration and transfer paperwork.',          N'car',        2, 0, 1),
(N'pan-aadhaar-centres',N'documentation', N'PAN, Aadhaar & e-Seva Centres', N'PAN and Aadhaar updates, bill payments and online applications.',      N'file-text',  3, 0, 1),
(N'certificate-services',N'documentation', N'Certificates & Registrations', N'Birth, marriage and income certificates, and shop registrations.',     N'stamp',      4, 0, 1),
-- Agriculture & farming
(N'tractor-hire',       N'agriculture',   N'Tractor & Farm Equipment Hire', N'Tractors, harvesters and tillers on hourly or daily hire.',            N'tractor',    1, 0, 1),
(N'seeds-fertilisers',  N'agriculture',   N'Seeds & Fertiliser Dealers', N'Certified seeds, fertilisers and crop-protection products.',             N'sprout',     2, 0, 1),
(N'borewell-drilling',  N'agriculture',   N'Borewell Drilling',       N'Ground-water survey, borewell drilling and motor installation.',            N'droplets',   3, 0, 1),
(N'drip-irrigation',    N'agriculture',   N'Drip Irrigation',         N'Drip and sprinkler irrigation design and installation.',                     N'droplets',   4, 0, 1),
(N'dairy-suppliers',    N'agriculture',   N'Dairy & Milk Suppliers',  N'Fresh milk, curd and ghee delivered daily from local dairies.',              N'leaf',       5, 0, 1),
-- Additions to existing categories
(N'locksmiths',         N'home-services', N'Locksmiths',              N'Lock repairs, key duplication and digital door-lock installation.',          N'lock',       9, 0, 1),
(N'glass-aluminium',    N'home-services', N'Glass & Aluminium Works', N'Aluminium windows, glass partitions, mosquito mesh and mirrors.',             N'grid',      10, 0, 1),
(N'inverter-battery',   N'home-services', N'Inverter & Battery Services', N'Inverter installation, battery replacement and UPS servicing.',         N'battery',   11, 0, 1),
(N'tank-cleaning',      N'home-services', N'Water Tank Cleaning',     N'Overhead and sump tank cleaning with anti-bacterial treatment.',             N'droplets',  12, 0, 1),
(N'ambulance-services', N'healthcare',    N'Ambulance Services',      N'24x7 basic and advanced life-support ambulances.',                           N'ambulance',  9, 0, 1),
(N'blood-banks',        N'healthcare',    N'Blood Banks',             N'Licensed blood banks and blood-donation camps.',                             N'heart-pulse',10, 0, 1),
(N'abacus-classes',     N'education',     N'Abacus & Vedic Maths',    N'Mental arithmetic, abacus and vedic maths for children.',                    N'calculator', 8, 0, 1),
(N'bike-service',       N'automotive',    N'Bike Service & Repair',   N'Two-wheeler servicing, repairs and doorstep bike service.',                  N'bike',       5, 0, 1),
(N'driving-schools',    N'automotive',    N'Driving Schools',         N'Car and two-wheeler driving lessons with licence test support.',             N'car',        6, 0, 1),
(N'tattoo-studios',     N'beauty-wellness', N'Tattoo Studios',        N'Custom tattoos and piercing by licensed artists in hygienic studios.',      N'pen-tool',   8, 0, 1),
(N'nail-art',           N'beauty-wellness', N'Nail Art Studios',      N'Gel extensions, nail art and manicures.',                                    N'brush',      9, 0, 1),
(N'mehendi-artists',    N'events-weddings', N'Mehendi Artists',       N'Bridal and party mehendi at home or venue.',                                 N'flower',     5, 0, 1),
(N'wedding-cards',      N'events-weddings', N'Wedding Cards & Invitations', N'Printed and digital wedding invitations and favours.',                 N'printer',    6, 0, 1),
(N'sweet-shops',        N'food-dining',   N'Sweet Shops',             N'Traditional sweets, namkeen and festive gift boxes.',                        N'cake',       4, 0, 1),
(N'tiffin-services',    N'food-dining',   N'Tiffin & Meal Services',  N'Home-style daily meal subscriptions for students and offices.',              N'utensils',   5, 0, 1),
(N'bus-hire',           N'travel-transport', N'Bus & Tempo Traveller Hire', N'Buses and tempo travellers for weddings, trips and staff transport.',  N'truck',      4, 0, 1),
(N'pet-shops',          N'pet-care',      N'Pet Food & Accessories',  N'Pet food, toys, beds and grooming supplies.',                                N'bag',        5, 0, 1);

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
