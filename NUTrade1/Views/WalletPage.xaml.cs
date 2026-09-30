using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class WalletPage : AppContentPage
{
    public WalletPage(WalletViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
