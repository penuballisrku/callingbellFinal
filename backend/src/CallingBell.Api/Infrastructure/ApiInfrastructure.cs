using System.Security.Claims;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Common.Models;
using CallingBell.Domain.Constants;
using Microsoft.AspNetCore.Diagnostics;

namespace CallingBell.Api.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
    public bool HasPermission(string permission) => Principal?.HasClaim(Permissions.ClaimType, permission) == true;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Maps application exceptions to the standard response envelope and HTTP status codes.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, response) = exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, ApiResponse.Fail(v.Message, v.Errors)),
            BadRequestException b => (StatusCodes.Status400BadRequest, ApiResponse.Fail(b.Message)),
            NotFoundException n => (StatusCodes.Status404NotFound, ApiResponse.Fail(n.Message)),
            ForbiddenAccessException f => (context.User.Identity?.IsAuthenticated == true ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
                ApiResponse.Fail(f.Message)),
            ConflictException c => (StatusCodes.Status409Conflict, ApiResponse.Fail(c.Message)),
            OperationCanceledException => (499, ApiResponse.Fail("The request was cancelled.")),
            _ => (StatusCodes.Status500InternalServerError, ApiResponse.Fail(environment.IsDevelopment()
                ? exception.Message
                : "Something went wrong on our side. Please try again."))
        };

        if (status >= 500) logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(response, ct);
        return true;
    }
}
