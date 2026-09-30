using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class FeedPage : AppContentPage
{
    public FeedPage(FeedViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
