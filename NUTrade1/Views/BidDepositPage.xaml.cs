using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class BidDepositPage : AppContentPage
{
    public BidDepositPage(BidDepositViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
