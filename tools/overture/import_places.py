"""
Imports a country's places from Overture Maps (open data: https://docs.overturemaps.org/) into dbo.OpenPlaces, so the "AI recommended"
search finds them locally: no Open Places API key, no quota, no network at search time.

    cd tools/overture
    python -m venv .venv
    .venv/Scripts/python -m pip install -r requirements.txt
    .venv/Scripts/python import_places.py --database CallingBell            # India, latest release
    .venv/Scripts/python import_places.py --database CallingBell_Prod --release 2026-10-21.0

Steps: download the country from Overture's public S3 bucket with DuckDB (about 5 minutes for India; kept in data/ and reused), write a
tab-separated file, bulk-load it with bcp into dbo.OpenPlaces_Load, keep any places the API saved for other countries, index it, then swap
it in for dbo.OpenPlaces in one transaction (searches never see a half-loaded table) and record the import in dbo.OpenPlacesImports.
Run database/scripts/43_OpenPlaces.sql first. Overture publishes a release every month; re-run to refresh.
"""
import argparse
import datetime as dt
import os
import subprocess
import sys
import time

import duckdb

BUCKET = "s3://overturemaps-us-west-2/release"
# Bounding boxes used to read only the country's part of the world (Overture's files are sorted by area); the country filter is exact.
COUNTRY_BBOX = {"IN": (68.0, 6.0, 98.0, 37.5)}
HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "data")


def log(message):
    print(f"[{dt.datetime.now():%H:%M:%S}] {message}", flush=True)


def connect():
    con = duckdb.connect()
    con.execute("INSTALL httpfs; LOAD httpfs; INSTALL spatial; LOAD spatial; SET s3_region='us-west-2';")
    return con


def latest_release(con):
    files = con.execute(f"SELECT file FROM glob('{BUCKET}/*/theme=places/type=place/*.parquet')").fetchall()
    return sorted({f[0].split("/release/")[1].split("/")[0] for f in files})[-1]


def download(con, country, release, bbox):
    """The country's places as a local Parquet file (reused when it already exists)."""
    path = os.path.join(DATA, f"places-{country}-{release}.parquet")
    if os.path.exists(path):
        log(f"Using {path}")
        return path
    os.makedirs(DATA, exist_ok=True)
    west, south, east, north = bbox
    log(f"Downloading {country} places from Overture {release} (several minutes)...")
    started = time.time()
    con.execute(f"""COPY (
        SELECT id, names."primary" AS name, confidence, operating_status, taxonomy, addresses, phones, websites,
               list_transform(sources, lambda s: s.dataset) AS datasets, ST_Y(geometry) AS lat, ST_X(geometry) AS lon
        FROM read_parquet('{BUCKET}/{release}/theme=places/type=place/*.parquet')
        WHERE bbox.xmin BETWEEN {west} AND {east} AND bbox.ymin BETWEEN {south} AND {north} AND addresses[1].country = '{country}'
    ) TO '{path.replace(os.sep, "/")}' (FORMAT parquet, COMPRESSION zstd)""")
    log(f"Downloaded in {time.time() - started:.0f}s: {path}")
    return path


def write_tsv(con, parquet, release, min_confidence):
    """Rows in dbo.OpenPlaces' column order; tabs and line breaks in values become spaces, empty fields load as NULL."""
    tsv = parquet.replace(".parquet", ".tsv")
    now = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%d %H:%M:%S +00:00")

    def clean(expr, length):
        return f"NULLIF(LEFT(trim(regexp_replace({expr}, '[\\t\\r\\n]+', ' ', 'g')), {length}), '')"

    log("Writing the load file...")
    con.execute(f"""COPY (
        SELECT
            id,
            {clean('name', 300)},
            {clean('taxonomy."primary"', 100)},
            CASE WHEN len(taxonomy.alternates) > 0 THEN LEFT(',' || array_to_string(taxonomy.alternates, ',') || ',', 400) END,
            {clean("array_to_string(taxonomy.hierarchy, '/')", 300)},
            {clean("concat_ws(', ', NULLIF(trim(addresses[1].freeform), ''), NULLIF(trim(addresses[1].locality), ''), NULLIF(trim(concat_ws(' ', addresses[1].region, addresses[1].postcode)), ''))", 500)},
            {clean('addresses[1].locality', 150)},
            {clean('addresses[1].region', 150)},
            {clean('addresses[1].postcode', 20)},
            addresses[1].country,
            round(lat, 6), round(lon, 6),
            CAST(floor((lat + 90) / 0.1) AS INTEGER) * 10000 + CAST(floor((lon + 180) / 0.1) AS INTEGER),
            {clean('phones[1]', 50)},
            {clean('websites[1]', 500)},
            round(confidence, 3),
            NULLIF(LEFT(array_to_string(list_filter(list_distinct(datasets), lambda d: d <> 'Overture'), ','), 200), ''),
            '{release}', '{now}', '{now}'
        FROM read_parquet('{parquet.replace(os.sep, "/")}')
        WHERE name IS NOT NULL AND trim(name) <> '' AND confidence >= {min_confidence}
          AND coalesce(operating_status, 'open') NOT IN ('permanently_closed', 'temporarily_closed')
    ) TO '{tsv.replace(os.sep, "/")}' (FORMAT csv, DELIMITER '\t', HEADER false, QUOTE '', ESCAPE '', NULL '')""")
    return tsv


def sql(server, database, statement):
    subprocess.run(["sqlcmd", "-S", server, "-E", "-C", "-I", "-b", "-d", database, "-Q", statement], check=True)


def main():
    parser = argparse.ArgumentParser(description="Import Overture Maps places into dbo.OpenPlaces.")
    parser.add_argument("--country", default="IN")
    parser.add_argument("--release", help="Overture release, e.g. 2026-10-21.0 (default: the latest)")
    parser.add_argument("--server", default=r".\SQLEXPRESS")
    parser.add_argument("--database", required=True)
    parser.add_argument("--min-confidence", type=float, default=0.2)
    args = parser.parse_args()
    country = args.country.upper()
    if country not in COUNTRY_BBOX:
        sys.exit(f"No bounding box for {country}: add it to COUNTRY_BBOX.")

    con = connect()
    release = args.release or latest_release(con)
    log(f"Overture release {release}, country {country}, database {args.database}")
    tsv = write_tsv(con, download(con, country, release, COUNTRY_BBOX[country]), release, args.min_confidence)

    stamp = dt.datetime.now().strftime("%Y%m%d%H%M%S")
    sql(args.server, args.database, """
        IF OBJECT_ID('dbo.OpenPlaces_Load') IS NOT NULL DROP TABLE dbo.OpenPlaces_Load;
        SELECT TOP 0 * INTO dbo.OpenPlaces_Load FROM dbo.OpenPlaces;""")
    log("Bulk loading (bcp)...")
    started = time.time()
    errors = tsv + ".errors.txt"
    # -u trusts the local server's certificate (ODBC 18); the row terminator is a bare LF (bcp reads "\n" as CRLF on Windows).
    bcp = subprocess.run(["bcp", "dbo.OpenPlaces_Load", "in", tsv, "-S", args.server, "-d", args.database, "-T", "-u", "-c", "-C", "65001",
                          "-t", r"\t", "-r", "0x0a", "-b", "100000", "-h", "TABLOCK", "-m", "1000", "-e", errors],
                         capture_output=True, text=True)
    copied = next((line.strip() for line in bcp.stdout.splitlines() if "rows copied" in line), "")
    if bcp.returncode != 0 or not copied:
        sys.exit("bcp failed:\n" + "\n".join(bcp.stdout.splitlines()[-15:]))
    log(f"Loaded in {time.time() - started:.0f}s: {copied}")

    log("Indexing and swapping in...")
    sql(args.server, args.database, f"""
        SET XACT_ABORT ON;
        -- Places the API saved for other countries (not part of this import) are kept.
        INSERT INTO dbo.OpenPlaces_Load SELECT o.* FROM dbo.OpenPlaces o
        WHERE (o.CountryCode IS NULL OR o.CountryCode <> '{country}')
          AND NOT EXISTS (SELECT 1 FROM dbo.OpenPlaces_Load l WHERE l.PlaceId = o.PlaceId);
        -- Overture ids are unique; a repeated row (it happens across source datasets) keeps one copy.
        WITH d AS (SELECT ROW_NUMBER() OVER (PARTITION BY PlaceId ORDER BY Confidence DESC) n FROM dbo.OpenPlaces_Load) DELETE FROM d WHERE n > 1;
        -- Page compression about halves the table, so more of it stays in SQL Server's memory.
        CREATE CLUSTERED INDEX IX_OpenPlaces_Cell_{stamp} ON dbo.OpenPlaces_Load (Cell) WITH (DATA_COMPRESSION = PAGE);
        ALTER TABLE dbo.OpenPlaces_Load ADD CONSTRAINT PK_OpenPlaces_{stamp} PRIMARY KEY NONCLUSTERED (PlaceId) WITH (DATA_COMPRESSION = PAGE);
        BEGIN TRANSACTION;
            EXEC sp_rename 'dbo.OpenPlaces', 'OpenPlaces_Old';
            EXEC sp_rename 'dbo.OpenPlaces_Load', 'OpenPlaces';
            DROP TABLE dbo.OpenPlaces_Old;
            EXEC sp_rename 'dbo.OpenPlaces.IX_OpenPlaces_Cell_{stamp}', 'IX_OpenPlaces_Cell', 'INDEX';
            EXEC sp_rename 'dbo.PK_OpenPlaces_{stamp}', 'PK_OpenPlaces', 'OBJECT';
            MERGE dbo.OpenPlacesImports AS t
            USING (SELECT '{country}' AS CountryCode, '{release}' AS Release, COUNT(*) AS Places,
                          MIN(Latitude) AS MinLatitude, MAX(Latitude) AS MaxLatitude, MIN(Longitude) AS MinLongitude, MAX(Longitude) AS MaxLongitude
                   FROM dbo.OpenPlaces WHERE CountryCode = '{country}' AND Release = '{release}') AS s
            ON t.CountryCode = s.CountryCode
            WHEN MATCHED THEN UPDATE SET Release = s.Release, Places = s.Places, MinLatitude = s.MinLatitude, MaxLatitude = s.MaxLatitude,
                 MinLongitude = s.MinLongitude, MaxLongitude = s.MaxLongitude, ImportedOn = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN INSERT (CountryCode, Release, Places, MinLatitude, MaxLatitude, MinLongitude, MaxLongitude, ImportedOn)
                 VALUES (s.CountryCode, s.Release, s.Places, s.MinLatitude, s.MaxLatitude, s.MinLongitude, s.MaxLongitude, SYSDATETIMEOFFSET());
        COMMIT;
        SELECT CountryCode, Release, Places, ImportedOn FROM dbo.OpenPlacesImports;""")
    os.remove(tsv)
    if os.path.exists(errors) and os.path.getsize(errors) == 0:
        os.remove(errors)
    log("Done. The API picks the import up within 10 minutes.")


if __name__ == "__main__":
    main()
