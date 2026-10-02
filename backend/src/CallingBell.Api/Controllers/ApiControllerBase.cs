using CallingBell.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CallingBell.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;
    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected static ActionResult<ApiResponse<T>> Success<T>(T data, string message = "Success") => new OkObjectResult(ApiResponse<T>.Ok(data, message));

    protected static ActionResult<ApiResponse<IReadOnlyList<T>>> Paged<T>(PagedResult<T> result) => new OkObjectResult(ApiResponse.Paged(result));

    protected static ActionResult<ApiResponse<object>> Done(string message = "Success") =>
        new OkObjectResult(new ApiResponse<object> { Success = true, Message = message });
}
