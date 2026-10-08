using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CallingBell.Application.Features.Businesses;

namespace CallingBell.Application.Features.Seo;

/// <summary>
/// Schema.org JSON-LD documents built only from real data: a property is left out when the database has no value for it (no invented
/// ratings, reviews, phone numbers, hours or prices). Serialised with HTML-sensitive characters escaped (&lt; &gt; &amp; and quotes), so
/// database text can never close the &lt;script&gt; element it is placed in.
/// </summary>
public static class SchemaOrg
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.Default, WriteIndented = false };

    public static string Serialize(JsonObject node) => node.ToJsonString(Json);

    public static JsonObject Organization(SeoOptions o)
    {
        var site = o.SiteUrl.TrimEnd('/');
        var node = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["@id"] = $"{site}/#organization",
            ["name"] = o.SiteName,
            ["url"] = site + "/",
        };
        if (!string.IsNullOrWhiteSpace(o.LogoUrl)) node["logo"] = SeoPaths.Absolute(site, o.LogoUrl);
        if (o.SameAs.Length > 0) node["sameAs"] = new JsonArray(o.SameAs.Select(s => (JsonNode)s).ToArray());
        return node;
    }

    /// <summary>WebSite with a SearchAction: the site's search page takes the query as ?q=.</summary>
    public static JsonObject WebSite(SeoOptions o)
    {
        var site = o.SiteUrl.TrimEnd('/');
        return new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["@id"] = $"{site}/#website",
            ["name"] = o.SiteName,
            ["url"] = site + "/",
            ["inLanguage"] = o.Language,
            ["publisher"] = new JsonObject { ["@id"] = $"{site}/#organization" },
            ["potentialAction"] = new JsonObject
            {
                ["@type"] = "SearchAction",
                ["target"] = new JsonObject { ["@type"] = "EntryPoint", ["urlTemplate"] = $"{site}/search?q={{search_term_string}}" },
                ["query-input"] = "required name=search_term_string",
            },
        };
    }

    public static JsonObject Breadcrumbs(IReadOnlyList<SeoLink> crumbs, string siteUrl) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = new JsonArray(crumbs.Select((c, i) => (JsonNode)new JsonObject
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = c.Name,
            ["item"] = SeoPaths.Absolute(siteUrl, c.Url),
        }).ToArray()),
    };

    /// <summary>FAQPage for questions that are shown on the page (never for hidden text).</summary>
    public static JsonObject Faq(IReadOnlyList<SeoFaq> faq) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "FAQPage",
        ["mainEntity"] = new JsonArray(faq.Select(f => (JsonNode)new JsonObject
        {
            ["@type"] = "Question",
            ["name"] = f.Question,
            ["acceptedAnswer"] = new JsonObject { ["@type"] = "Answer", ["text"] = f.Answer },
        }).ToArray()),
    };

    /// <summary>The businesses a category / location page lists, in order, as links to their own pages.</summary>
    public static JsonObject ItemList(string name, IReadOnlyList<(string Name, string Url)> items) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "ItemList",
        ["name"] = name,
        ["numberOfItems"] = items.Count,
        ["itemListElement"] = new JsonArray(items.Select((x, i) => (JsonNode)new JsonObject
        {
            ["@type"] = "ListItem", ["position"] = i + 1, ["name"] = x.Name, ["url"] = x.Url,
        }).ToArray()),
    };

    /// <summary>
    /// The most specific Schema.org LocalBusiness subtype the business's category clearly is; LocalBusiness otherwise. Matched on the
    /// sub-category (then category) name and slug.
    /// </summary>
    public static string BusinessType(string? subCategory, string? category)
    {
        foreach (var text in new[] { subCategory, category })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var words = text.Replace('-', ' ');
            foreach (var (pattern, type) in TypeRules)
                if (pattern.IsMatch(words)) return type;
        }
        return "LocalBusiness";
    }

    private static Regex Words(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Most specific first; whole words only ("spa" is not "space").</summary>
    private static readonly (Regex Pattern, string Type)[] TypeRules =
    [
        (Words(@"\belectric(al|ians?)?\b"), "Electrician"),
        (Words(@"\bplumb(ers?|ing)\b"), "Plumber"),
        (Words(@"\b(ac|air[\s]?condition(ing|er)?s?|hvac)\b"), "HVACBusiness"),
        (Words(@"\blocksmiths?\b"), "Locksmith"),
        (Words(@"\bpaint(ers?|ing)\b"), "HousePainter"),
        (Words(@"\broof(ing|ers?)\b"), "RoofingContractor"),
        (Words(@"\b(carpent(ers?|ry)|interior|contractors?|construction|renovations?)\b"), "HomeAndConstructionBusiness"),
        (Words(@"\b(packers|movers|moving)\b"), "MovingCompany"),
        (Words(@"\b(dentists?|dental)\b"), "Dentist"),
        (Words(@"\bhospitals?\b"), "Hospital"),
        (Words(@"\b(pharmac(y|ies)|chemists?)\b"), "Pharmacy"),
        (Words(@"\b(veterinar(y|ians?)|vets?|pet clinics?)\b"), "VeterinaryCare"),
        (Words(@"\b(doctors?|physicians?|clinics?|paediatrics?|pediatrics?|dermatolog\w*|gyn(a)?ecolog\w*|physiotherap\w*)\b"), "MedicalClinic"),
        (Words(@"\b(salons?|barbers?|beauty|make[\s]?up|bridal|parlou?rs?)\b"), "BeautySalon"),
        (Words(@"\b(spas?|massages?)\b"), "DaySpa"),
        (Words(@"\b(gyms?|fitness|yoga)\b"), "ExerciseGym"),
        (Words(@"\b(restaurants?|dining|biryani)\b"), "Restaurant"),
        (Words(@"\b(cafes?|cafés?|coffee|bakery|bakeries)\b"), "CafeOrCoffeeShop"),
        (Words(@"\b(hotels?|lodging|resorts?)\b"), "Hotel"),
        (Words(@"\b(car|auto|bike|two[\s]?wheeler)[\s]+(repairs?|services?|mechanics?)\b|\bmechanics?\b|\bgarages?\b"), "AutoRepair"),
        (Words(@"\b(real[\s]+estate|propert(y|ies)|realtors?)\b"), "RealEstateAgent"),
        (Words(@"\b(lawyers?|advocates?|legal|attorneys?)\b"), "LegalService"),
        (Words(@"\b(chartered[\s]+accountants?|accountants?|accounting|tax[\s]+consultants?|bookkeeping)\b"), "AccountingService"),
        (Words(@"\b(travel[\s]+agen(ts?|cy|cies)|tours?)\b"), "TravelAgency"),
        (Words(@"\b(child[\s]?care|day[\s]?care|creches?|pre[\s]?schools?)\b"), "ChildCare"),
        (Words(@"\b(stores?|shops?|grocer(y|ies)|supermarkets?)\b"), "Store"),
    ];

    /// <summary>LocalBusiness (or a subtype) for a business page, from its record in SQL Server only.</summary>
    public static JsonObject LocalBusiness(BusinessDetailDto b, string pageUrl, string? countryName, string? stateName, IReadOnlyList<string> imageUrls)
    {
        var c = b.Card;
        var node = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = BusinessType(c.SubCategoryName ?? c.SubCategorySlug, c.CategoryName),
            ["@id"] = pageUrl + "#business",
            ["name"] = c.Name,
            ["url"] = pageUrl,
        };
        if (!string.IsNullOrWhiteSpace(b.Description)) node["description"] = SeoText.Clamp(b.Description, 500);
        if (!string.IsNullOrWhiteSpace(c.Tagline)) node["slogan"] = c.Tagline;
        if (imageUrls.Count > 0) node["image"] = new JsonArray(imageUrls.Select(u => (JsonNode)u).ToArray());
        if (!string.IsNullOrWhiteSpace(c.LogoUrl) && imageUrls.Count > 0) node["logo"] = imageUrls[0];
        if (!string.IsNullOrWhiteSpace(b.PhoneNumber)) node["telephone"] = b.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(b.Email)) node["email"] = b.Email;

        var address = new JsonObject { ["@type"] = "PostalAddress" };
        var street = SeoText.JoinAddress(b.AddressLine, c.Area);
        if (street.Length > 0) address["streetAddress"] = street;
        if (!string.IsNullOrWhiteSpace(c.City)) address["addressLocality"] = c.City;
        if (!string.IsNullOrWhiteSpace(stateName)) address["addressRegion"] = stateName;
        if (!string.IsNullOrWhiteSpace(b.Pincode)) address["postalCode"] = b.Pincode;
        if (!string.IsNullOrWhiteSpace(b.CountryCode)) address["addressCountry"] = b.CountryCode;
        if (address.Count > 1) node["address"] = address;

        if (b.Latitude is { } lat && b.Longitude is { } lng)
            node["geo"] = new JsonObject
            {
                ["@type"] = "GeoCoordinates",
                ["latitude"] = Math.Round(lat, 6),
                ["longitude"] = Math.Round(lng, 6),
            };

        var areaServed = new[] { c.Area, c.City }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (areaServed.Count > 0) node["areaServed"] = new JsonArray(areaServed.Select(a => (JsonNode)new JsonObject { ["@type"] = "Place", ["name"] = a }).ToArray());

        // The business's own website and verified social profiles identify the same entity elsewhere.
        var sameAs = new[] { b.Website }.Concat(b.SocialLinks.Select(l => l.Url))
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == Uri.UriSchemeHttps || x.Scheme == Uri.UriSchemeHttp))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sameAs.Count > 0) node["sameAs"] = new JsonArray(sameAs.Select(u => (JsonNode)u!).ToArray());

        var hours = OpeningHours(b.Hours);
        if (hours.Count > 0) node["openingHoursSpecification"] = hours;

        var currency = Currency(b.CountryCode);
        var offers = b.Services.Where(s => !string.IsNullOrWhiteSpace(s.Name)).Take(30).Select(s =>
        {
            var service = new JsonObject { ["@type"] = "Service", ["name"] = s.Name };
            if (!string.IsNullOrWhiteSpace(s.Description)) service["description"] = SeoText.Clamp(s.Description, 300);
            var offer = new JsonObject { ["@type"] = "Offer", ["itemOffered"] = service };
            // A price only when the business set one (0 means "on request"), in the business's own currency.
            if (s.Price > 0 && currency is not null)
            {
                offer["price"] = s.Price.ToString("0.##", CultureInfo.InvariantCulture);
                offer["priceCurrency"] = currency;
            }
            return (JsonNode)offer;
        }).ToArray();
        if (offers.Length > 0)
            node["hasOfferCatalog"] = new JsonObject { ["@type"] = "OfferCatalog", ["name"] = $"Services of {c.Name}", ["itemListElement"] = new JsonArray(offers) };

        // Ratings and reviews only from genuine published reviews in the database.
        if (c.ReviewCount > 0 && c.AverageRating is > 0 and <= 5)
            node["aggregateRating"] = new JsonObject
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = Math.Round(c.AverageRating, 1),
                ["reviewCount"] = c.ReviewCount,
                ["bestRating"] = 5,
                ["worstRating"] = 1,
            };
        var reviews = b.RecentReviews.Where(r => r.Rating is >= 1 and <= 5 && !string.IsNullOrWhiteSpace(r.Comment)).Take(5).Select(r =>
        {
            var review = new JsonObject
            {
                ["@type"] = "Review",
                ["author"] = new JsonObject { ["@type"] = "Person", ["name"] = r.CustomerName },
                ["datePublished"] = r.CreatedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["reviewBody"] = SeoText.Clamp(r.Comment, 500),
                ["reviewRating"] = new JsonObject { ["@type"] = "Rating", ["ratingValue"] = (int)r.Rating, ["bestRating"] = 5, ["worstRating"] = 1 },
            };
            if (!string.IsNullOrWhiteSpace(r.Title)) review["name"] = r.Title;
            return (JsonNode)review;
        }).ToArray();
        if (reviews.Length > 0) node["review"] = new JsonArray(reviews);
        if (b.YearEstablished is { } year and > 1800) node["foundingDate"] = year.ToString(CultureInfo.InvariantCulture);
        return node;
    }

    private static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <summary>One OpeningHoursSpecification per open day; closed days are left out (as Schema.org intends).</summary>
    public static JsonArray OpeningHours(IReadOnlyList<HoursDto> hours) => new(hours
        .Where(h => !h.IsClosed && h.Open is { Length: >= 4 } && h.Close is { Length: >= 4 } && h.DayOfWeek is >= 0 and <= 6)
        .Select(h => (JsonNode)new JsonObject
        {
            ["@type"] = "OpeningHoursSpecification",
            ["dayOfWeek"] = $"https://schema.org/{DayNames[h.DayOfWeek]}",
            ["opens"] = h.Open![..5],
            ["closes"] = h.Close![..5],
        }).ToArray());

    /// <summary>ISO 4217 currency of a country ("IN" → "INR"); null when unknown.</summary>
    public static string? Currency(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        try { return new RegionInfo(countryCode).ISOCurrencySymbol; }
        catch (ArgumentException) { return null; }
    }
}
