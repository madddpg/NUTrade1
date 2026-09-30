using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ReviewListingPage : AppContentPage
{
    public ReviewListingPage(ReviewListingViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
