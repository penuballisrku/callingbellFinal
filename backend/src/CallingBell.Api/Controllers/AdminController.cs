using CallingBell.Application.Common.Models;
using CallingBell.Application.Features.Admin;
using CallingBell.Application.Features.Owner;
using CallingBell.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallingBell.Api.Controllers;

[Authorize(Roles = Roles.Administrator), Route("api/admin")]
public sealed class AdminController : ApiControllerBase
{
    public sealed record BusinessUpdate(string? Status, string? VerificationStatus, bool? IsFeatured, string? Note);
    public sealed record CategoryUpdate(string Name, string? Description, bool IsActive, bool IsFeatured, int SortOrder);
    public sealed record ToggleUpdate(bool IsActive, bool IsFeatured);
    public sealed record CityUpdate(bool IsActive, bool IsPopular);
    public sealed record UserUpdate(bool IsActive);
    public sealed record ModerationRequest(string Action);

    [Authorize(Policy = Permissions.AnalyticsView), HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<AdminDashboardDto>>> Dashboard(CancellationToken ct) =>
        Success(await Sender.Send(new GetAdminDashboardQuery(), ct));

    [Authorize(Policy = Permissions.BusinessesManage), HttpGet("businesses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminBusinessRowDto>>>> Businesses([FromQuery] GetAdminBusinessesQuery query, CancellationToken ct) =>
        Paged(await Sender.Send(query, ct));

    [Authorize(Policy = Permissions.BusinessesManage), HttpGet("businesses/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminBusinessDetailDto>>> Business(Guid id, CancellationToken ct) =>
        Success(await Sender.Send(new GetAdminBusinessDetailQuery(id), ct));

    [Authorize(Policy = Permissions.BusinessesManage), HttpPatch("businesses/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateBusiness(Guid id, BusinessUpdate body, CancellationToken ct)
    {
        await Sender.Send(new UpdateBusinessAdminCommand(id, body.Status, body.VerificationStatus, body.IsFeatured, body.Note), ct);
        return Done("Business updated");
    }

    [Authorize(Policy = Permissions.CategoriesManage), HttpGet("categories")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCategoryDto>>>> Categories(CancellationToken ct) =>
        Success(await Sender.Send(new GetAdminCategoriesQuery(), ct));

    [Authorize(Policy = Permissions.CategoriesManage), HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateCategory(Guid id, CategoryUpdate body, CancellationToken ct)
    {
        await Sender.Send(new UpdateCategoryCommand(id, body.Name, body.Description, body.IsActive, body.IsFeatured, body.SortOrder), ct);
        return Done("Category saved");
    }

    [Authorize(Policy = Permissions.CategoriesManage), HttpPatch("subcategories/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateSubCategory(Guid id, ToggleUpdate body, CancellationToken ct)
    {
        await Sender.Send(new UpdateSubCategoryCommand(id, body.IsActive, body.IsFeatured), ct);
        return Done("Sub-category saved");
    }

    [Authorize(Policy = Permissions.CitiesManage), HttpGet("cities")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCityDto>>>> Cities(CancellationToken ct) =>
        Success(await Sender.Send(new GetAdminCitiesQuery(), ct));

    [Authorize(Policy = Permissions.CitiesManage), HttpPatch("cities/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateCity(Guid id, CityUpdate body, CancellationToken ct)
    {
        await Sender.Send(new UpdateCityCommand(id, body.IsActive, body.IsPopular), ct);
        return Done("City saved");
    }

    [Authorize(Policy = Permissions.UsersManage), HttpGet("users")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminUserDto>>>> Users([FromQuery] GetAdminUsersQuery query, CancellationToken ct) =>
        Paged(await Sender.Send(query, ct));

    [Authorize(Policy = Permissions.UsersManage), HttpPatch("users/{id}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateUser(string id, UserUpdate body, CancellationToken ct)
    {
        await Sender.Send(new UpdateUserStatusCommand(id, body.IsActive), ct);
        return Done(body.IsActive ? "User activated" : "User deactivated");
    }

    [Authorize(Policy = Permissions.ReviewsModerate), HttpGet("reviews")]
    public async Task<ActionResult<ApiResponse<OwnerListResult<AdminReviewDto>>>> Reviews([FromQuery] GetAdminReviewsQuery query, CancellationToken ct) =>
        Success(await Sender.Send(query, ct));

    [Authorize(Policy = Permissions.ReviewsModerate), HttpPatch("reviews/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> ModerateReview(Guid id, ModerationRequest body, CancellationToken ct)
    {
        await Sender.Send(new ModerateReviewCommand(id, body.Action), ct);
        return Done(body.Action == "publish" ? "Review published" : "Review rejected");
    }

    [Authorize(Policy = Permissions.AdvertisementsManage), HttpGet("advertisements")]
    public async Task<ActionResult<ApiResponse<OwnerListResult<AdminAdDto>>>> Ads([FromQuery] GetAdminAdsQuery query, CancellationToken ct) =>
        Success(await Sender.Send(query, ct));

    [Authorize(Policy = Permissions.AdvertisementsManage), HttpPatch("advertisements/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> ModerateAd(Guid id, ModerationRequest body, CancellationToken ct)
    {
        await Sender.Send(new ModerateAdCommand(id, body.Action), ct);
        return Done("Campaign updated");
    }

    [Authorize(Policy = Permissions.SubscriptionsManage), HttpGet("subscriptions")]
    public async Task<ActionResult<ApiResponse<AdminSubscriptionsDto>>> Subscriptions([FromQuery] GetAdminSubscriptionsQuery query, CancellationToken ct) =>
        Success(await Sender.Send(query, ct));
}
