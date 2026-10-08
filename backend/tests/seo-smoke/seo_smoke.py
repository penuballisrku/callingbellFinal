"""
SEO smoke test against a running Calling Bell API that serves the built app (Seo:SpaRoot -> web/dist) and its SQL Server database.

Checks what crawlers receive: robots.txt, the sitemaps, and for business, category and location pages the status code, title,
description, canonical URL, robots rule, Open Graph tags, JSON-LD (parsed and compared with the database) and internal links;
empty location pages (noindex), canonical handling of tracking parameters and old addresses, 404 / 410, and search pages.

With --mutate it also changes data temporarily - renames a business (old address must answer 301) and adds a duplicate listing (must
point its canonical at the original and be noindex) - and restores everything afterwards.

Usage:  python seo_smoke.py --base http://localhost:5081 [--server .\\SQLEXPRESS --database CallingBell] [--mutate]
"""
from __future__ import annotations

import argparse
import html
import json
import re
import subprocess
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

NS = {"s": "http://www.sitemaps.org/schemas/sitemap/0.9"}
results: list[tuple[bool, str]] = []


def check(ok: bool, name: str) -> bool:
    results.append((ok, name))
    print(("PASS " if ok else "FAIL ") + name)
    return ok


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):  # report 301s instead of following them
        return None


OPENER = urllib.request.build_opener(NoRedirect)


def get(url: str) -> tuple[int, dict, str]:
    try:
        with OPENER.open(urllib.request.Request(url, headers={"User-Agent": "CallingBell-seo-smoke/1.0"}), timeout=60) as r:
            return r.status, dict(r.headers), r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, dict(e.headers), e.read().decode("utf-8", "replace")


def sql(args, query: str) -> list[str]:
    out = subprocess.run(["sqlcmd", "-S", args.server, "-d", args.database, "-E", "-C", "-I", "-h", "-1", "-W", "-b", "-Q", "SET NOCOUNT ON; " + query],
                         capture_output=True, text=True, check=True).stdout
    return [line.strip() for line in out.splitlines() if line.strip()]


def head(page: str) -> dict:
    meta = lambda attr, key: (m.group(1) if (m := re.search(rf'<meta {attr}="{re.escape(key)}" content="([^"]*)"', page)) else None)
    canonical = re.search(r'<link rel="canonical" href="([^"]*)"', page)
    title = re.search(r"<title>(.*?)</title>", page, re.S)
    lds = [json.loads(x) for x in re.findall(r'<script type="application/ld\+json" data-seo>(.*?)</script>', page, re.S)]
    return {
        "title": html.unescape(title.group(1)) if title else None,
        "description": html.unescape(meta("name", "description") or ""),
        "robots": meta("name", "robots"),
        "canonical": html.unescape(canonical.group(1)) if canonical else None,
        "og_title": meta("property", "og:title"), "og_image": meta("property", "og:image"), "twitter": meta("name", "twitter:card"),
        "jsonld": lds, "types": [x.get("@type") for x in lds],
        "h1": re.sub("<[^>]+>", "", m.group(1)) if (m := re.search(r"<h1[^>]*>(.*?)</h1>", page, re.S)) else None,
        "links": re.findall(r'<a href="(/[^"]*)"', page),
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5081")
    ap.add_argument("--server", default=".\\SQLEXPRESS")
    ap.add_argument("--database", default="CallingBell")
    ap.add_argument("--mutate", action="store_true")
    args = ap.parse_args()
    base = args.base.rstrip("/")

    # ---------------- robots.txt ----------------
    status, _, robots = get(base + "/robots.txt")
    check(status == 200 and "Sitemap: " in robots and "Disallow: /admin" in robots and "Allow: /api/media/" in robots
          and not re.search(r"^Disallow: /(business|category|location)\b", robots, re.M), "robots.txt: sitemap listed, private areas closed, public pages open")

    # ---------------- sitemaps ----------------
    status, _, index_xml = get(base + "/sitemap.xml")
    files = [e.text for e in ET.fromstring(index_xml).findall("s:sitemap/s:loc", NS)]
    check(status == 200 and len(files) >= 4, f"sitemap.xml is an index of {len(files)} files")
    site = re.match(r"https?://[^/]+", files[0]).group(0)
    urls: list[str] = []
    for f in files:
        st, _, body = get(base + f[len(site):])
        locs = [e.text for e in ET.fromstring(body).findall("s:url/s:loc", NS)]
        urls += locs
        check(st == 200 and 0 < len(locs) <= 50000, f"{f[len(site):]} is valid XML with {len(locs)} URLs")
    check(len(urls) == len(set(urls)), "sitemap URLs are unique")
    check(not any(re.search(r"/(search|login|register|account|owner|admin)\b|\?", u) for u in urls), "sitemaps hold no search, private or query-string URLs")
    sample = [u for u in urls if "/location/" in u][:6] + [u for u in urls if "/business/" in u][:6] + [u for u in urls if "/category/" in u][:3]
    bad = []
    for u in sample:
        st, _, page = get(base + u[len(site):])
        h = head(page)
        if st != 200 or not (h["robots"] or "").startswith("index") or h["canonical"] != u:
            bad.append((u, st, h["robots"], h["canonical"]))
    check(not bad, f"{len(sample)} sitemap URLs answer 200, index, canonical = sitemap URL" + (f" ({bad})" if bad else ""))

    # ---------------- business page ----------------
    slug, name, phone, city, has_reviews = sql(args, "SELECT TOP 1 Slug + '|' + Name + '|' + ISNULL(PhoneNumber,'') + '|' + City + '|' + "
                                                     "CAST(ReviewCount AS varchar) FROM Businesses WHERE Status='Active' AND ReviewCount > 0 AND PhoneNumber IS NOT NULL "
                                                     "ORDER BY ReviewCount DESC")[0].split("|")
    st, headers, page = get(f"{base}/business/{slug}")
    h = head(page)
    biz = next((x for x in h["jsonld"] if x.get("@type") not in ("BreadcrumbList", "FAQPage")), {})
    check(st == 200 and h["title"].startswith(f"{name} | ") and h["title"].endswith("| Calling Bell"), f"business title: {h['title']}")
    check(h["description"].startswith(f"Find {name}") and len(h["description"]) <= 160, "business meta description from real data")
    check(h["canonical"] == f"{site}/business/{slug}" and h["robots"].startswith("index"), "business canonical and robots")
    check(bool(h["og_title"]) and h["twitter"] in ("summary", "summary_large_image"), "business Open Graph / Twitter tags")
    check(biz.get("telephone") == phone and biz.get("address", {}).get("addressLocality") == city, "LocalBusiness phone and city match the database")
    check(biz.get("@id") == f"{site}/business/{slug}#business" and "aggregateRating" in biz, "LocalBusiness entity id and genuine rating")
    check("BreadcrumbList" in h["types"] and "FAQPage" in h["types"], "business breadcrumb and FAQ structured data")
    check(h["h1"] == html.escape(name, quote=False) or html.unescape(h["h1"] or "") == name, "business h1 is the business name")
    check("<address" in page and any(l.startswith("/location/") for l in h["links"]), "business address element and location links in the HTML")
    alts = re.findall(r'<img [^>]*alt="([^"]*)"', page)
    check(all(a and a.lower() not in ("image", "photo", "business") for a in alts), f"business images have descriptive alt text ({len(alts)})")
    st, _, page = get(f"{base}/business/{slug}?utm_source=google&utm_medium=cpc")
    check(head(page)["canonical"] == f"{site}/business/{slug}", "tracking parameters: canonical is the clean URL")

    # ---------------- category and location pages ----------------
    st, _, page = get(base + "/category/electrical")
    h = head(page)
    check(st == 200 and h["canonical"] == f"{site}/category/electrical" and "ItemList" in h["types"], f"category page: {h['title']}")
    st, _, page = get(base + "/categories/home-services")
    check(head(page)["canonical"] == f"{site}/category/home-services", "old /categories/ address: canonical is /category/")
    loc = next(u for u in urls if re.search(r"/location/[^/]+/[^/]+/[^/]+/[^/]+/[^/]+$", u))
    st, _, page = get(base + loc[len(site):])
    h = head(page)
    crumbs = next((x for x in h["jsonld"] if x.get("@type") == "BreadcrumbList"), {}).get("itemListElement", [])
    check(st == 200 and len(crumbs) == 6, f"area + category page with country/state/city/area/category breadcrumbs: {loc[len(site):]}")
    check(any(l.startswith("/business/") for l in h["links"]), "location page links to its businesses")
    empty_city = sql(args, "SELECT TOP 1 LOWER(s.CountryCode) + '|' + s.Slug + '|' + c.Slug FROM Cities c JOIN States s ON s.Id = c.StateId "
                           "WHERE c.IsActive = 1 AND s.CountryCode = 'IN' AND NOT EXISTS (SELECT 1 FROM Businesses b WHERE b.CityId = c.Id AND b.Status = 'Active')")[0].split("|")
    st, headers, page = get(f"{base}/location/india/{empty_city[1]}/{empty_city[2]}")
    check(st == 200 and head(page)["robots"] == "noindex,follow" and headers.get("X-Robots-Tag") == "noindex,follow"
          and f"/location/india/{empty_city[1]}/{empty_city[2]}" not in "".join(urls), "empty location page is noindex,follow and not in the sitemap")
    st, headers, _ = get(f"{base}/location/india/karnataka/hyderabad")
    check(st == 301 and headers.get("Location") == "/location/india/telangana/hyderabad", "city under the wrong state redirects to its canonical page")

    # ---------------- other status codes ----------------
    st, headers, _ = get(f"{base}/b/{slug}")
    check(st == 301 and headers.get("Location") == f"/business/{slug}", "old /b/ address answers 301")
    st, _, page = get(base + "/business/this-business-does-not-exist")
    check(st == 404 and head(page)["robots"] == "noindex,follow", "unknown business answers 404 (not a redirect to home)")
    gone = sql(args, "SELECT TOP 1 Slug FROM Businesses WHERE Status IN ('Inactive','Suspended')")
    if gone:
        st, _, _ = get(f"{base}/business/{gone[0]}")
        check(st == 410, "removed business answers 410 Gone")
    st, _, page = get(base + "/search?q=electrician")
    check(st == 200 and head(page)["robots"] == "noindex,follow", "search results are noindex,follow")
    st, _, page = get(base + "/")
    check(st == 200 and {"Organization", "WebSite"} <= set(head(page)["types"]), "home page has Organization and WebSite structured data")

    # ---------------- slug change and duplicate listing (temporary data changes) ----------------
    if args.mutate:
        new_slug = slug + "-renamed"
        sql(args, f"UPDATE Businesses SET Slug = '{new_slug}', ModifiedOn = SYSDATETIMEOFFSET() WHERE Slug = '{slug}'")
        try:
            st, headers, _ = get(f"{base}/business/{slug}")
            check(st == 301 and headers.get("Location") == f"/business/{new_slug}", "renamed business: old slug answers 301 to the new one")
            st, _, page = get(f"{base}/business/{new_slug}")
            check(st == 200 and head(page)["canonical"] == f"{site}/business/{new_slug}", "renamed business: new slug is canonical")
        finally:
            sql(args, f"UPDATE Businesses SET Slug = '{slug}', ModifiedOn = SYSDATETIMEOFFSET() WHERE Slug = '{new_slug}'")
            # The test's temporary slug isn't a real former address: take it off the redirect history.
            sql(args, f"DELETE FROM BusinessSlugHistory WHERE Slug = '{new_slug}'")
        st, _, _ = get(f"{base}/business/{slug}")
        check(st == 200, "renamed back: original address works again")

        dup = slug + "-duplicate"
        sql(args, f"SELECT * INTO #d FROM Businesses WHERE Slug = '{slug}'; UPDATE #d SET Id = NEWID(), Slug = '{dup}', CreatedOn = DATEADD(day, 1, CreatedOn), "
                  f"ModifiedOn = SYSDATETIMEOFFSET(); INSERT INTO Businesses SELECT * FROM #d")
        try:
            st, _, page = get(f"{base}/business/{dup}")
            h = head(page)
            check(st == 200 and h["canonical"] == f"{site}/business/{slug}" and h["robots"] == "noindex,follow",
                  "duplicate listing: canonical is the original, noindex")
        finally:
            sql(args, f"DELETE FROM BusinessSlugHistory WHERE BusinessId IN (SELECT Id FROM Businesses WHERE Slug = '{dup}'); DELETE FROM Businesses WHERE Slug = '{dup}'")

    failed = [n for ok, n in results if not ok]
    print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
