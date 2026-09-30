using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class OffersPage : AppContentPage
{
    public OffersPage(OffersViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
