using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;

namespace NUTrade1.ViewModels;

public partial class ChatRoomViewModel : BaseViewModel
{
    private readonly IChatService _chats;
    private readonly IAuthService _auth;
    private IDisposable? _messagesSubscription;

    public ChatRoomViewModel(IChatService chats, IAuthService auth)
    {
        _chats = chats;
        _auth = auth;
        Title = "Chat";
    }

    [ObservableProperty] private string? _chatId;
    [ObservableProperty] private string _draft = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    [NotifyPropertyChangedFor(nameof(WinningBidDisplay))]
    [NotifyPropertyChangedFor(nameof(CanMarkCompleted))]
    [NotifyPropertyChangedFor(nameof(CompletionStatusText))]
    [NotifyPropertyChangedFor(nameof(ShowCompletionBar))]
    private Chat? _chat;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public bool HasMessages => Messages.Count > 0;

    public string HeaderTitle => string.IsNullOrEmpty(Chat?.ListingTitle) ? "Trade partner" : Chat!.ListingTitle;

    public string WinningBidDisplay => Money.ToDisplay(Chat?.WinningBidCentavos ?? 0);

    /// <summary>Hidden until the chat document has loaded, so the bar can't flash empty.</summary>
    public bool ShowCompletionBar => Chat is not null;

    public bool CanMarkCompleted =>
        Chat is { IsClosed: false } chat && !chat.HasConfirmed(_auth.CurrentUid);

    public string CompletionStatusText
    {
        get
        {
            if (Chat is not { } chat) return string.Empty;
            if (chat.IsClosed) return "Trade completed — both of you confirmed the handover.";
            if (chat.HasConfirmed(_auth.CurrentUid)) return "You marked this done. Waiting on the other student.";
            return "Met up and swapped? Mark it completed — the trade closes once you both do.";
        }
    }

    public override async Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(ChatId)) return;

        // Bubbles in bones until the first batch of messages arrives, rather than
        // "Say hello" over a conversation that is still loading.
        IsLoading = Messages.Count == 0;

        Chat = await _chats.GetChatAsync(ChatId);
        Title = HeaderTitle;

        _messagesSubscription = _chats.ObserveMessages(ChatId, msgs =>
        {
            Messages.Clear();
            foreach (var m in msgs) Messages.Add(m);
            OnPropertyChanged(nameof(HasMessages));
            IsLoading = false;
        });
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(ChatId)) return;

        Draft = string.Empty;
        ErrorMessage = null;

        var result = await _chats.SendMessageAsync(ChatId, text);
        if (!result.Succeeded)
        {
            ErrorMessage = result.Error;
            Draft = text;   // hand the text back rather than losing what they typed
            return;
        }

        // No optimistic append: the observer re-reads within a couple of seconds and
        // replaces the list wholesale, so adding it here would only risk a duplicate.
    }

    [RelayCommand]
    private async Task MarkCompletedAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(ChatId) || !CanMarkCompleted) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _chats.MarkTradeCompletedAsync(ChatId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Could not mark the trade completed.";
                return;
            }

            // Re-read rather than guess: whether the trade actually closed depends on
            // the other participant, and only the server knows that.
            Chat = await _chats.GetChatAsync(ChatId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override Task OnDisappearingAsync()
    {
        _messagesSubscription?.Dispose();
        _messagesSubscription = null;
        return Task.CompletedTask;
    }
}
