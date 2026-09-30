using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ListingDetailPage : AppContentPage
{
    public ListingDetailPage(ListingDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
