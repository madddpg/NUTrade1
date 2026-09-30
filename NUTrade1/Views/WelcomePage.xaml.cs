using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class WelcomePage : AppContentPage
{
    public WelcomePage(WelcomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
