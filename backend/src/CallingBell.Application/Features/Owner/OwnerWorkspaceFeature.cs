using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Businesses;
using CallingBell.Application.Features.Onboarding;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;
using Microsoft.EntityFrameworkCore;
using MediaEntity = CallingBell.Domain.Entities.Media;

namespace CallingBell.Application.Features.Owner;

// ===================== Media rules =====================

/// <summary>Upload limits and content sniffing. The stored content type always comes from the file's bytes, never the client.</summary>
public static class MediaRules
{
    public const int MaxImageMb = 10;
    public const int MaxThumbnailMb = 2;
    public const int MaxVideoMb = 50;
    public const int MaxVideos = 5;
    public const long MaxImageBytes = MaxImageMb * 1024L * 1024;
    public const long MaxThumbnailBytes = MaxThumbnailMb * 1024L * 1024;
    public const long MaxVideoBytes = MaxVideoMb * 1024L * 1024;

    public static readonly string[] Kinds = ["logo", "cover", "photo", "video"];

    public static (string ContentType, string Extension)? DetectImage(byte[] d)
    {
        if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return ("image/png", ".png");
        if (d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return ("image/webp", ".webp");
        return null;
    }

    public static (string ContentType, string Extension)? DetectVideo(byte[] d)
    {
        if (d.Length > 4 && d[0] == 0x1A && d[1] == 0x45 && d[2] == 0xDF && d[3] == 0xA3) return ("video/webm", ".webm");
        if (d.Length > 12 && d[4] == 'f' && d[5] == 't' && d[6] == 'y' && d[7] == 'p')
            return d[8] == 'q' && d[9] == 't' ? ("video/quicktime", ".mov") : ("video/mp4", ".mp4");
        return null;
    }

    public static string Url(Guid mediaId) => $"/api/media/{mediaId.ToString().ToLowerInvariant()}";
    public static string ThumbnailUrl(Guid mediaId) => Url(mediaId) + "/thumbnail";

    /// <summary>Extracts the media id from a "/api/media/{id}[/thumbnail]" URL.</summary>
    public static Guid? IdFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("/api/media/")) return null;
        var segment = url["/api/media/".Length..].Split('/')[0];
        return Guid.TryParse(segment, out var id) ? id : null;
    }
}

internal static class MediaStore
{
    /// <summary>Soft-deletes a stored file (Media is not an AuditableEntity, so this is done explicitly).</summary>
    public static async Task SoftDeleteAsync(IUnitOfWork uow, string? url, string? userId, CancellationToken ct)
    {
        if (MediaRules.IdFromUrl(url) is not { } id) return;
        var row = await uow.Repository<MediaEntity>().Query().FirstOrDefaultAsync(m => m.MediaId == id, ct);
        if (row is null) return;
        row.IsDeleted = true;
        row.IsActive = false;
        row.ModifiedOn = DateTimeOffset.UtcNow;
        row.ModifiedBy = userId;
    }
}

internal static class OwnerPlans
{
    /// <summary>The plan currently in effect (active or trial), falling back to the Free plan.</summary>
    public static async Task<SubscriptionPlan> EffectivePlanAsync(IUnitOfWork uow, Guid businessId, CancellationToken ct)
    {
        var today = IndianTime.Today;
        return await uow.Repository<BusinessSubscription>().QueryNoTracking()
                   .Where(s => s.BusinessId == businessId && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial)
                               && s.StartDate <= today && s.EndDate >= today)
                   .OrderByDescending(s => s.StartDate).Select(s => s.Plan).FirstOrDefaultAsync(ct)
               ?? await uow.Repository<SubscriptionPlan>().QueryNoTracking().FirstAsync(p => p.Code == PlanCodes.Free, ct);
    }
}

// ===================== Media: list =====================

public sealed record OwnerMediaItemDto(Guid Id, string Kind, string Url, string? ThumbnailUrl, string? Title, string? ContentType, long? FileSize,
    int? DurationSeconds, bool IsPrimary, DateTimeOffset CreatedOn);

public sealed record MediaLimitsDto(string PlanName, int MaxPhotos, int MaxVideos, int MaxImageMb, int MaxVideoMb);

public sealed record OwnerMediaDto(string? LogoUrl, string? CoverImageUrl, IReadOnlyList<OwnerMediaItemDto> Photos, IReadOnlyList<OwnerMediaItemDto> Videos,
    MediaLimitsDto Limits);

public sealed record GetOwnerMediaQuery(Guid BusinessId) : IRequest<OwnerMediaDto>;

public sealed class GetOwnerMediaHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerMediaQuery, OwnerMediaDto>
{
    public async Task<OwnerMediaDto> Handle(GetOwnerMediaQuery r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var plan = await OwnerPlans.EffectivePlanAsync(uow, b.Id, ct);
        var photos = await uow.Repository<BusinessImage>().QueryNoTracking().Where(i => i.BusinessId == b.Id).OrderBy(i => i.SortOrder)
            .Select(i => new OwnerMediaItemDto(i.Id, "photo", i.ImageUrl, i.ThumbnailUrl, i.Caption ?? i.AltText, null, null, null, i.IsPrimary, i.CreatedOn))
            .ToListAsync(ct);
        var videos = await uow.Repository<BusinessVideo>().QueryNoTracking().Where(v => v.BusinessId == b.Id).OrderBy(v => v.SortOrder)
            .Select(v => new OwnerMediaItemDto(v.Id, "video", v.VideoUrl, v.PosterUrl, v.Title, v.ContentType, v.FileSize, v.DurationSeconds, false, v.CreatedOn))
            .ToListAsync(ct);
        return new OwnerMediaDto(b.LogoUrl, b.CoverImageUrl, photos, videos,
            new MediaLimitsDto(plan.Name, plan.MaxImages, MediaRules.MaxVideos, MediaRules.MaxImageMb, MediaRules.MaxVideoMb));
    }
}

// ===================== Media: upload =====================

/// <summary>
/// Uploads one file. <paramref name="Thumbnail"/> is an optional smaller rendition for photos/logos/covers, or the poster frame for videos.
/// </summary>
public sealed record UploadBusinessMediaCommand(Guid BusinessId, string Kind, string FileName, byte[] Data, byte[]? Thumbnail, string? Title,
    int? DurationSeconds) : IRequest<OwnerMediaItemDto>;

public sealed class UploadBusinessMediaValidator : AbstractValidator<UploadBusinessMediaCommand>
{
    public UploadBusinessMediaValidator()
    {
        RuleFor(x => x.Kind).Must(k => MediaRules.Kinds.Contains(k)).WithMessage("Media type must be logo, cover, photo or video.");
        RuleFor(x => x.Data).NotEmpty().WithMessage("Choose a file to upload.");
        RuleFor(x => x.Data.LongLength).LessThanOrEqualTo(MediaRules.MaxImageBytes).When(x => x.Kind != "video" && x.Data is not null)
            .OverridePropertyName("file").WithMessage($"Images must be {MediaRules.MaxImageMb} MB or smaller.");
        RuleFor(x => x.Data.LongLength).LessThanOrEqualTo(MediaRules.MaxVideoBytes).When(x => x.Kind == "video" && x.Data is not null)
            .OverridePropertyName("file").WithMessage($"Videos must be {MediaRules.MaxVideoMb} MB or smaller.");
        RuleFor(x => x.Thumbnail!.LongLength).LessThanOrEqualTo(MediaRules.MaxThumbnailBytes).When(x => x.Thumbnail is not null)
            .OverridePropertyName("thumbnail").WithMessage($"Preview images must be {MediaRules.MaxThumbnailMb} MB or smaller.");
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.DurationSeconds).InclusiveBetween(1, 3600).When(x => x.DurationSeconds.HasValue);
    }
}

public sealed class UploadBusinessMediaHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<UploadBusinessMediaCommand, OwnerMediaItemDto>
{
    public async Task<OwnerMediaItemDto> Handle(UploadBusinessMediaCommand r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct, track: true);
        var isVideo = r.Kind == "video";
        var type = isVideo ? MediaRules.DetectVideo(r.Data) : MediaRules.DetectImage(r.Data);
        if (type is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = [isVideo ? "Upload an MP4, WebM or MOV video." : "Upload a JPG, PNG or WebP image."]
            });
        byte[]? thumbnail = null;
        if (r.Thumbnail is { Length: > 0 })
        {
            if (MediaRules.DetectImage(r.Thumbnail) is null)
                throw new ValidationException(new Dictionary<string, string[]> { ["thumbnail"] = ["The preview image must be a JPG, PNG or WebP image."] });
            thumbnail = r.Thumbnail;
        }

        var alt = $"{b.Name} {(r.Kind == "photo" ? "photo" : r.Kind)}";
        var now = DateTimeOffset.UtcNow;
        switch (r.Kind)
        {
            case "logo":
            case "cover":
            {
                var entityType = r.Kind == "logo" ? MediaEntityTypes.BusinessLogo : MediaEntityTypes.BusinessCover;
                var media = NewMedia(entityType, b.Id, r.FileName, type.Value, r.Data, thumbnail, alt);
                await MediaStore.SoftDeleteAsync(uow, r.Kind == "logo" ? b.LogoUrl : b.CoverImageUrl, user.UserId, ct);
                if (r.Kind == "logo") b.LogoUrl = MediaRules.Url(media.MediaId); else b.CoverImageUrl = MediaRules.Url(media.MediaId);
                await uow.SaveChangesAsync(ct);
                return new OwnerMediaItemDto(media.MediaId, r.Kind, MediaRules.Url(media.MediaId), thumbnail is null ? null : MediaRules.ThumbnailUrl(media.MediaId),
                    null, type.Value.ContentType, r.Data.LongLength, null, true, now);
            }
            case "photo":
            {
                var plan = await OwnerPlans.EffectivePlanAsync(uow, b.Id, ct);
                var images = uow.Repository<BusinessImage>();
                var count = await images.QueryNoTracking().CountAsync(i => i.BusinessId == b.Id, ct);
                if (count >= plan.MaxImages)
                    throw new BadRequestException($"Your {plan.Name} plan allows up to {plan.MaxImages} photos. Remove a photo or upgrade your plan to add more.");
                // SortOrder is unique per business, including soft-deleted rows.
                var maxSort = await images.QueryNoTracking().IgnoreQueryFilters().Where(i => i.BusinessId == b.Id).MaxAsync(i => (int?)i.SortOrder, ct) ?? 0;
                var media = NewMedia(MediaEntityTypes.BusinessGallery, b.Id, r.FileName, type.Value, r.Data, thumbnail, alt);
                var image = new BusinessImage
                {
                    BusinessId = b.Id, ImageUrl = MediaRules.Url(media.MediaId), DesktopImageUrl = MediaRules.Url(media.MediaId),
                    ThumbnailUrl = MediaRules.ThumbnailUrl(media.MediaId), MobileImageUrl = MediaRules.ThumbnailUrl(media.MediaId),
                    AltText = alt, Caption = string.IsNullOrWhiteSpace(r.Title) ? null : r.Title.Trim(), IsPrimary = count == 0, SortOrder = maxSort + 1
                };
                images.Add(image);
                await uow.SaveChangesAsync(ct);
                return new OwnerMediaItemDto(image.Id, "photo", image.ImageUrl, image.ThumbnailUrl, image.Caption, type.Value.ContentType, r.Data.LongLength,
                    null, image.IsPrimary, now);
            }
            default:
            {
                var videos = uow.Repository<BusinessVideo>();
                var count = await videos.QueryNoTracking().CountAsync(v => v.BusinessId == b.Id, ct);
                if (count >= MediaRules.MaxVideos)
                    throw new BadRequestException($"You can upload up to {MediaRules.MaxVideos} videos. Remove a video to add another.");
                var maxSort = await videos.QueryNoTracking().Where(v => v.BusinessId == b.Id).MaxAsync(v => (int?)v.SortOrder, ct) ?? 0;
                var video = new BusinessVideo
                {
                    BusinessId = b.Id,
                    Title = string.IsNullOrWhiteSpace(r.Title) ? Path.GetFileNameWithoutExtension(r.FileName).Trim() is { Length: > 0 } n ? Truncate(n, 150) : $"{b.Name} video" : r.Title.Trim(),
                    ContentType = type.Value.ContentType, FileSize = r.Data.LongLength, DurationSeconds = r.DurationSeconds, SortOrder = maxSort + 1
                };
                var media = NewMedia(MediaEntityTypes.BusinessVideo, video.Id, r.FileName, type.Value, r.Data, null, video.Title);
                video.VideoUrl = MediaRules.Url(media.MediaId);
                if (thumbnail is not null)
                {
                    var poster = NewMedia(MediaEntityTypes.BusinessVideoPoster, video.Id, "poster.jpg", MediaRules.DetectImage(thumbnail)!.Value, thumbnail, null, video.Title);
                    video.PosterUrl = MediaRules.Url(poster.MediaId);
                }
                videos.Add(video);
                await uow.SaveChangesAsync(ct);
                return new OwnerMediaItemDto(video.Id, "video", video.VideoUrl, video.PosterUrl, video.Title, video.ContentType, video.FileSize,
                    video.DurationSeconds, false, now);
            }
        }
    }

    private MediaEntity NewMedia(string entityType, Guid entityId, string fileName, (string ContentType, string Extension) type, byte[] data, byte[]? thumbnail, string? alt)
    {
        var safeName = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName ?? "file"));
        safeName = new string(safeName.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        var media = new MediaEntity
        {
            MediaId = Guid.NewGuid(), EntityType = entityType, EntityId = entityId,
            FileName = $"{Truncate(string.IsNullOrEmpty(safeName) ? "upload" : safeName, 80)}-{Guid.NewGuid().ToString("N")[..6]}{type.Extension}",
            ContentType = type.ContentType, FileExtension = type.Extension, FileSize = data.LongLength, FileData = data, ThumbnailData = thumbnail,
            AltText = alt is null ? null : Truncate(alt, 300), IsPrimary = true, IsActive = true,
            CreatedOn = DateTimeOffset.UtcNow, CreatedBy = user.UserId
        };
        uow.Repository<MediaEntity>().Add(media);
        return media;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

// ===================== Media: delete & set primary =====================

public sealed record DeleteBusinessMediaCommand(Guid BusinessId, string Kind, Guid? ItemId) : IRequest;

public sealed class DeleteBusinessMediaHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<DeleteBusinessMediaCommand>
{
    public async Task Handle(DeleteBusinessMediaCommand r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct, track: true);
        Task Remove(string? url) => MediaStore.SoftDeleteAsync(uow, url, user.UserId, ct);

        switch (r.Kind)
        {
            case "logo": await Remove(b.LogoUrl); b.LogoUrl = null; break;
            case "cover": await Remove(b.CoverImageUrl); b.CoverImageUrl = null; break;
            case "photo":
            {
                var images = uow.Repository<BusinessImage>();
                var image = await images.Query().FirstOrDefaultAsync(i => i.Id == r.ItemId && i.BusinessId == b.Id, ct) ?? throw new NotFoundException("Photo", r.ItemId!);
                await Remove(image.ImageUrl);
                images.Remove(image);
                if (image.IsPrimary)
                {
                    var next = await images.Query().Where(i => i.BusinessId == b.Id && i.Id != image.Id).OrderBy(i => i.SortOrder).FirstOrDefaultAsync(ct);
                    if (next is not null) next.IsPrimary = true;
                }
                break;
            }
            case "video":
            {
                var videos = uow.Repository<BusinessVideo>();
                var video = await videos.Query().FirstOrDefaultAsync(v => v.Id == r.ItemId && v.BusinessId == b.Id, ct) ?? throw new NotFoundException("Video", r.ItemId!);
                await Remove(video.VideoUrl);
                await Remove(video.PosterUrl);
                videos.Remove(video);
                break;
            }
            default: throw new BadRequestException("Media type must be logo, cover, photo or video.");
        }
        await uow.SaveChangesAsync(ct);
    }
}

public sealed record SetPrimaryPhotoCommand(Guid BusinessId, Guid PhotoId) : IRequest;

public sealed class SetPrimaryPhotoHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<SetPrimaryPhotoCommand>
{
    public async Task Handle(SetPrimaryPhotoCommand r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var images = await uow.Repository<BusinessImage>().Query().Where(i => i.BusinessId == b.Id).ToListAsync(ct);
        if (images.All(i => i.Id != r.PhotoId)) throw new NotFoundException("Photo", r.PhotoId);
        images.ForEach(i => i.IsPrimary = i.Id == r.PhotoId);
        await uow.SaveChangesAsync(ct);
    }
}

// ===================== Social links =====================

public sealed record GetSocialLinksQuery(Guid BusinessId) : IRequest<IReadOnlyList<SocialLinkDto>>;

public sealed class GetSocialLinksHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetSocialLinksQuery, IReadOnlyList<SocialLinkDto>>
{
    public async Task<IReadOnlyList<SocialLinkDto>> Handle(GetSocialLinksQuery r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        return await uow.Repository<BusinessSocialLink>().QueryNoTracking().Where(l => l.BusinessId == b.Id)
            .OrderBy(l => l.Platform).Select(l => new SocialLinkDto(l.Platform, l.Url)).ToListAsync(ct);
    }
}

public sealed record UpdateSocialLinksCommand(Guid BusinessId, IReadOnlyList<OnboardingSocialLinkInput> Links) : IRequest<IReadOnlyList<SocialLinkDto>>;

public sealed class UpdateSocialLinksValidator : AbstractValidator<UpdateSocialLinksCommand>
{
    public UpdateSocialLinksValidator()
    {
        RuleFor(x => x.Links).NotNull();
        RuleForEach(x => x.Links).ChildRules(l =>
        {
            l.RuleFor(x => x.Platform).NotEmpty();
            l.RuleFor(x => x.Url).NotEmpty().MaximumLength(300);
        });
        RuleFor(x => x.Links).Must(l => l.Select(x => x.Platform).Distinct().Count() == l.Count).When(x => x.Links is not null)
            .WithMessage("Add each social platform only once.");
    }
}

public sealed class UpdateSocialLinksHandler(IUnitOfWork uow, ICurrentUser user, IMediator mediator) : IRequestHandler<UpdateSocialLinksCommand, IReadOnlyList<SocialLinkDto>>
{
    public async Task<IReadOnlyList<SocialLinkDto>> Handle(UpdateSocialLinksCommand r, CancellationToken ct)
    {
        var b = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var platforms = await uow.Repository<LookupValue>().QueryNoTracking().Where(l => l.LookupType == LookupTypes.SocialPlatform && l.IsActive)
            .ToDictionaryAsync(l => l.Code, l => l.Description, ct);
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < r.Links.Count; i++)
        {
            var link = r.Links[i];
            if (!platforms.TryGetValue(link.Platform, out var example)) errors[$"links[{i}].platform"] = ["Choose a supported platform."];
            else if (!SocialLinks.MatchesPlatform(link.Url, example)) errors[$"links[{i}].url"] = [$"Enter a {link.Platform} profile URL, e.g. {example}"];
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var repo = uow.Repository<BusinessSocialLink>();
        var existing = await repo.Query().Where(l => l.BusinessId == b.Id).ToListAsync(ct);
        foreach (var row in existing.Where(e => r.Links.All(l => l.Platform != e.Platform))) repo.Remove(row);
        foreach (var link in r.Links)
        {
            var row = existing.FirstOrDefault(e => e.Platform == link.Platform);
            if (row is null) repo.Add(new BusinessSocialLink { BusinessId = b.Id, Platform = link.Platform, Url = link.Url.Trim() });
            else row.Url = link.Url.Trim();
        }
        await uow.SaveChangesAsync(ct);
        return await mediator.Send(new GetSocialLinksQuery(b.Id), ct);
    }
}

// ===================== Overview: profile card, completion, plan, activity =====================

public sealed record OwnerBusinessCardDto(Guid Id, string Name, string Slug, string? Tagline, string? LogoUrl, string? CoverImageUrl, string CategoryName,
    string? SubCategoryName, string City, string? Area, string Status, string VerificationStatus, string? PhoneNumber, string? Email, string? Website,
    int ServiceCount, DateTimeOffset CreatedOn);

public sealed record CompletionItemDto(string Key, string Label, string Hint, bool Done, string LinkUrl);
public sealed record ProfileCompletionDto(int Percent, int Completed, int Total, IReadOnlyList<CompletionItemDto> Items);

public sealed record ActivePlanDto(string Code, string Name, string Status, string BillingCycle, DateTime StartDate, DateTime EndDate, decimal Amount,
    int LeadCredits, int LeadsThisMonth, int MaxServices, int MaxImages, IReadOnlyList<string> Features, int? TrialDaysLeft);

public sealed record ActivityDto(string Type, string Title, string Description, DateTimeOffset OccurredOn, string? LinkUrl);

/// <summary>A paid-plan checkout that was started but not completed (shown as "Complete your payment").</summary>
public sealed record PendingPaymentDto(Guid OrderId, string PlanCode, string PlanName, string BillingCycle, decimal Total, string Status,
    string? FailureReason, DateTimeOffset CreatedOn);

public sealed record OwnerOverviewDto(OwnerBusinessCardDto Business, ProfileCompletionDto Completion, ActivePlanDto? Plan,
    IReadOnlyList<OwnerMediaItemDto> RecentPhotos, IReadOnlyList<OwnerMediaItemDto> Videos, int PhotoCount, IReadOnlyList<ActivityDto> Activities,
    PendingPaymentDto? PendingPayment);

public sealed record GetOwnerOverviewQuery(Guid BusinessId) : IRequest<OwnerOverviewDto>;

public sealed class GetOwnerOverviewHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<GetOwnerOverviewQuery, OwnerOverviewDto>
{
    public async Task<OwnerOverviewDto> Handle(GetOwnerOverviewQuery r, CancellationToken ct)
    {
        var owned = await OwnerAccess.GetOwnedAsync(uow, user, r.BusinessId, ct);
        var id = owned.Id;
        var today = IndianTime.Today;

        var card = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == id)
            .Select(b => new OwnerBusinessCardDto(b.Id, b.Name, b.Slug, b.Tagline, b.LogoUrl, b.CoverImageUrl, b.Category.Name,
                b.SubCategory != null ? b.SubCategory.Name : null, b.City, b.Area, b.Status, b.VerificationStatus, b.PhoneNumber, b.Email, b.Website,
                b.Services.Count(s => s.IsActive), b.CreatedOn))
            .FirstAsync(ct);

        var photos = uow.Repository<BusinessImage>().QueryNoTracking().Where(i => i.BusinessId == id);
        var photoCount = await photos.CountAsync(ct);
        var recentPhotos = await photos.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).Take(8)
            .Select(i => new OwnerMediaItemDto(i.Id, "photo", i.ImageUrl, i.ThumbnailUrl, i.Caption ?? i.AltText, null, null, null, i.IsPrimary, i.CreatedOn))
            .ToListAsync(ct);
        var videos = await uow.Repository<BusinessVideo>().QueryNoTracking().Where(v => v.BusinessId == id).OrderBy(v => v.SortOrder)
            .Select(v => new OwnerMediaItemDto(v.Id, "video", v.VideoUrl, v.PosterUrl, v.Title, v.ContentType, v.FileSize, v.DurationSeconds, false, v.CreatedOn))
            .ToListAsync(ct);
        var hasHours = await uow.Repository<BusinessHour>().QueryNoTracking().AnyAsync(h => h.BusinessId == id, ct);
        var socialCount = await uow.Repository<BusinessSocialLink>().QueryNoTracking().CountAsync(l => l.BusinessId == id, ct);

        // ---- Profile completion ----
        var items = new List<CompletionItemDto>
        {
            new("logo", "Upload your logo", "Businesses with a logo are recognised faster in search.", owned.LogoUrl != null, "/business/media"),
            new("cover", "Add a cover image", "A banner photo makes your profile look complete.", owned.CoverImageUrl != null, "/business/media"),
            new("description", "Write a detailed description", "Aim for at least 150 characters about what you do.", owned.Description.Length >= 150, "/business/profile"),
            new("photos", "Add 3 or more photos", "Show your work, premises and team.", photoCount >= 3, "/business/media"),
            new("video", "Upload a promotional video", "Videos help customers trust you before they call.", videos.Count > 0, "/business/media"),
            new("services", "List 3 or more services", "Clear services and prices bring better leads.", card.ServiceCount >= 3, "/business/services"),
            new("hours", "Set your working hours", "Needed for \"open now\" search and booking slots.", hasHours, "/business/profile"),
            new("contact", "Add WhatsApp and email", "Give customers more ways to reach you.", owned.WhatsAppNumber != null && owned.Email != null, "/business/profile"),
            new("social", "Link a website or social profile", "Customers like to see your online presence.", owned.Website != null || socialCount > 0, "/business/profile"),
            new("verified", "Get verified", owned.VerificationStatus == VerificationStatuses.Pending ? "Your documents are being reviewed by our team." : "Verified businesses earn a trust badge.",
                owned.VerificationStatus == VerificationStatuses.Verified, "/business/profile"),
        };
        var done = items.Count(i => i.Done);
        var completion = new ProfileCompletionDto((int)Math.Round(done * 100.0 / items.Count), done, items.Count, items);

        // ---- Active plan ----
        var monthStart = IndianTime.At(new DateTime(today.Year, today.Month, 1), TimeSpan.Zero);
        var leadsThisMonth = await uow.Repository<Enquiry>().QueryNoTracking().CountAsync(e => e.BusinessId == id && e.CreatedOn >= monthStart, ct);
        var sub = await uow.Repository<BusinessSubscription>().QueryNoTracking()
            .Where(s => s.BusinessId == id && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Trial) && s.StartDate <= today && s.EndDate >= today)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new { s.Plan, s.Status, s.BillingCycle, s.StartDate, s.EndDate, s.Amount })
            .FirstOrDefaultAsync(ct);
        var plan = sub is null ? null : new ActivePlanDto(sub.Plan.Code, sub.Plan.Name, sub.Status, sub.BillingCycle, sub.StartDate, sub.EndDate, sub.Amount,
            sub.Plan.LeadCredits, leadsThisMonth, sub.Plan.MaxServices, sub.Plan.MaxImages,
            sub.Plan.Features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            sub.Status == SubscriptionStatuses.Trial ? (int)(sub.EndDate - today).TotalDays : null);

        // ---- Recent activity (merged from the business's own records) ----
        var activities = new List<ActivityDto>
        {
            new("BusinessCreated", "Business profile created", $"{owned.Name} was submitted for review.", owned.CreatedOn, "/business/profile")
        };
        if (owned.ModifiedOn is { } modified && modified > owned.CreatedOn.AddMinutes(5))
            activities.Add(new("ProfileUpdated", "Profile updated", "Your business details were changed.", modified, "/business/profile"));

        activities.AddRange(await uow.Repository<Enquiry>().QueryNoTracking().Where(e => e.BusinessId == id).OrderByDescending(e => e.CreatedOn).Take(6)
            .Select(e => new ActivityDto("Lead", $"New {e.EnquiryType.ToLower()} from {e.CustomerName}", e.Message, e.CreatedOn, "/business/leads")).ToListAsync(ct));
        activities.AddRange(await uow.Repository<Booking>().QueryNoTracking().Where(bk => bk.BusinessId == id).OrderByDescending(bk => bk.CreatedOn).Take(6)
            .Select(bk => new ActivityDto("Booking", $"Booking {bk.BookingNumber} · {bk.Status}", bk.CustomerName + " booked " + bk.Service.Name, bk.CreatedOn, "/business/bookings")).ToListAsync(ct));
        activities.AddRange(await uow.Repository<Review>().QueryNoTracking().Where(rv => rv.BusinessId == id).OrderByDescending(rv => rv.CreatedOn).Take(4)
            .Select(rv => new ActivityDto("Review", $"{rv.Rating}★ review from {rv.Customer.DisplayName}", rv.Title ?? rv.Comment, rv.CreatedOn, "/business/reviews")).ToListAsync(ct));
        activities.AddRange(await photos.OrderByDescending(i => i.CreatedOn).Take(3)
            .Select(i => new ActivityDto("Photo", "Photo added", i.Caption ?? "A new photo was added to your gallery.", i.CreatedOn, "/business/media")).ToListAsync(ct));
        activities.AddRange(videos.OrderByDescending(v => v.CreatedOn).Take(3)
            .Select(v => new ActivityDto("Video", "Video uploaded", v.Title ?? "Promotional video", v.CreatedOn, "/business/media")));
        activities.AddRange(await uow.Repository<BusinessSubscription>().QueryNoTracking().Where(s => s.BusinessId == id).OrderByDescending(s => s.CreatedOn).Take(2)
            .Select(s => new ActivityDto("Plan", s.Status == SubscriptionStatuses.Trial ? $"{s.Plan.Name} trial started" : $"{s.Plan.Name} plan · {s.Status}",
                s.BillingCycle + " billing", s.CreatedOn, "/business/plan")).ToListAsync(ct));

        // ---- Unfinished checkout for a plan the business doesn't have yet ----
        var since = DateTimeOffset.UtcNow.AddDays(-14);
        var orders = uow.Repository<PaymentOrder>().QueryNoTracking().Where(o => o.BusinessId == id);
        var lastPaid = await orders.Where(o => o.Status == PaymentOrderStatuses.Paid).MaxAsync(o => (DateTimeOffset?)o.CreatedOn, ct);
        var pending = await orders
            .Where(o => o.Status != PaymentOrderStatuses.Paid && o.CreatedOn >= since && (lastPaid == null || o.CreatedOn > lastPaid))
            .OrderByDescending(o => o.CreatedOn)
            .Select(o => new PendingPaymentDto(o.Id, o.Plan.Code, o.Plan.Name, o.BillingCycle, o.TotalAmount, o.Status, o.FailureReason, o.CreatedOn))
            .FirstOrDefaultAsync(ct);
        if (pending is not null && plan?.Code == pending.PlanCode) pending = null;

        activities.AddRange(await orders.Where(o => o.Status != PaymentOrderStatuses.Created).OrderByDescending(o => o.CreatedOn).Take(3)
            .Select(o => new ActivityDto("Payment",
                o.Status == PaymentOrderStatuses.Paid ? $"Payment received · {o.Plan.Name}" : $"Payment {o.Status.ToLower()} · {o.Plan.Name}",
                o.Status == PaymentOrderStatuses.Paid ? (o.InvoiceNumber ?? o.OrderNumber) : (o.FailureReason ?? o.OrderNumber),
                o.PaidOn ?? o.ModifiedOn ?? o.CreatedOn, "/business/plan")).ToListAsync(ct));

        return new OwnerOverviewDto(card, completion, plan, recentPhotos, videos, photoCount,
            activities.OrderByDescending(a => a.OccurredOn).Take(12).ToList(), pending);
    }
}
