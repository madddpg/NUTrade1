namespace NUTrade1.Core;

/// <summary>
/// The device's Firebase Cloud Messaging registration token.
/// The MAUI app does not ship a messaging SDK, so the registered provider returns null
/// until one is configured. Callers must not invent a token.
/// </summary>
public interface IPushTokenProvider
{
    /// <summary>The current device token, or null when messaging is not configured.</summary>
    Task<string?> GetTokenAsync(CancellationToken ct = default);
}

/// <summary>Used until a platform messaging SDK and google-services config are wired in.</summary>
public sealed class UnconfiguredPushTokenProvider : IPushTokenProvider
{
    public Task<string?> GetTokenAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
