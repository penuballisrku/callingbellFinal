using FluentValidation;
using MediatR;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;

namespace CallingBell.Application.Common.Behaviours;

public sealed class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var errors = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .GroupBy(f => ToCamelCase(f.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await next();
    }

    // "Business.Services[0].Name" -> "business.services[0].name" so clients can map nested errors to fields.
    private static string ToCamelCase(string name) =>
        string.Join('.', name.Split('.').Select(part => string.IsNullOrEmpty(part) ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
