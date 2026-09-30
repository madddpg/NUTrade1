namespace NUTrade1.Core;

/// <summary>
/// The posting tier chosen when creating an auction. Every verified student's first
/// active auction is <see cref="Free"/>; any additional concurrent auction requires
/// <see cref="Additional"/>. <see cref="Priority"/> also pins the listing to the top
/// of the feed with a gold badge once the fee is confirmed paid.
/// </summary>
public enum ListingPackage
{
    Free = 0,
    Additional,
    Priority,
}
