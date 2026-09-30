using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>
/// "Pay Now" from the Bid Approved notification: the winning bidder pays the seller by
/// QR Ph. The order is marked paid only by PayMongo's signed webhook, so this screen
/// shows the code and watches the order; it never claims payment on its own.
/// </summary>
[QueryProperty(nameof(OrderId), "orderId")]
public partial class OrderPaymentViewModel : BaseViewModel
{
    private readonly IOrderService _orders;
    private readonly INavigationService _nav;
    private IDisposable? _subscription;
    private SecondClock? _expiryClock;
    private bool _paidNoticeShown;

    public OrderPaymentViewModel(IOrderService orders, INavigationService nav)
    {
        _orders = orders;
        _nav = nav;
        Title = "Pay for your item";
    }

    [ObservableProperty] private string? _orderId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountDisplay))]
    [NotifyPropertyChangedFor(nameof(ItemTitle))]
    [NotifyPropertyChangedFor(nameof(Reference))]
    [NotifyPropertyChangedFor(nameof(QrImageSource))]
    [NotifyPropertyChangedFor(nameof(HasQrImage))]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    [NotifyPropertyChangedFor(nameof(IsPaid))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ExpiryText))]
    private Order? _order;


    public string ItemTitle => Order?.ListingTitle ?? string.Empty;
    public string AmountDisplay => Money.ToDisplay(Order?.AmountCentavos ?? 0);
    public string Reference => Order?.Reference ?? string.Empty;
    public string? QrImageSource => Order?.QrImageUrl;
    public bool HasQrImage => !string.IsNullOrEmpty(Order?.QrImageUrl);
    public bool IsOpen => Order?.IsOpen ?? true;
    public bool IsPaid => Order?.IsSettled ?? false;

    public string StatusText => Order?.Status switch
    {
        OrderStatus.Paid => "Paid — arrange the handover in your chat.",
        OrderStatus.Released => "The payment window closed and the bid was released.",
        OrderStatus.Cancelled => "This order was cancelled.",
        _ => Order?.DueAt is { } due ? $"Pay by {due.ToLocalTime():MMM d, h:mm tt} to keep your win." : "Waiting for your payment.",
    };

    public string ExpiryText => Order?.QrExpiresAt is { } at
        ? $"Expires in {CountdownClock.Format(at - DateTimeOffset.UtcNow)}"
        : string.Empty;

    public override async Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(OrderId)) return;

        // The payment card in bones until the order is known; from then on the card is
        // real and only its QR waits, in QR bones, while a fresh code is minted.
        IsLoading = Order is null;
        try
        {
            Order = await _orders.GetOrderAsync(OrderId);
        }
        finally
        {
            IsLoading = false;
        }
        if (Order is { IsOpen: true, NeedsFreshQr: true }) await GenerateAsync();

        _expiryClock ??= new SecondClock(() => OnPropertyChanged(nameof(ExpiryText)));
        _subscription = _orders.ObserveOrder(OrderId, order =>
        {
            if (order is null) return;
            Order = order;
            if (order.IsSettled) AnnouncePaid(order);
        });
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (string.IsNullOrEmpty(OrderId) || IsBusy) return;
        IsBusy = true;
        try
        {
            ErrorMessage = null;
            var result = await _orders.CreateQrPaymentAsync(OrderId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not start the payment. Try again.";
                return;
            }
            Order = result.Value;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenChatAsync() =>
        Order is { ChatId.Length: > 0 } o
            ? _nav.GoToAsync(Routes.ChatRoom, new Dictionary<string, object> { ["chatId"] = o.ChatId })
            : Task.CompletedTask;

    private void AnnouncePaid(Order order)
    {
        if (_paidNoticeShown) return;
        _paidNoticeShown = true;
        NotificationCenter.Current.Show(new AppNotification(
            "Payment Received! 🎉",
            $"You paid {Money.ToDisplay(order.AmountCentavos)} for {order.ListingTitle}. Arrange the handover with the seller in your chat.",
            "Open Chat",
            () => _nav.GoToAsync(Routes.ChatRoom, new Dictionary<string, object> { ["chatId"] = order.ChatId })));
    }

    public override Task OnDisappearingAsync()
    {
        _subscription?.Dispose();
        _subscription = null;
        _expiryClock?.Dispose();
        _expiryClock = null;
        return Task.CompletedTask;
    }
}
