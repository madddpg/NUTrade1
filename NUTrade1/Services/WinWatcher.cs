using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>
/// Announces won auctions with the notification card, to both sides:
/// the buyer gets "Bid Approved! 🎉 … Pay Now", the seller "You Have a Winner! 🎉 … Open Chat".
///
/// A won auction is exactly when an order is created (seller approves a bid, or the
/// auction closes on its own), so this watches the signed-in student's orders — every
/// 30 seconds while signed in — and announces each new one once. Which orders were
/// already announced is remembered per account on the device, so nothing repeats
/// across launches.
/// </summary>
public sealed class WinWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IChatService _chats;
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly INavigationService _nav;
    private CancellationTokenSource? _cts;

    public WinWatcher(IChatService chats, IUserService users, IAuthService auth, INavigationService nav)
    {
        _chats = chats;
        _users = users;
        _auth = auth;
        _nav = nav;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A failed poll is retried next time round; never let it end the loop.
            }

            try
            {
                await Task.Delay(Interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        if (_auth.CurrentUid is not { } uid) return;

        var key = $"nutrade.announcedWins.{uid}";
        var firstRun = !Preferences.ContainsKey(key);
        var announced = Preferences.Get(key, string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        var fresh = (await _chats.GetMyChatsAsync(ct))
            .Where(c => !announced.Contains(c.Id))
            .OrderBy(c => c.LastMessageAt ?? DateTimeOffset.MinValue)
            .ToList();
        if (fresh.Count == 0) return;

        foreach (var chat in fresh)
        {
            announced.Add(chat.Id);
            // On a fresh install only trades still in progress are announced — not every
            // chat this account ever had.
            if (firstRun && chat.CompletedBy.Count > 0) continue;
            await AnnounceAsync(chat, uid, ct);
        }

        Preferences.Set(key, string.Join(',', announced));
    }

    private async Task AnnounceAsync(Chat chat, string uid, CancellationToken ct)
    {
        var openChat = () => _nav.GoToAsync(
            Routes.ChatRoom, new Dictionary<string, object> { ["chatId"] = chat.Id });

        if (chat.BuyerUid == uid)
        {
            var seller = await _users.GetProfileAsync(chat.SellerUid, ct);
            var sellerName = seller?.DisplayName is { Length: > 0 } n ? n : "The seller";
            NotificationCenter.Current.Show(new AppNotification(
                "You Won! 🎉",
                $"You won {chat.ListingTitle} at {Money.ToDisplay(chat.WinningBidCentavos)}. Your deposit is held — " +
                $"agree a campus meetup with {sellerName} and pay the rest there.",
                "Open Chat",
                openChat));
        }
        else if (chat.SellerUid == uid)
        {
            var buyer = await _users.GetProfileAsync(chat.BuyerUid, ct);
            var buyerName = buyer?.DisplayName is { Length: > 0 } b ? b : "A student";
            NotificationCenter.Current.Show(new AppNotification(
                "You Have a Winner! 🎉",
                $"{buyerName} won {chat.ListingTitle} at {Money.ToDisplay(chat.WinningBidCentavos)}. Their deposit is " +
                "held and pays out to you once you both confirm the handover.",
                "Open Chat",
                openChat));
        }
    }
}
