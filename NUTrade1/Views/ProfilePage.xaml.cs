using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ProfilePage : AppContentPage
{
    public ProfilePage(ProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
