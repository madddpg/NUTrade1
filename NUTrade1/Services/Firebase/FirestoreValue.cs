using System.Globalization;
using System.Text.Json;

namespace NUTrade1.Services.Firebase;

/// <summary>
/// Conversion between .NET values and Firestore's REST wire format, where every
/// field is wrapped in a one-key type envelope — <c>{"stringValue":"x"}</c>,
/// <c>{"integerValue":"42"}</c> (a string, not a number — 64-bit ints do not survive
/// JSON), <c>{"timestampValue":"...Z"}</c>, and so on.
///
/// Writers return dictionaries ready to serialize; readers take a document's
/// <c>fields</c> element and never throw on a missing or unexpectedly typed field,
/// because a document written by an older build of the app must still render.
/// </summary>
public static class Fs
{
    // ---- Writers -------------------------------------------------------------

    public static Dictionary<string, object?> Str(string? value) =>
        value is null ? Null() : new Dictionary<string, object?> { ["stringValue"] = value };

    public static Dictionary<string, object?> Int(long value) =>
        new() { ["integerValue"] = value.ToString(CultureInfo.InvariantCulture) };

    public static Dictionary<string, object?> Int(long? value) => value is { } v ? Int(v) : Null();

    public static Dictionary<string, object?> Dbl(double value) =>
        new() { ["doubleValue"] = value };

    public static Dictionary<string, object?> Bool(bool value) =>
        new() { ["booleanValue"] = value };

    public static Dictionary<string, object?> Ts(DateTimeOffset value) =>
        new() { ["timestampValue"] = value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) };

    public static Dictionary<string, object?> Ts(DateTimeOffset? value) => value is { } v ? Ts(v) : Null();

    public static Dictionary<string, object?> Null() =>
        new() { ["nullValue"] = null };

    public static Dictionary<string, object?> Arr(IEnumerable<string> values) =>
        new()
        {
            ["arrayValue"] = new Dictionary<string, object?>
            {
                ["values"] = values.Select(v => (object)Str(v)).ToArray(),
            },
        };

    /// <summary>Enums travel as their member name, matching the strings the Functions write.</summary>
    public static Dictionary<string, object?> Enum<T>(T value) where T : struct, System.Enum =>
        Str(value.ToString());

    // ---- Readers -------------------------------------------------------------

    private static JsonElement? Field(JsonElement fields, string name) =>
        fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out var value)
            ? value
            : null;

    public static string? String(JsonElement fields, string name) =>
        Field(fields, name) is { } v && v.TryGetProperty("stringValue", out var s)
            ? s.GetString()
            : null;

    public static string StringOr(JsonElement fields, string name, string fallback = "") =>
        String(fields, name) ?? fallback;

    public static long? LongOrNull(JsonElement fields, string name)
    {
        if (Field(fields, name) is not { } v) return null;

        if (v.TryGetProperty("integerValue", out var i))
        {
            return i.ValueKind == JsonValueKind.String
                ? long.TryParse(i.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null
                : i.TryGetInt64(out var direct) ? direct : null;
        }

        // Firestore hands back a whole number written as a double as doubleValue.
        return v.TryGetProperty("doubleValue", out var d) && d.TryGetDouble(out var dv)
            ? (long)dv
            : null;
    }

    public static long Long(JsonElement fields, string name, long fallback = 0) =>
        LongOrNull(fields, name) ?? fallback;

    public static int Int32(JsonElement fields, string name, int fallback = 0) =>
        (int)Long(fields, name, fallback);

    public static double? DoubleOrNull(JsonElement fields, string name)
    {
        if (Field(fields, name) is not { } v) return null;
        if (v.TryGetProperty("doubleValue", out var d) && d.TryGetDouble(out var dv)) return dv;
        return LongOrNull(fields, name);
    }

    public static bool Bool(JsonElement fields, string name, bool fallback = false) =>
        Field(fields, name) is { } v && v.TryGetProperty("booleanValue", out var b)
            ? b.ValueKind == JsonValueKind.True
            : fallback;

    public static DateTimeOffset? Timestamp(JsonElement fields, string name)
    {
        if (Field(fields, name) is not { } v) return null;
        if (!v.TryGetProperty("timestampValue", out var t)) return null;

        return DateTimeOffset.TryParse(
            t.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    public static List<string> StringList(JsonElement fields, string name)
    {
        var result = new List<string>();
        if (Field(fields, name) is not { } v) return result;
        if (!v.TryGetProperty("arrayValue", out var array)) return result;
        if (!array.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in values.EnumerateArray())
        {
            if (item.TryGetProperty("stringValue", out var s) && s.GetString() is { } str)
                result.Add(str);
        }
        return result;
    }

    /// <summary>
    /// Parses an enum member name. An unrecognised value (a status written by a newer
    /// backend, say) falls back rather than throwing, so one odd document can't blank
    /// out a whole feed page.
    /// </summary>
    public static T Enum<T>(JsonElement fields, string name, T fallback) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(String(fields, name), ignoreCase: true, out var parsed) ? parsed : fallback;

    /// <summary>Last path segment of a document's <c>name</c> — its document id.</summary>
    public static string IdFromName(JsonElement document) =>
        document.TryGetProperty("name", out var name) && name.GetString() is { } path
            ? path[(path.LastIndexOf('/') + 1)..]
            : string.Empty;
}

/// <summary>
/// The backend is TypeScript and spells three of the enums in lower case on the wire
/// — bid status, chat status and message type. <c>firestore.rules</c> matches those
/// literals exactly (a message must have <c>type == 'text'</c>), so a write that used
/// the .NET member name verbatim would be rejected.
/// </summary>
public static class FsLower
{
    public static Dictionary<string, object?> Enum<T>(T value) where T : struct, System.Enum =>
        Fs.Str(value.ToString()!.ToLowerInvariant());
}
