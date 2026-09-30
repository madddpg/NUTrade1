using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ForgotPasswordPage : AppContentPage
{
    public ForgotPasswordPage(ForgotPasswordViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
