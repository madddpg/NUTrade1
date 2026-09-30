namespace NUTrade1.Core;

/// <summary>
/// Cross-cutting constants for the NUTrade domain. Values that the client and the
/// Cloud Functions must agree on live here so there is a single source of truth.
/// </summary>
public static class NUTradeConstants
{
    /// <summary>Posting fee for every listing after a student's free first one, in centavos (₱10.00).</summary>
    public const long AdditionalFeeCentavos = 1_000;

    /// <summary>Featured Priority Pin upgrade fee, in centavos (₱20.00).</summary>
    public const long PriorityFeeCentavos = 2_000;

    /// <summary>
    /// Fallback when a listing-fee response has no expiry. Live codes use the time
    /// PayMongo sends, which the payment screen counts down.
    /// </summary>
    public const int QrExpiryMinutes = 10;

    /// <summary>
    /// How long an unpaid bid deposit stays open. Mirrors DEPOSIT_INTENT_EXPIRY_MINUTES.
    /// The bid QR countdown follows the expiry on the deposit, not this constant.
    /// </summary>
    public const int BidDepositWindowMinutes = 30;

    /// <summary>A listing stuck in <c>pending_payment</c> longer than this is expired by a scheduled Function.</summary>
    public const int PendingListingExpiryHours = 24;

    /// <summary>
    /// The commitment deposit held to place a bid, as a percentage of it. Mirrors
    /// BID_DEPOSIT_PERCENT in functions/src/constants.ts — the server is the authority,
    /// this is only for telling the student before they tap.
    /// </summary>
    public const int BidDepositPercent = 15;

    /// <summary>Minimum / maximum photos per listing ("Add up to 4 photos"). Mirrored by MAX_LISTING_PHOTOS.</summary>
    public const int MinListingPhotos = 1;
    public const int MaxListingPhotos = 4;

    /// <summary>
    /// Every auction runs this long. There is no duration picker — sellers choose a
    /// package, not a length. Mirrored by AUCTION_DURATION_HOURS in functions/src/constants.ts.
    /// </summary>
    public const int AuctionDurationHours = 24;

    /// <summary>How long a winning bidder has to pay before the bid is released.</summary>
    public const int OrderPaymentWindowHours = 24;

    /// <summary>
    /// The programs a student picks from at registration, stored as-is in
    /// <c>users/{uid}.program</c>. Mirrored by PROGRAMS in functions/src/constants.ts,
    /// which is what completeSignup accepts, and by programs() in firestore.rules, which
    /// is what a student may set on their own profile.
    /// </summary>
    public static readonly IReadOnlyList<string> Programs = ["SACE", "SAHS", "SABM", "SHS"];

    /// <summary>
    /// Whether a listing in <paramref name="status"/> has used its owner's one free post.
    /// The Free package is a student's first listing, once ever: anything submitted and
    /// not turned down counts — waiting for approval, live, or finished in any way. A
    /// rejected listing gives the free post back so a refused first attempt can be fixed
    /// and resubmitted for free; drafts and unpaid listings never count.
    /// Mirrored by FREE_POST_USED_STATUSES in functions/src/constants.ts, which is what
    /// createQrPayment enforces — the app only uses this to hide the option.
    /// </summary>
    public static bool UsesFreePost(ListingStatus status) => status is
        ListingStatus.PendingApproval or
        ListingStatus.Active or
        ListingStatus.Matched or
        ListingStatus.Completed or
        ListingStatus.Expired;

    public static long FeeForPackage(ListingPackage package) => package switch
    {
        ListingPackage.Free => 0,
        ListingPackage.Additional => AdditionalFeeCentavos,
        ListingPackage.Priority => PriorityFeeCentavos,
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "Unknown listing package"),
    };

    /// <summary>
    /// Cheap shape check before a sign-up request is sent. Students register with any
    /// personal email — there is no campus-domain restriction — so the only thing worth
    /// catching client-side is an obvious typo. Ownership of the address is established
    /// by the emailed one-time code, and being an actual NU student by ID review.
    /// </summary>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@')) return false;

        var domain = trimmed[(at + 1)..];
        return domain.Length >= 3
            && domain.Contains('.')
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && !trimmed.Contains(' ');
    }
}
