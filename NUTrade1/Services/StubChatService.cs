using NUTrade1.Core;

namespace NUTrade1.Services;

/// <summary>In-memory <see cref="IChatService"/> stand-in; real-time chat lands in Phase 5.</summary>
public sealed class StubChatService : IChatService
{
    private readonly Dictionary<string, List<ChatMessage>> _messages = new();
    private readonly List<Chat> _chats = new();

    public Task<IReadOnlyList<Chat>> GetMyChatsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Chat>>(_chats.ToArray());

    public Task<Chat?> GetChatAsync(string chatId, CancellationToken ct = default) =>
        Task.FromResult(_chats.FirstOrDefault(c => c.Id == chatId));

    public Task<OperationResult> SendMessageAsync(string chatId, string text, CancellationToken ct = default)
    {
        if (!_messages.TryGetValue(chatId, out var list))
            _messages[chatId] = list = new List<ChatMessage>();

        list.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            Text = text,
            SentAt = DateTimeOffset.UtcNow,
            Type = ChatMessageType.Text,
        });
        return Task.FromResult(OperationResult.Ok());
    }

    public IDisposable ObserveMessages(string chatId, Action<IReadOnlyList<ChatMessage>> onChanged)
    {
        onChanged(_messages.TryGetValue(chatId, out var list) ? list.ToArray() : Array.Empty<ChatMessage>());
        return new NoopDisposable();
    }

    public Task<OperationResult> MarkTradeCompletedAsync(string chatId, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Ok());

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
