namespace NUTrade1.Core;

/// <summary>
/// Maps to <c>chats/{chatId}</c>. Created by a Cloud Function when an auction is won —
/// never by the client, which is why the document is read-only in firestore.rules.
/// </summary>
public sealed class Chat
{
    public string Id { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    /// <summary>Denormalized so the chat list can name the item without reading the listing.</summary>
    public string ListingTitle { get; set; } = string.Empty;

    /// <summary>Exactly the two trading parties' UIDs.</summary>
    public IList<string> ParticipantUids { get; set; } = new List<string>();

    public string SellerUid { get; set; } = string.Empty;

    public string BuyerUid { get; set; } = string.Empty;

    /// <summary>The bid that won the auction, in centavos.</summary>
    public long WinningBidCentavos { get; set; }

    /// <summary>
    /// UIDs that have confirmed the handover. The trade only closes when both are
    /// present — one tap must not be able to credit the other party with a completed
    /// trade that never happened.
    /// </summary>
    public IList<string> CompletedBy { get; set; } = new List<string>();

    public string LastMessage { get; set; } = string.Empty;

    public DateTimeOffset? LastMessageAt { get; set; }

    public ChatStatus Status { get; set; } = ChatStatus.Active;

    /// <summary>True once both sides confirmed and a Function archived the room.</summary>
    public bool IsClosed => Status == ChatStatus.Archived;

    public bool HasConfirmed(string? uid) =>
        uid is not null && CompletedBy.Contains(uid);

    /// <summary>The other party's UID, from the perspective of <paramref name="uid"/>.</summary>
    public string? PartnerUid(string? uid) =>
        uid is null ? null : ParticipantUids.FirstOrDefault(p => p != uid);
}
