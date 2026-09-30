using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ProfileSetupPage : AppContentPage
{
    public ProfileSetupPage(ProfileSetupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
