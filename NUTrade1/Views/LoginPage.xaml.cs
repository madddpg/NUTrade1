using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class LoginPage : AppContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
