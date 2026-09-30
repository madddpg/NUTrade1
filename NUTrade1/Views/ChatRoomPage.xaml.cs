using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ChatRoomPage : AppContentPage
{
    public ChatRoomPage(ChatRoomViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
