using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class RegisterPage : AppContentPage
{
    public RegisterPage(RegisterViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
