using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class PostTradePage : AppContentPage
{
    public PostTradePage(PostTradeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
