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

            // One missing profile must not blank the whole list — that is the only
            // door into the room after a win.
            var rows = new List<ChatRowViewModel>(chats.Count);
            foreach (var chat in chats)
            {
                var partnerUid = chat.PartnerUid(_auth.CurrentUid);
                UserProfile? partner = null;
                if (partnerUid is not null)
                {
                    try
                    {
                        partner = await _users.GetProfileAsync(partnerUid);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        partner = null;
                    }
                }

                rows.Add(new ChatRowViewModel(
                    chat,
                    partner?.DisplayName is { Length: > 0 } name ? name : "NU–Lipa student"));
            }

            Chats.ReplaceAll(rows);
            OnPropertyChanged(nameof(HasChats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Couldn't load your messages. Open the tab again to try.";
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
