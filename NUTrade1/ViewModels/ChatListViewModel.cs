using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>One Messages row: the other student's name over the item being traded.</summary>
public sealed class ChatRowViewModel
{
    public ChatRowViewModel(Chat chat, string partnerName)
    {
        Chat = chat;
        PartnerName = partnerName;
    }

    public Chat Chat { get; }
    public string PartnerName { get; }
    public string ListingTitle => Chat.ListingTitle;
}

public partial class ChatListViewModel : BaseViewModel
{
    private readonly IChatService _chats;
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly INavigationService _nav;

    public ChatListViewModel(IChatService chats, IUserService users, IAuthService auth, INavigationService nav)
    {
        _chats = chats;
        _users = users;
        _auth = auth;
        _nav = nav;
        Title = "Messages";
    }

    public ObservableRangeCollection<ChatRowViewModel> Chats { get; } = new();

    public bool HasChats => Chats.Count > 0;

    /// <summary>"No conversations yet" — never while the skeleton is standing in for the list.</summary>
    public bool ShowEmptyState => !HasChats && !IsLoading;

    protected override void OnIsLoadingChangedCore(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    public override async Task OnAppearingAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        // The skeleton only on a first visit; after that the rows on screen stay put
        // while the fresh list loads, then swap in at once.
        IsLoading = Chats.Count == 0;
        try
        {
            var chats = await _chats.GetMyChatsAsync();

            // Each row needs the other student's name; asked for together, not in turn.
            var rows = await Task.WhenAll(chats.Select(async chat =>
            {
                var partnerUid = chat.PartnerUid(_auth.CurrentUid);
                var partner = partnerUid is null ? null : await _users.GetProfileAsync(partnerUid);
                return new ChatRowViewModel(chat, partner?.DisplayName is { Length: > 0 } name ? name : "NU–Lipa student");
            }));
            Chats.ReplaceAll(rows);
            OnPropertyChanged(nameof(HasChats));
        }
        finally
        {
            IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task OpenChatAsync(ChatRowViewModel? row) =>
        row is null
            ? Task.CompletedTask
            : _nav.GoToAsync(Routes.ChatRoom, new Dictionary<string, object> { ["chatId"] = row.Chat.Id });
}
