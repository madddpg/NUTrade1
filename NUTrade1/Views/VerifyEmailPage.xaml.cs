using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class VerifyEmailPage : AppContentPage
{
    public VerifyEmailPage(VerifyEmailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
