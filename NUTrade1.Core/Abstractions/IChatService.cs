namespace NUTrade1.Core;

/// <summary>Chat rooms and their message subcollections.</summary>
public interface IChatService
{
    /// <summary>Chats the signed-in user participates in, most-recent activity first.</summary>
    Task<IReadOnlyList<Chat>> GetMyChatsAsync(CancellationToken ct = default);

    Task<Chat?> GetChatAsync(string chatId, CancellationToken ct = default);

    Task<OperationResult> SendMessageAsync(string chatId, string text, CancellationToken ct = default);

    /// <summary>Subscribes to the message subcollection in send order. Dispose to detach.</summary>
    IDisposable ObserveMessages(string chatId, Action<IReadOnlyList<ChatMessage>> onChanged);

    /// <summary>Marks the signed-in user's side of the trade complete. When both sides have, a Function closes it out.</summary>
    Task<OperationResult> MarkTradeCompletedAsync(string chatId, CancellationToken ct = default);
}
