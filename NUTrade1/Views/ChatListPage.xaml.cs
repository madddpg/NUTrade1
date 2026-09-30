using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ChatListPage : AppContentPage
{
    public ChatListPage(ChatListViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
