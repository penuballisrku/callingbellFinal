using CallingBell.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CallingBell.Infrastructure.Identity;

/// <summary>
/// Development SMS sender: writes the message to the application log instead of texting it.
/// Register a real gateway implementation of <see cref="ISmsSender"/> (e.g. MSG91, Twilio) for production.
/// </summary>
internal sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        logger.LogWarning("SMS to {PhoneNumber} (not delivered - no SMS gateway configured): {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}
