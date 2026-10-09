using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Owner;
using CallingBell.Application.Features.Search;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;

namespace CallingBell.Application.Features.Onboarding;

public sealed class OnboardingOptions
{
    public const string Section = "Onboarding";

    /// <summary>
    /// Copy photos from the source (Google Maps) into a new listing when the owner picks them. Off by default: Google's Maps Platform terms
    /// don't allow storing Places photos, and they are usually taken by other people. Turn on only with a licence that allows it.
    /// </summary>
    public bool AllowExternalPhotoImport { get; set; }
}

// ===================== DTOs =====================

/// <summary>A photo of the source place. Shown with its credit; <see cref="Importable"/> says whether it may be copied to the new listing.</summary>
/// <param name="Reference">The source's photo reference, sent back to import it (validated against the place on the server).</param>
public sealed record JoinImageDto(string Reference, string ImageUrl, string ThumbnailUrl, string MobileImageUrl, string DesktopImageUrl, string AltText,
    bool IsPrimary, int SortOrder, string Source, string? Attribution, string? AttributionUrl, bool Importable);

/// <summary>A Calling Bell business that may be the same as the imported place.</summary>
/// <param name="MatchedBy">SourceId (created from this very place), Phone, Website, Location or NameAndCity.</param>
/// <param name="IsStrong">Almost certainly the same business: registering again is refused.</param>
public sealed record ExistingBusinessDto(Guid Id, string Slug, string Name, string? City, string Status, string MatchedBy, bool IsStrong);

/// <summary>
/// Everything known about a place outside Calling Bell, ready for the business sign-up form. Only what the source provides is filled in;
/// the Calling Bell category, city and area are matched from the database.
/// </summary>
public sealed record JoinCallingBellBusinessDto(
    string SourceBusinessId, string Source, string SourceName, string? SourceUrl, string? GooglePlaceId,
    string BusinessName, string? Description, string? SourceCategory, IReadOnlyList<string> Tags,
    string? CategorySlug, string? SubCategorySlug, string? CategoryName, string? SubCategoryName,
    string? Address, string? AddressLine, string? Area, string? City, string? State, string? Country, string? CountryCode, string? PostalCode, string? Pincode,
    string? CitySlug, string? AreaSlug, double? Latitude, double? Longitude,
    string? Phone, string? AlternatePhone, string? WhatsApp, string? Email, string? Website, IReadOnlyList<OnboardingSocialLinkInput> SocialLinks,
    IReadOnlyList<ImportedHours> BusinessHours, IReadOnlyList<string> Services, decimal? Rating, int? ReviewCount, string? BusinessStatus,
    IReadOnlyList<JoinImageDto> Images, bool ImagesImportable, IReadOnlyList<ExistingBusinessDto> ExistingBusinesses);

// ===================== Query =====================

/// <param name="SourceId">"google:{place id}" or "osm:node/123", as on the search and Explore nearby results.</param>
/// <param name="Hint">
/// What the person was browsing when they clicked: a sub-category or category slug ("electrical") or search text ("electrician"). Used
/// for the category only when the source's own category doesn't match one (Google often just says "Services").
/// </param>
public sealed record GetJoinCallingBellBusinessQuery(string SourceId, string? Hint = null) : IRequest<JoinCallingBellBusinessDto>;

public sealed class GetJoinCallingBellBusinessValidator : AbstractValidator<GetJoinCallingBellBusinessQuery>
{
    public GetJoinCallingBellBusinessValidator()
    {
        RuleFor(x => x.SourceId).Must(s => JoinImport.ParseSource(s) is not null).WithMessage("Unknown business reference.");
        RuleFor(x => x.Hint).MaximumLength(120);
    }
}

/// <summary>
/// Reads the place from its source on the server (never from what the browser sends): Google Place Details, or OpenStreetMap plus the same
/// place on Google Maps. Cleans and validates every value, matches the Calling Bell category, city and area, parses opening hours, lists
/// the photos, and finds Calling Bell businesses that may already be this one. Nothing from the source is stored.
/// </summary>
public sealed class GetJoinCallingBellBusinessHandler(ISender sender, IUnitOfWork uow, ReverseGeocoder geocoder, BusinessDuplicateFinder duplicates,
    IOptions<OnboardingOptions> options, ILogger<GetJoinCallingBellBusinessHandler> logger)
    : IRequestHandler<GetJoinCallingBellBusinessQuery, JoinCallingBellBusinessDto>
{
    private const double CityRadiusKm = 60;

    public async Task<JoinCallingBellBusinessDto> Handle(GetJoinCallingBellBusinessQuery r, CancellationToken ct)
    {
        var source = JoinImport.ParseSource(r.SourceId)!;
        PlaceDetailsDto d;
        try
        {
            d = await sender.Send(new GetPlaceDetailsQuery(source.Provider, source.ExternalId, null, null, null), ct);
        }
        catch (Exception ex) when (ex is BadRequestException or NotFoundException or ValidationException
                                   or ExternalServiceException { StatusCode: 400 or 404 })
        {
            // The source doesn't know this place (or the reference is malformed).
            throw new NotFoundException("Business", source.ToString());
        }

        var name = JoinImport.Clean(d.Name, 150) ?? throw new NotFoundException("Business", source.ToString());
        var countryCode = JoinImport.CountryCode(d.Country) ?? Phones.CountryOf(Phones.ToE164(d.InternationalPhone ?? d.Phone));
        double? lat = d.Latitude is { } la && Math.Abs(la) <= 90 ? la : null, lon = d.Longitude is { } lo && Math.Abs(lo) <= 180 ? lo : null;

        // City and area as listed on Calling Bell, from the coordinates.
        ReverseGeocodeResult? place = null;
        if (lat is { } plat && lon is { } plon && countryCode is not null)
        {
            try { place = await geocoder.ReverseAsync(plat, plon, countryCode, CityRadiusKm, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Join Calling Bell: reverse geocoding failed"); }
        }

        // Category: first by name against the category list ("Electrician" → Electricians), then the search parser (keywords, AI).
        // Then what the person was browsing (a slug, or search text), then the business name ("Manikanta Electricals" → Electricians).
        var hint = JoinImport.Clean(r.Hint, 120);
        // In order of trust: the source's main category, what the person was browsing, the source's other tags, the business name.
        string? categorySlug = null, subSlug = await MatchSubCategoryAsync([d.Category], ct)
            ?? await HintSubCategoryAsync(hint, ct)
            ?? await MatchSubCategoryAsync(d.Tags.Where(t => !string.Equals(t, d.Category, StringComparison.OrdinalIgnoreCase)), ct)
            ?? await MatchSubCategoryAsync([name], ct);
        if (subSlug is null && hint is not null && await uow.Repository<Category>().QueryNoTracking().AnyAsync(c => c.Slug == hint && c.IsActive, ct))
            categorySlug = hint;
        if (subSlug is null && categorySlug is null)
        try
        {
            var parsed = await sender.Send(new ParseSearchQuery(JoinImport.Clean($"{d.Category} {string.Join(' ', d.Tags.Take(3))}", 200) ?? name, null), ct);
            categorySlug = parsed.CategorySlug;
            subSlug = parsed.SubCategorySlug;
            if (subSlug is null && categorySlug is null)
            {
                parsed = await sender.Send(new ParseSearchQuery(name, null), ct);
                (categorySlug, subSlug) = (parsed.CategorySlug, parsed.SubCategorySlug);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Join Calling Bell: category match failed"); }
        var category = await uow.Repository<SubCategory>().QueryNoTracking()
            .Where(s => s.Slug == subSlug && s.IsActive).Select(s => new { s.Slug, s.Name, CategorySlug = s.Category.Slug, CategoryName = s.Category.Name })
            .FirstOrDefaultAsync(ct);
        var categoryName = category?.CategoryName ?? (categorySlug is null ? null
            : await uow.Repository<Category>().QueryNoTracking().Where(c => c.Slug == categorySlug && c.IsActive).Select(c => c.Name).FirstOrDefaultAsync(ct));

        // Phones: the main number, another one, and a WhatsApp-capable (Indian mobile) number.
        var numbers = new[] { d.Phone, d.Mobile, d.InternationalPhone }
            .Select(p => OnboardingRules.IsIndianPhone(p) ? Phones.Normalize(p!) : Phones.ToE164(p))
            .Where(p => p is not null).Distinct().ToList();
        var phone = numbers.FirstOrDefault();
        var alternate = numbers.Skip(1).FirstOrDefault(p => !SameNumber(p, phone));
        var whatsApp = numbers.FirstOrDefault(p => p is not null && IsIndianMobile(p));

        var website = JoinImport.CleanWebsite(d.Website);
        var social = await SocialLinksAsync(d.SocialLinks, ct);
        var hours = JoinImport.ParseHours(d.OpeningHours);
        var pincode = JoinImport.Pincode(d.PostalCode, d.Address);
        var cityName = place?.City.Name ?? JoinImport.Clean(d.City, 120);
        var images = Images(d, name);

        var existing = await duplicates.FindAsync(new DuplicateProbe(source, name, [phone, alternate, whatsApp], website, place?.City.Id, lat, lon), ct);

        return new JoinCallingBellBusinessDto(
            source.ToString(), source.Provider, d.Source == "google" ? "Google Maps" : "OpenStreetMap", d.MapsUrl ?? d.SourceUrl, d.GooglePlaceId ?? (source.Provider == "google" ? source.ExternalId : null),
            name, JoinImport.Clean(d.Description, 2000), JoinImport.Clean(d.Category, 120), d.Tags.Select(t => JoinImport.Clean(t, 60)).OfType<string>().Distinct().Take(20).ToList(),
            category?.CategorySlug ?? categorySlug, category?.Slug, categoryName, category?.Name,
            JoinImport.Clean(d.Address, 300), JoinImport.StreetAddress(d.Address, cityName, d.City, d.State, d.Country, place?.Region?.Name, d.PostalCode, pincode),
            place?.Area?.Name ?? JoinImport.Clean(place?.Locality, 120), cityName, JoinImport.Clean(d.State, 120) ?? place?.Region?.Name,
            JoinImport.Clean(d.Country, 80), countryCode, JoinImport.Clean(d.PostalCode, 20), pincode,
            place?.City.Slug, place?.Area?.Slug, lat, lon,
            phone, alternate, whatsApp, JoinImport.CleanEmail(d.Email), website, social,
            hours, [], d.Rating, d.RatingCount, JoinImport.Clean(d.BusinessStatus, 40),
            images, options.Value.AllowExternalPhotoImport && images.Count > 0, existing);
    }

    /// <summary>Same number written differently ("+91 80 1234 5678" and "080 1234 5678"): compares the last 10 digits.</summary>
    private static bool SameNumber(string? a, string? b)
    {
        static string Last10(string s) { var d = new string(s.Where(char.IsDigit).ToArray()); return d.Length > 10 ? d[^10..] : d; }
        return a is not null && b is not null && Last10(a) == Last10(b);
    }

    /// <summary>
    /// The sub-category whose name matches what the source calls the place, ignoring plurals ("Electrician" and "Electricians",
    /// "Dentist" and "Dentists"). Labels are tried in order and the first that matches decides; within a label, the sub-category covering
    /// most of the label's words wins, then the one with the fewest other words, then the one with more listings.
    /// </summary>
    private async Task<string?> MatchSubCategoryAsync(IEnumerable<string?> labels, CancellationToken ct)
    {
        static HashSet<string> Words(string? text) => JoinImport.NameTokens(text).Select(Singular).ToHashSet();
        static string Singular(string w) => w.EndsWith("ies") && w.Length > 4 ? w[..^3] + "y" : w.EndsWith('s') && !w.EndsWith("ss") && w.Length > 3 ? w[..^1] : w;

        var wanted = labels.Where(l => !string.IsNullOrWhiteSpace(l)).Take(6).Select(Words).Where(w => w.Count > 0).ToList();
        if (wanted.Count == 0) return null;
        var subs = (await uow.Repository<SubCategory>().QueryNoTracking().Where(s => s.IsActive && s.Category.IsActive)
                .Select(s => new { s.Slug, s.Name, Listed = s.Businesses.Count(b => b.Status == BusinessStatuses.Active) }).ToListAsync(ct))
            .Select(s => (s.Slug, s.Listed, Words: Words(s.Name).Union(Words(s.Slug.Replace('-', ' '))).ToHashSet()))
            .ToList();
        foreach (var label in wanted)
        {
            var best = subs
                .Select(s => (s.Slug, s.Listed, Covered: label.Count(s.Words.Contains) / (double)label.Count, Extra: s.Words.Count(w => !label.Contains(w))))
                .Where(s => s.Covered >= 0.5)
                .OrderByDescending(s => s.Covered).ThenBy(s => s.Extra).ThenByDescending(s => s.Listed)
                .Select(s => s.Slug).FirstOrDefault();
            if (best is not null) return best;
        }
        return null;
    }

    /// <summary>The hint as a sub-category: its slug, or text naming one ("electrician").</summary>
    private async Task<string?> HintSubCategoryAsync(string? hint, CancellationToken ct)
    {
        if (hint is null) return null;
        if (await uow.Repository<SubCategory>().QueryNoTracking().AnyAsync(s => s.Slug == hint && s.IsActive, ct)) return hint;
        // A whole industry ("home-services") says nothing about which of its services: it only sets the industry (below).
        if (await uow.Repository<Category>().QueryNoTracking().AnyAsync(c => c.Slug == hint, ct)) return null;
        return await MatchSubCategoryAsync([hint.Replace('-', ' ')], ct);
    }

    private static bool IsIndianMobile(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        return digits.Length == 10 && digits[0] is >= '6' and <= '9';
    }

    /// <summary>Social profiles on a platform Calling Bell supports (matched by the URL's site, as the sign-up form checks).</summary>
    private async Task<IReadOnlyList<OnboardingSocialLinkInput>> SocialLinksAsync(IReadOnlyList<Businesses.SocialLinkDto> links, CancellationToken ct)
    {
        if (links.Count == 0) return [];
        var platforms = await uow.Repository<LookupValue>().QueryNoTracking()
            .Where(l => l.LookupType == LookupTypes.SocialPlatform && l.IsActive).Select(l => new { l.Code, l.Description }).ToListAsync(ct);
        return links
            .Select(l => (Url: JoinImport.Clean(l.Url, 300), Link: l))
            .Where(x => x.Url is not null && OnboardingRules.IsHttpUrl(x.Url))
            .Select(x => (x.Url, Platform: platforms.FirstOrDefault(p => SocialLinks.MatchesPlatform(x.Url!, p.Description))?.Code))
            .Where(x => x.Platform is not null)
            .GroupBy(x => x.Platform).Select(g => new OnboardingSocialLinkInput(g.Key!, g.First().Url!))
            .ToList();
    }

    /// <summary>The place's photos in several widths (all served through /api/places/photo, so the API key stays on the server).</summary>
    private IReadOnlyList<JoinImageDto> Images(PlaceDetailsDto d, string name)
    {
        var importable = options.Value.AllowExternalPhotoImport;
        var list = new List<JoinImageDto>();
        foreach (var (photo, i) in d.Photos.Select((p, i) => (p, i)))
        {
            var reference = PhotoReference(photo.Url);
            if (reference is null) continue;
            string Url(int width) => $"/api/places/photo?name={Uri.EscapeDataString(reference)}&maxWidth={width}";
            var author = photo.Attributions.FirstOrDefault();
            list.Add(new JoinImageDto(reference, Url(1200), Url(400), Url(800), Url(1600), $"Photo of {name}", i == 0, i + 1,
                d.Source == "google" ? "Google Maps" : "OpenStreetMap", author?.DisplayName, author?.Uri, importable));
        }
        return list;
    }

    /// <summary>The photo reference inside "/api/places/photo?name=…&amp;maxWidth=…".</summary>
    internal static string? PhotoReference(string url)
    {
        var q = url.IndexOf('?');
        if (q < 0) return null;
        foreach (var pair in url[(q + 1)..].Split('&'))
            if (pair.StartsWith("name=", StringComparison.Ordinal)) return Uri.UnescapeDataString(pair[5..]);
        return null;
    }
}

// ===================== Duplicates =====================

/// <summary>What is known about a business that is about to be created.</summary>
public sealed record DuplicateProbe(SourceRef? Source, string Name, IReadOnlyList<string?> Phones, string? Website, Guid? CityId, double? Latitude, double? Longitude);

/// <summary>
/// Calling Bell businesses that may be the same as a new one: created from the same source place; the same phone or website with a similar
/// name; a similar name within 150 m; or a very similar name in the same city. Strong matches (same place, or same phone/website/location
/// and a similar name) block registering again; others are shown to the person to decide.
/// </summary>
public sealed class BusinessDuplicateFinder(IUnitOfWork uow)
{
    private const double SimilarName = 0.6;
    private const double NearMetres = 150;

    public async Task<IReadOnlyList<ExistingBusinessDto>> FindAsync(DuplicateProbe p, CancellationToken ct)
    {
        var found = new Dictionary<Guid, ExistingBusinessDto>();
        var businesses = uow.Repository<Business>().QueryNoTracking();
        void Add(Candidate b, string reason, bool strong)
        {
            if (found.TryGetValue(b.Id, out var existing) && (existing.IsStrong || !strong)) return;
            found[b.Id] = new ExistingBusinessDto(b.Id, b.Slug, b.Name, b.City, b.Status, reason, strong);
        }

        if (p.Source is { } s)
            foreach (var b in await Select(businesses.Where(b => b.SourceProvider == s.Provider && b.SourceExternalId == s.ExternalId), ct))
                Add(b, "SourceId", true);

        var phones = p.Phones.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => OnboardingRules.IsIndianPhone(x) ? Phones.Normalize(x!) : x!.Trim()).Distinct().ToList();
        if (phones.Count > 0)
            foreach (var b in await Select(businesses.Where(b => phones.Contains(b.PhoneNumber!) || phones.Contains(b.WhatsAppNumber!)), ct))
                Add(b, "Phone", JoinImport.NameSimilarity(b.Name, p.Name) >= SimilarName);

        if (JoinImport.Host(p.Website) is { } host)
            foreach (var b in await Select(businesses.Where(b => b.Website != null && b.Website.Contains(host)), ct))
                if (JoinImport.Host(b.Website) == host) Add(b, "Website", JoinImport.NameSimilarity(b.Name, p.Name) >= SimilarName);

        if (p is { Latitude: { } lat, Longitude: { } lon })
        {
            // ~0.002° is ~200 m; the exact distance is checked below.
            decimal minLat = (decimal)lat - 0.002m, maxLat = (decimal)lat + 0.002m, minLon = (decimal)lon - 0.002m, maxLon = (decimal)lon + 0.002m;
            foreach (var b in await Select(businesses.Where(b => b.Latitude >= minLat && b.Latitude <= maxLat && b.Longitude >= minLon && b.Longitude <= maxLon), ct))
                if (b is { Latitude: { } blat, Longitude: { } blon } && JoinImport.DistanceMetres(lat, lon, (double)blat, (double)blon) <= NearMetres
                    && JoinImport.NameSimilarity(b.Name, p.Name) >= SimilarName)
                    Add(b, "Location", true);
        }

        if (p.CityId is { } cityId && JoinImport.NameTokens(p.Name).OrderByDescending(t => t.Length).FirstOrDefault() is { Length: >= 4 } word)
            foreach (var b in await Select(businesses.Where(b => b.CityId == cityId && b.Name.Contains(word)), ct))
                if (JoinImport.NameSimilarity(b.Name, p.Name) >= 0.9) Add(b, "NameAndCity", false);

        return found.Values.OrderByDescending(x => x.IsStrong).ThenBy(x => x.Name).Take(5).ToList();
    }

    private static Task<List<Candidate>> Select(IQueryable<Business> q, CancellationToken ct) =>
        q.Select(b => new Candidate(b.Id, b.Slug, b.Name, b.City, b.Status, b.Latitude, b.Longitude, b.Website)).Take(20).ToListAsync(ct);

    private sealed record Candidate(Guid Id, string Slug, string Name, string? City, string Status, decimal? Latitude, decimal? Longitude, string? Website);
}

// ===================== Photo import (off unless licensed) =====================

/// <summary>Downloads a source photo on the server (the API key never reaches the browser). Implemented in Infrastructure.</summary>
public interface IExternalPhotoFetcher
{
    /// <returns>The image bytes, or null when the photo is unavailable or not an image within the size limit.</returns>
    Task<byte[]?> FetchAsync(string provider, string reference, CancellationToken ct);
}

/// <param name="References">Photo references from <see cref="JoinCallingBellBusinessDto.Images"/>, in the order wanted (the first may become the main photo).</param>
public sealed record ImportSourcePhotosCommand(Guid BusinessId, IReadOnlyList<string> References) : IRequest<int>;

public sealed class ImportSourcePhotosValidator : AbstractValidator<ImportSourcePhotosCommand>
{
    public ImportSourcePhotosValidator()
    {
        RuleFor(x => x.References).NotEmpty().Must(r => r.Count <= 10).WithMessage("Choose up to 10 photos.");
        RuleForEach(x => x.References).NotEmpty().MaximumLength(500);
    }
}

/// <summary>
/// Copies chosen photos of the place a business was created from into its gallery, through the normal media upload (same checks, storage
/// and thumbnails). Only for the owner of that business, only photos of that same place, and only when <see cref="OnboardingOptions.AllowExternalPhotoImport"/> is on.
/// </summary>
public sealed class ImportSourcePhotosHandler(IUnitOfWork uow, ICurrentUser user, ISender sender, IExternalPhotoFetcher fetcher,
    IOptions<OnboardingOptions> options) : IRequestHandler<ImportSourcePhotosCommand, int>
{
    public async Task<int> Handle(ImportSourcePhotosCommand r, CancellationToken ct)
    {
        if (!options.Value.AllowExternalPhotoImport) throw new ForbiddenAccessException();
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var b = await uow.Repository<Business>().QueryNoTracking().Where(x => x.Id == r.BusinessId)
            .Select(x => new { x.OwnerUserId, x.SourceProvider, x.SourceExternalId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Business", r.BusinessId);
        if (b.OwnerUserId != userId) throw new ForbiddenAccessException();
        if (b.SourceProvider is null || b.SourceExternalId is null) throw new BadRequestException("This business wasn't created from another listing.");

        // The place's own photos, read again from the source: references for any other place are ignored.
        var details = await sender.Send(new GetPlaceDetailsQuery(b.SourceProvider, b.SourceExternalId, null, null, null), ct);
        var allowed = details.Photos.Select(p => GetJoinCallingBellBusinessHandler.PhotoReference(p.Url)).OfType<string>().ToHashSet();
        var imported = 0;
        foreach (var reference in r.References.Distinct().Where(allowed.Contains))
        {
            var bytes = await fetcher.FetchAsync(b.SourceProvider, reference, ct);
            if (bytes is null) continue;
            var credit = details.Photos.First(p => GetJoinCallingBellBusinessHandler.PhotoReference(p.Url) == reference).Attributions.FirstOrDefault()?.DisplayName;
            await sender.Send(new UploadBusinessMediaCommand(r.BusinessId, "photo", "imported.jpg", bytes, null,
                credit is null ? null : JoinImport.Clean($"Photo: {credit}", 150), null), ct);
            imported++;
        }
        return imported;
    }
}

// ===================== Claim an existing listing =====================

/// <param name="SourceId">The Google Maps / OpenStreetMap place the person came from, if any ("google:…").</param>
public sealed record CreateBusinessClaimCommand(Guid BusinessId, string Name, string Phone, string? Email, string? Message, string? SourceId)
    : IRequest<BusinessClaimResultDto>;

public sealed record BusinessClaimResultDto(string RequestNumber, string BusinessName);

public sealed class CreateBusinessClaimValidator : AbstractValidator<CreateBusinessClaimCommand>
{
    public CreateBusinessClaimValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter your name.").MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().Must(p => Phones.ToE164(p) is not null).WithMessage("Enter a valid mobile number.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Message).MaximumLength(1000);
        RuleFor(x => x.SourceId).Must(s => JoinImport.ParseSource(s) is not null).When(x => !string.IsNullOrWhiteSpace(x.SourceId))
            .WithMessage("Unknown source business.");
    }
}

/// <summary>
/// Records a request to take over an existing listing and tells the administrators, who verify the person (e.g. by calling the listed
/// number) before transferring it. The same number asking again for the same listing gets its earlier request back.
/// </summary>
public sealed class CreateBusinessClaimHandler(IUnitOfWork uow, ICurrentUser user, IRealtimeNotifier notifier)
    : IRequestHandler<CreateBusinessClaimCommand, BusinessClaimResultDto>
{
    public async Task<BusinessClaimResultDto> Handle(CreateBusinessClaimCommand r, CancellationToken ct)
    {
        var business = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == r.BusinessId)
            .Select(b => new { b.Id, b.Name, b.Slug }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business", r.BusinessId);
        var phone = OnboardingRules.IsIndianPhone(r.Phone) ? Phones.Normalize(r.Phone) : Phones.ToE164(r.Phone)!;

        var pending = await uow.Repository<BusinessClaimRequest>().QueryNoTracking()
            .Where(c => c.BusinessId == business.Id && c.ClaimantPhone == phone && c.Status == "Pending").Select(c => c.RequestNumber).FirstOrDefaultAsync(ct);
        if (pending is not null) return new BusinessClaimResultDto(pending, business.Name);

        var source = JoinImport.ParseSource(r.SourceId);
        var claim = new BusinessClaimRequest
        {
            RequestNumber = Engagement.References.New("CLM"), BusinessId = business.Id, UserId = user.UserId,
            ClaimantName = JoinImport.Clean(r.Name, 120)!, ClaimantPhone = phone, ClaimantEmail = JoinImport.CleanEmail(r.Email),
            Message = JoinImport.Clean(r.Message, 1000), SourceProvider = source?.Provider, SourceExternalId = source?.ExternalId,
            IpAddress = user.IpAddress,
        };
        uow.Repository<BusinessClaimRequest>().Add(claim);
        await uow.SaveChangesAsync(ct);

        var admins = await uow.Repository<ApplicationUser>().QueryNoTracking().Where(u => u.UserType == Roles.Administrator).Select(u => u.Id).ToListAsync(ct);
        foreach (var admin in admins)
            await Notifications.NotificationPublisher.PublishAsync(uow, notifier, admin, "Listing claim request",
                $"{claim.ClaimantName} ({Phones.Mask(phone)}) says {business.Name} is theirs. Request {claim.RequestNumber}.", "Claim",
                $"/admin/businesses?q={Uri.EscapeDataString(business.Name)}", ct);
        return new BusinessClaimResultDto(claim.RequestNumber, business.Name);
    }
}
