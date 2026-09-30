using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ListingApprovalsPage : AppContentPage
{
    public ListingApprovalsPage(ListingApprovalsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
