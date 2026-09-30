#if DEBUG
// TEMPORARY: screenshot harness for the skeleton loaders. Delete before shipping.
using NUTrade1.ViewModels;
using NUTrade1.Views;

namespace NUTrade1;

internal static class SkeletonPreview
{
    private static readonly Dictionary<string, Type> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["feed"] = typeof(FeedPage),
        ["offers"] = typeof(OffersPage),
        ["chats"] = typeof(ChatListPage),
        ["chatroom"] = typeof(ChatRoomPage),
        ["profile"] = typeof(ProfilePage),
        ["listing"] = typeof(ListingDetailPage),
        ["wallet"] = typeof(WalletPage),
        ["payouts"] = typeof(PayoutsPage),
        ["approvals"] = typeof(ListingApprovalsPage),
        ["order"] = typeof(OrderPaymentPage),
        ["deposit"] = typeof(BidDepositPage),
        ["payment"] = typeof(PaymentPage),
    };

    public static Page? TryCreate(IServiceProvider services)
    {
        var flag = Path.Combine(FileSystem.AppDataDirectory, "skeleton-preview.txt");
        if (!File.Exists(flag)) return null;
        var key = File.ReadAllText(flag).Trim();
        if (!Pages.TryGetValue(key, out var type)) return null;

        var page = (Page)services.GetRequiredService(type);
        page.Appearing += async (_, _) =>
        {
            // Let the page's own load finish (or fail, signed out), then pin it loading.
            await Task.Delay(4000);
            if (page.BindingContext is BaseViewModel vm)
            {
                vm.IsLoading = true;
                if (key.Equals("payment", StringComparison.OrdinalIgnoreCase) || key.Equals("order", StringComparison.OrdinalIgnoreCase))
                    vm.IsBusy = true;
            }
        };
        return page;
    }
}
#endif
