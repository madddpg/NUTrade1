using System.Globalization;
using System.Text;
using NUTrade1.Core;

namespace NUTrade1.Converters;

/// <summary>PascalCase / enum member → spaced words. <c>LikeNew</c> → "Like New".</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        var s = value.ToString() ?? string.Empty;
        if (s is "Unknown" or "None") return parameter as string ?? string.Empty;
        return Humanize(s);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Humanize(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>A display name → its first initial (or two), uppercased, for an avatar badge.</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        if (string.IsNullOrWhiteSpace(name)) return parameter as string ?? "NU";

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}"
            : parts[0][..Math.Min(2, parts[0].Length)];
        return initials.ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The line under a student's name. Bound to a whole <see cref="UserProfile"/>, the
/// program picked at registration wins; accounts from before that fall back to their
/// <see cref="CampusHub"/>. Bound to a <see cref="CampusHub"/>, just its label.
/// </summary>
public sealed class CampusHubProgramConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        UserProfile { Program.Length: > 0 } profile => profile.Program,
        UserProfile profile => Convert(profile.CampusHub, targetType, parameter, culture),
        CampusHub.Engineering => "BS Engineering",
        CampusHub.InformationTechnology => "BS Information Technology",
        CampusHub.Business => "BS Business Administration",
        CampusHub.Education => "Bachelor of Elementary Education",
        CampusHub.Nursing => "BS Nursing",
        CampusHub.ArtsAndSciences => "BS Arts & Sciences",
        _ => "NU–Lipa student",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary><see cref="DateTimeOffset"/>? → "just now" / "18h ago" / "3d ago".</summary>
public sealed class TimeAgoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset when)
        {
            if (value is DateTime dt) when = new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero);
            else return string.Empty;
        }

        var delta = DateTimeOffset.UtcNow - when;
        if (delta < TimeSpan.FromMinutes(1)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m ago";
        if (delta < TimeSpan.FromDays(1)) return $"{(int)delta.TotalHours}h ago";
        if (delta < TimeSpan.FromDays(7)) return $"{(int)delta.TotalDays}d ago";
        return when.ToLocalTime().ToString("MMM d", culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a bool.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value is null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>True when the value is a non-empty string / non-null object / non-empty collection.</summary>
public sealed class HasValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        return parameter is "invert" ? !has : has;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>value.Equals(parameter) → bool, round-tripping the parameter on set (for pill radio groups).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (t.IsEnum) return Enum.Parse(t, parameter.ToString()!, ignoreCase: true);
            return parameter;
        }
        return Binding.DoNothing;
    }
}

/// <summary>First entry of a string list, or null. Handy for a listing's cover photo.</summary>
public sealed class FirstOrDefaultConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable<string> items ? items.FirstOrDefault() : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Centavos (long) → "₱10.00".</summary>
public sealed class CentavosConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long c ? Money.ToDisplay(c) : value is int i ? Money.ToDisplay(i) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Count / int → true when greater than zero (drives notification badges). Pass "invert" for the empty case.</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var any = value is int i ? i > 0 : value is System.Collections.ICollection c && c.Count > 0;
        return parameter is "invert" ? !any : any;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True when the bound value is null; round-trips null when set true (for an "All" filter chip).</summary>
public sealed class IsNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? null : Binding.DoNothing;
}

/// <summary>
/// Maps a bool to one of two preset objects (colors, strings, styles). Set
/// <see cref="TrueObject"/> / <see cref="FalseObject"/> on the converter instance.
/// A non-bool value is treated as truthy when non-null.
/// </summary>
public sealed class BoolToObjectConverter : IValueConverter
{
    public object? TrueObject { get; set; }
    public object? FalseObject { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b ? b : value is not null) ? TrueObject : FalseObject;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, TrueObject);
}

/// <summary><see cref="ListingStatus"/> → the seller-facing status line on My listings.</summary>
public sealed class ListingStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ListingStatus.Draft => "● Not posted yet — tap to finish",
        ListingStatus.PendingPayment => "● Waiting for payment — tap to check",
        ListingStatus.PendingApproval => "● Pending admin approval",
        ListingStatus.Active => "● Accepting bids",
        ListingStatus.Matched => "● Matched with a buyer",
        ListingStatus.Completed => "● Sold",
        ListingStatus.Expired => "● Ended",
        ListingStatus.Cancelled => "● Cancelled",
        ListingStatus.Rejected => "● Not approved",
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// <see cref="ListingStatus"/> → the colour of its status line: <see cref="Live"/> for an
/// auction taking bids, <see cref="Waiting"/> for anything not yet live, and
/// <see cref="Closed"/> for everything that has finished or been turned down. Each has a
/// <c>*Dark</c> twin picked when the app runs in dark mode — an AppThemeBinding cannot be
/// set on a converter's plain CLR properties, so the theme is resolved here instead.
/// </summary>
public sealed class ListingStatusColorConverter : IValueConverter
{
    public Color Live { get; set; } = Colors.Green;
    public Color LiveDark { get; set; } = Colors.LightGreen;
    public Color Waiting { get; set; } = Colors.DarkGoldenrod;
    public Color WaitingDark { get; set; } = Colors.Goldenrod;
    public Color Closed { get; set; } = Colors.Gray;
    public Color ClosedDark { get; set; } = Colors.LightGray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return value switch
        {
            ListingStatus.Active => dark ? LiveDark : Live,
            ListingStatus.Draft or ListingStatus.PendingPayment or ListingStatus.PendingApproval => dark ? WaitingDark : Waiting,
            _ => dark ? ClosedDark : Closed,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
