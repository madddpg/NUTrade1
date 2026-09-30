using System.Text.Json;
using NUTrade1.Core;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// <see cref="IChatService"/> over <c>chats/{chatId}</c> and its <c>messages</c>
/// subcollection.
///
/// A chat room is never created by this class — <c>awardListing</c> opens one when an
/// auction is won, which is why the chat document itself is read-only to clients.
/// Participants may append their own messages; <c>onChatMessageCreated</c> mirrors the
/// latest one onto the parent for the chat-list preview, and only a Function may post
/// as <c>system</c> or close the trade out.
/// </summary>
public sealed class FirestoreChatService : IChatService
{
    /// <summary>Messages poll faster than the rest — a conversation at 5s intervals feels broken.</summary>
    private static readonly TimeSpan MessagePollInterval = TimeSpan.FromSeconds(2);

    private readonly FirestoreClient _firestore;
    private readonly FunctionsClient _functions;
    private readonly IAuthService _auth;

    public FirestoreChatService(FirestoreClient firestore, FunctionsClient functions, IAuthService auth)
    {
        _firestore = firestore;
        _functions = functions;
        _auth = auth;
    }

    public async Task<IReadOnlyList<Chat>> GetMyChatsAsync(CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return Array.Empty<Chat>();

        var query = Q.Build(
            Q.From("chats"),
            Q.ArrayContains("participantUids", Fs.Str(uid)),
            new[] { Q.OrderBy("lastMessageAt", descending: true) },
            limit: 50);

        var documents = await _firestore.RunQueryAsync(string.Empty, query, ct);
        return documents.Select(Map).ToArray();
    }

    public async Task<Chat?> GetChatAsync(string chatId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chatId)) return null;
        var document = await _firestore.GetDocumentAsync($"chats/{chatId}", ct);
        return document is { } doc ? Map(doc) : null;
    }

    public async Task<OperationResult> SendMessageAsync(
        string chatId, string text, CancellationToken ct = default)
    {
        if (_auth.CurrentUid is not { } uid) return OperationResult.Fail("Sign in to continue.");

        var trimmed = text.Trim();
        if (trimmed.Length == 0) return OperationResult.Fail("Type a message first.");
        if (trimmed.Length > 2000) return OperationResult.Fail("That message is too long.");

        var fields = new Dictionary<string, object?>
        {
            ["senderUid"] = Fs.Str(uid),
            ["text"] = Fs.Str(trimmed),
            ["type"] = FsLower.Enum(ChatMessageType.Text),
            ["sentAt"] = Fs.Ts(DateTimeOffset.UtcNow),
        };

        try
        {
            await _firestore.CreateDocumentAsync($"chats/{chatId}/messages", null, fields, ct);
            return OperationResult.Ok();
        }
        catch (FirestoreException ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    public IDisposable ObserveMessages(string chatId, Action<IReadOnlyList<ChatMessage>> onChanged) =>
        new PollingObserver<IReadOnlyList<ChatMessage>>(
            read: ct => ReadMessagesAsync(chatId, ct),
            signature: messages => $"{messages.Count}:{(messages.Count > 0 ? messages[^1].Id : string.Empty)}",
            onChanged: onChanged,
            interval: MessagePollInterval);

    public async Task<OperationResult> MarkTradeCompletedAsync(string chatId, CancellationToken ct = default)
    {
        var result = await _functions.CallAsync("markTradeCompleted", new { chatId }, ct);
        return result.Succeeded ? OperationResult.Ok() : OperationResult.Fail(result.Error!);
    }

    private async Task<IReadOnlyList<ChatMessage>> ReadMessagesAsync(string chatId, CancellationToken ct)
    {
        var query = Q.Build(
            Q.From("messages"),
            orderBy: new[] { Q.OrderBy("sentAt") },
            limit: 200);

        var documents = await _firestore.RunQueryAsync($"chats/{chatId}", query, ct);
        return documents.Select(MapMessage).ToArray();
    }

    internal static Chat Map(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new Chat
        {
            Id = Fs.IdFromName(document),
            ListingId = Fs.StringOr(fields, "listingId"),
            ListingTitle = Fs.StringOr(fields, "listingTitle"),
            ParticipantUids = Fs.StringList(fields, "participantUids"),
            SellerUid = Fs.StringOr(fields, "sellerUid"),
            BuyerUid = Fs.StringOr(fields, "buyerUid"),
            WinningBidCentavos = Fs.Long(fields, "winningBidCentavos"),
            CompletedBy = Fs.StringList(fields, "completedBy"),
            LastMessage = Fs.StringOr(fields, "lastMessage"),
            LastMessageAt = Fs.Timestamp(fields, "lastMessageAt"),
            Status = Fs.Enum(fields, "status", ChatStatus.Active),
        };
    }

    internal static ChatMessage MapMessage(JsonElement document)
    {
        var fields = document.GetProperty("fields");
        return new ChatMessage
        {
            Id = Fs.IdFromName(document),
            SenderUid = Fs.StringOr(fields, "senderUid"),
            Text = Fs.StringOr(fields, "text"),
            SentAt = Fs.Timestamp(fields, "sentAt") ?? DateTimeOffset.UtcNow,
            Type = Fs.Enum(fields, "type", ChatMessageType.Text),
        };
    }
}
