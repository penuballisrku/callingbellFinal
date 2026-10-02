namespace CallingBell.Application.Common.Exceptions;

public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

public sealed class ForbiddenAccessException(string message = "You do not have access to this resource.")
    : Exception(message);

public sealed class ConflictException(string message) : Exception(message);

public sealed class BadRequestException(string message) : Exception(message);

public sealed class ValidationException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
}
