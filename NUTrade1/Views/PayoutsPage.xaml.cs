using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class PayoutsPage : AppContentPage
{
    public PayoutsPage(PayoutsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
