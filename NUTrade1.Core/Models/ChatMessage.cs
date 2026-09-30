namespace NUTrade1.Core;

/// <summary>Maps to <c>chats/{chatId}/messages/{msgId}</c>.</summary>
public sealed class ChatMessage
{
    public string Id { get; set; } = string.Empty;

    public string SenderUid { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }

    public ChatMessageType Type { get; set; } = ChatMessageType.Text;
}
