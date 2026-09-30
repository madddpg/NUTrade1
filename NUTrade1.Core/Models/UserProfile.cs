namespace NUTrade1.Core;

/// <summary>Maps to <c>users/{uid}</c> in Firestore.</summary>
public sealed class UserProfile
{
    /// <summary>Firebase Auth UID. Also the document id.</summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>"First Last", shown across the app.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Asked for at registration; <see cref="DisplayName"/> is built from these.</summary>
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>No longer collected — registration dropped the Student ID. Kept for older accounts.</summary>
    public string StudentId { get; set; } = string.Empty;

    /// <summary>Any personal email. Ownership is proven by the emailed one-time code.</summary>
    public string Email { get; set; } = string.Empty;

    public CampusHub CampusHub { get; set; } = CampusHub.Unknown;

    /// <summary>Course and year shown under the name on the profile, e.g. "BS Information Technology".</summary>
    public string Program { get; set; } = string.Empty;

    /// <summary>Storage download URL, or null before a photo is set.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Incremented by a Function when a trade completes.</summary>
    public int TradesCompleted { get; set; }

    /// <summary>Average rating out of 5 from completed trades, or null before the first one.</summary>
    public double? Rating { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Push registration tokens (one per signed-in device), read by Cloud Functions to
    /// notify the seller when a listing goes live. Not yet populated by the client —
    /// no Firebase Cloud Messaging plugin is wired into the MAUI app yet.
    /// </summary>
    public List<string> FcmTokens { get; set; } = new();
}
