namespace NUTrade1.ViewModels;

/// <summary>
/// Sent once a reviewed listing has been saved as a draft and handed to payment. The Post
/// tab clears its form on it, so the next listing starts blank rather than as a copy of the
/// last one waiting to be posted twice.
/// </summary>
public sealed record ListingSubmittedMessage(string ListingId);
