using NUTrade1.Core;
using NUTrade1.Services;
using NUTrade1.Views;

namespace NUTrade1;

public partial class AppShell : Shell
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly IPushTokenProvider _push;
    private readonly WinWatcher _wins;
    private bool _gateApplied;
    private bool _pushAttempted;

    public AppShell(IAuthService auth, IUserService users, IPushTokenProvider push, WinWatcher wins)
    {
        InitializeComponent();

        _auth = auth;
        _users = users;
        _push = push;
        _wins = wins;

        RegisterDetailRoutes();

        _auth.AuthStateChanged += (_, _) => Dispatcher.Dispatch(async () => await ApplyGateAsync());
    }

    private static void RegisterDetailRoutes()
    {
        Routing.RegisterRoute(Routes.ListingDetail, typeof(ListingDetailPage));
        Routing.RegisterRoute(Routes.Payment, typeof(PaymentPage));
        Routing.RegisterRoute(Routes.OrderPayment, typeof(OrderPaymentPage));
        Routing.RegisterRoute(Routes.ChatRoom, typeof(ChatRoomPage));
        Routing.RegisterRoute(Routes.ListingApprovals, typeof(ListingApprovalsPage));
        Routing.RegisterRoute(Routes.Wallet, typeof(WalletPage));
        Routing.RegisterRoute(Routes.Payouts, typeof(PayoutsPage));
        Routing.RegisterRoute(Routes.BidDeposit, typeof(BidDepositPage));
        Routing.RegisterRoute(Routes.PhotoViewer, typeof(PhotoViewerPage));
        Routing.RegisterRoute(Routes.ReviewListing, typeof(ReviewListingPage));
        Routing.RegisterRoute(Routes.Register, typeof(RegisterPage));
        Routing.RegisterRoute(Routes.ForgotPassword, typeof(ForgotPasswordPage));
    }

    protected override async void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (_gateApplied) return;
        _gateApplied = true;
        await ApplyGateAsync();
    }

    /// <summary>The screens a signed-out student is allowed to be on.</summary>
    private static readonly string[] SignedOutRoutes =
        { Routes.Welcome, Routes.Login, Routes.Register, Routes.ForgotPassword };

    /// <summary>
    /// Sends the student to Welcome, the emailed-code screen, profile setup or the app,
    /// whichever their account state calls for.
    ///
    /// It only moves them when they are somewhere that state does not allow. Anything
    /// that refreshes the ID token raises AuthStateChanged — opening Profile does, on
    /// every visit — and redirecting to the Feed tab on each of those bounced students
    /// off whichever tab they had just opened.
    /// </summary>
    private async Task ApplyGateAsync()
    {
        // Pick up a persisted refresh token before deciding where to land, so a
        // returning student is not bounced through the login screen on every launch.
        await _auth.RestoreSessionAsync();

        var location = CurrentState.Location.OriginalString.Trim('/');

        string target;
        if (!_auth.IsSignedIn)
        {
            // Signed out: stop watching for wins and drop anything meant for the last account.
            _wins.Stop();
            _pushAttempted = false;
            NotificationCenter.Current.Clear();
            Toaster.Current.Clear();
            if (IsAt(location, SignedOutRoutes)) return;
            target = $"//{Routes.Welcome}";
        }
        else if (!_auth.IsVerified)
        {
            // Registration is not finished until the emailed code has been entered:
            // the "verified" claim is what the rules and Functions gate everything on.
            if (IsAt(location, Routes.VerifyEmail)) return;
            target = $"//{Routes.VerifyEmail}";
        }
        else if (await _users.GetCurrentProfileAsync() is null)
        {
            if (IsAt(location, Routes.ProfileSetup)) return;
            target = $"//{Routes.ProfileSetup}";
        }
        else
        {
            // In the app proper: announce won auctions (Bid Approved / You Have a Winner).
            _wins.Start();
            await TryRegisterPushTokenAsync();

            // Already past the gate — leave them on whichever tab or page they opened.
            var onGateScreen = location.Length == 0
                || IsAt(location, SignedOutRoutes)
                || IsAt(location, Routes.VerifyEmail, Routes.ProfileSetup);
            if (!onGateScreen) return;
            target = Routes.FeedTab;
        }

        await GoToAsync(target);
    }

    /// <summary>
    /// Writes this device's FCM token onto the signed-in profile when one exists.
    /// No token is invented: the provider returns null until messaging is configured,
    /// and that is stored as a no-op rather than a placeholder.
    /// </summary>
    private async Task TryRegisterPushTokenAsync()
    {
        if (_pushAttempted) return;
        _pushAttempted = true;
        try
        {
            var token = await _push.GetTokenAsync();
            await _users.RegisterPushTokenAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _pushAttempted = false;
        }
    }

    private static bool IsAt(string location, params string[] routes) =>
        routes.Any(r => location.Equals(r, StringComparison.OrdinalIgnoreCase)
                     || location.StartsWith($"{r}/", StringComparison.OrdinalIgnoreCase));
}
