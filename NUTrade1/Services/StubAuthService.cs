using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Offline stand-in for <see cref="IAuthService"/>, used only when
/// <c>MauiProgram.UseStubServices</c> is on. Accepts any campus-domain email and
/// reports the account as verified, so the UI can be clicked through with no network.
/// </summary>
public sealed class StubAuthService : IAuthService
{
    public string? CurrentUid { get; private set; }
    public string? CurrentEmail { get; private set; }
    public bool IsSignedIn => CurrentUid is not null;
    public bool IsVerified { get; private set; }

    /// <summary>Offline there is only one person at the keyboard, so they review their own
    /// listings — otherwise the post-an-auction flow could never finish.</summary>
    public bool IsAdmin => IsSignedIn;

    public bool IsGoogleSignInAvailable => false;

    public event EventHandler? AuthStateChanged;

    public Task RestoreSessionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<OperationResult> SignInWithEmailAsync(string email, string password, CancellationToken ct = default) =>
        Task.FromResult(StartSession(email));

    public Task<OperationResult> SignUpWithEmailAsync(string email, string password, CancellationToken ct = default) =>
        Task.FromResult(StartSession(email));

    public Task<OperationResult> SignInWithGoogleAsync(CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail("Google sign-in needs a network. Use email and password offline."));

    public Task RefreshClaimsAsync(CancellationToken ct = default, bool force = false) => Task.CompletedTask;

    /// <summary>Stands in for verifyEmailOtp granting the claim. See StubEmailVerificationService.</summary>
    internal void MarkVerified() => IsVerified = true;

    public Task SignOutAsync(CancellationToken ct = default)
    {
        CurrentUid = null;
        CurrentEmail = null;
        IsVerified = false;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Emails that finished offline registration, so signing in with one is verified.</summary>
    private static readonly HashSet<string> RegisteredEmails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stands in for completeSignup creating a verified account.</summary>
    internal static void Register(string email) => RegisteredEmails.Add(email.Trim());

    /// <summary>The uid an offline session gets for <paramref name="email"/>.</summary>
    internal static string OfflineUid(string email) => "offline-" + email.Trim().GetHashCode().ToString("x8");

    private OperationResult StartSession(string email)
    {
        if (!NUTradeConstants.IsValidEmail(email))
            return OperationResult.Fail("Enter a valid email address.");

        CurrentEmail = email.Trim();
        CurrentUid = OfflineUid(CurrentEmail);
        IsVerified = RegisteredEmails.Contains(CurrentEmail);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult.Ok();
    }
}
