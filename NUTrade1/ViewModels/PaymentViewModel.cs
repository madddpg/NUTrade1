using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

[QueryProperty(nameof(ListingId), "listingId")]
[QueryProperty(nameof(Package), "package")]
public partial class PaymentViewModel : BaseViewModel
{
    /// <summary>Where the listing is on its way to the feed, as far as this screen is concerned.</summary>
    public enum Stage
    {
        /// <summary>The QR is up (or loading) and the fee hasn't cleared.</summary>
        AwaitingPayment,
        /// <summary>Paid, or free — sitting in the admin queue.</summary>
        AwaitingApproval,
        /// <summary>An admin approved it; it is live or queued for the next hourly refresh.</summary>
        Approved,
        Rejected,
    }

    private readonly IPaymentService _payments;
    private readonly IListingService _listings;
    private readonly INavigationService _nav;
    private IDisposable? _listingSubscription;
    private CancellationTokenSource? _paymentChecks;
    private bool _postedNoticeShown;

    /// <summary>How often the QR screen asks the backend to check with PayMongo.</summary>
    private static readonly TimeSpan PaymentCheckInterval = TimeSpan.FromSeconds(10);

    public PaymentViewModel(IPaymentService payments, IListingService listings, INavigationService nav)
    {
        _payments = payments;
        _listings = listings;
        _nav = nav;
        Title = "Pay listing fee";
    }

    [ObservableProperty] private string? _listingId;
    [ObservableProperty] private ListingPackage _package = ListingPackage.Additional;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountDisplay))]
    [NotifyPropertyChangedFor(nameof(HasQrImage))]
    [NotifyPropertyChangedFor(nameof(QrFileName))]
    [NotifyPropertyChangedFor(nameof(ExpiryText))]
    [NotifyPropertyChangedFor(nameof(ShowQr))]
    private QrPaymentResult? _qr;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQr))]
    [NotifyPropertyChangedFor(nameof(IsAwaitingApproval))]
    [NotifyPropertyChangedFor(nameof(IsApproved))]
    [NotifyPropertyChangedFor(nameof(IsRejected))]
    [NotifyPropertyChangedFor(nameof(IsSubmitted))]
    [NotifyPropertyChangedFor(nameof(Kicker))]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyPropertyChangedFor(nameof(Subheading))]
    private Stage _currentStage = Stage.AwaitingPayment;


    /// <summary>Reply to "I've paid — check now" when PayMongo hasn't seen the money yet.</summary>
    [ObservableProperty] private string? _paymentCheckMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    private bool _isCheckingPayment;

    [ObservableProperty] private string _approvedText = string.Empty;
    [ObservableProperty] private string _rejectedText = string.Empty;

    public string PackageName => Package switch
    {
        ListingPackage.Priority => "Priority",
        ListingPackage.Additional => "Additional",
        _ => "Free",
    };
    public string AmountDisplay => Money.ToDisplay(Qr?.AmountCentavos ?? NUTradeConstants.FeeForPackage(Package));
    public bool HasQrImage => !string.IsNullOrEmpty(Qr?.QrImageUrl) || !string.IsNullOrEmpty(Qr?.QrImageBase64);

    /// <summary>What "Save QR" calls the picture, so it is easy to find in the gallery.</summary>
    public string QrFileName => $"nutrade-listing-fee-{DateTime.Now:yyyyMMdd-HHmm}";

    /// <summary>PayMongo's own expiry for the code on screen (about 30 minutes).</summary>
    public string ExpiryText => Qr is { RequiresPayment: true } qr
        ? $"This code works until {qr.ExpiresAt.ToLocalTime():h:mm tt}. Generate a new one after that."
        : "QR codes expire about 30 minutes after they're generated.";

    /// <summary>The QR card — hidden for a free first post, which has nothing to pay.</summary>
    public bool ShowQr => CurrentStage == Stage.AwaitingPayment && (Qr?.RequiresPayment ?? true);

    public bool IsAwaitingApproval => CurrentStage == Stage.AwaitingApproval;
    public bool IsApproved => CurrentStage == Stage.Approved;
    public bool IsRejected => CurrentStage == Stage.Rejected;

    /// <summary>The fee is settled one way or another, so the "leave this screen" actions show.</summary>
    public bool IsSubmitted => CurrentStage != Stage.AwaitingPayment;

    public string Kicker => CurrentStage switch
    {
        Stage.AwaitingApproval => "Posted",
        Stage.Approved => "Approved",
        Stage.Rejected => "Not approved",
        _ => "Almost there",
    };

    public string Heading => CurrentStage switch
    {
        Stage.AwaitingApproval => "Waiting for admin approval",
        Stage.Approved => "Your auction is approved",
        Stage.Rejected => "Your listing wasn't approved",
        _ => "Scan to pay the listing fee",
    };

    public string Subheading => CurrentStage switch
    {
        Stage.AwaitingApproval =>
            "An admin reviews every new listing before it goes on the campus feed. Your 24-hour auction starts the moment it's approved.",
        Stage.Approved => "An admin approved your listing and its 24-hour countdown has started.",
        Stage.Rejected => "Nothing was published. You can post a corrected listing any time.",
        _ => "Pay with any QR Ph banking or e-wallet app. Once the fee clears, your listing goes to an admin for approval.",
    };

    public override async Task OnAppearingAsync()
    {
        await GenerateAsync();

        if (string.IsNullOrEmpty(ListingId)) return;
        _listingSubscription = _listings.ObserveListing(ListingId, Apply);
        StartPaymentChecks();
    }

    /// <summary>
    /// While the QR is up, asks the backend every few seconds to check with PayMongo.
    /// The webhook is the fast path, but when it is late or missing this is what moves a
    /// paid listing on — the listing observer then picks up the new status.
    /// </summary>
    private void StartPaymentChecks()
    {
        StopPaymentChecks();
        _paymentChecks = new CancellationTokenSource();
        _ = CheckPaymentLoopAsync(_paymentChecks.Token);
    }

    private void StopPaymentChecks()
    {
        _paymentChecks?.Cancel();
        _paymentChecks?.Dispose();
        _paymentChecks = null;
    }

    private async Task CheckPaymentLoopAsync(CancellationToken ct)
    {
        try
        {
            while (CurrentStage == Stage.AwaitingPayment)
            {
                await Task.Delay(PaymentCheckInterval, ct);
                if (ShowQr && Qr is not null && !string.IsNullOrEmpty(ListingId))
                    await _payments.CheckListingPaymentAsync(ListingId, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Left the screen.
        }
    }

    /// <summary>"I've paid — check now": the same PayMongo check, on demand, with an answer.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNowAsync()
    {
        if (string.IsNullOrEmpty(ListingId)) return;
        IsCheckingPayment = true;
        PaymentCheckMessage = null;
        try
        {
            var result = await _payments.CheckListingPaymentAsync(ListingId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error;
                return;
            }

            Apply(await _listings.GetListingAsync(ListingId));
            if (CurrentStage == Stage.AwaitingPayment)
                PaymentCheckMessage = "PayMongo hasn't confirmed this payment yet. If you just paid, give it a minute — this screen keeps checking.";
        }
        finally
        {
            IsCheckingPayment = false;
        }
    }

    private bool CanCheckNow() => !IsCheckingPayment;

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (string.IsNullOrEmpty(ListingId) || IsBusy || CurrentStage != Stage.AwaitingPayment) return;
        IsBusy = true;
        try
        {
            ErrorMessage = null;
            var result = await _payments.CreateQrPaymentAsync(ListingId, Package);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not start the payment. Try again.";
                return;
            }

            Qr = result.Value;

            // A free first auction is already in the admin queue by the time this
            // returns — there is no QR to wait on.
            if (Qr is { RequiresPayment: false }) MarkPosted();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Follows the listing document: paid → approved (or rejected) by an admin.</summary>
    private void Apply(Listing? listing)
    {
        switch (listing?.Status)
        {
            case ListingStatus.PendingApproval:
                MarkPosted();
                break;

            case ListingStatus.Active:
                // Approval can land between two polls, so the "posted" moment may never
                // be observed on its own — still tell the seller it went through.
                MarkPosted();
                ApprovedText = listing.IsVisible || listing.VisibleFrom is null
                    ? "Your listing is now live on the campus feed."
                    : $"It joins the campus feed at the next hourly refresh, {listing.VisibleFrom.Value.ToLocalTime():h:mm tt}.";
                CurrentStage = Stage.Approved;
                break;

            case ListingStatus.Rejected:
                RejectedText = string.IsNullOrWhiteSpace(listing.RejectionReason)
                    ? "An admin reviewed this listing and didn't approve it."
                    : $"Admin's note: {listing.RejectionReason}";
                CurrentStage = Stage.Rejected;
                break;
        }
    }

    /// <summary>The fee is settled: show the notification once and move to the approval wait.</summary>
    private void MarkPosted()
    {
        ErrorMessage = null;
        PaymentCheckMessage = null;
        if (CurrentStage == Stage.AwaitingPayment) CurrentStage = Stage.AwaitingApproval;

        if (_postedNoticeShown) return;
        _postedNoticeShown = true;
        NotificationCenter.Current.Show(new AppNotification(
            "Item Successfully Posted! 🎉",
            "Your listing was sent to an admin for approval. It appears on the campus feed once it's approved.",
            "View Listings",
            () => _nav.GoToAsync(Routes.ProfileTab)));
    }

    [RelayCommand]
    private Task DoneAsync() => _nav.GoToAsync(Routes.FeedTab);

    [RelayCommand]
    private Task ViewMyListingsAsync() => _nav.GoToAsync(Routes.ProfileTab);

    public override Task OnDisappearingAsync()
    {
        StopPaymentChecks();
        _listingSubscription?.Dispose();
        _listingSubscription = null;
        return Task.CompletedTask;
    }
}
