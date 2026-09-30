namespace NUTrade1.Services.Firebase;

/// <summary>
/// Builders for Firestore <c>structuredQuery</c> bodies. Plain dictionaries rather
/// than a typed model — the shapes are small, used in one place each, and this keeps
/// them readable next to the REST reference.
/// </summary>
public static class Q
{
    public static Dictionary<string, object?> From(string collectionId, bool allDescendants = false) =>
        new()
        {
            ["collectionId"] = collectionId,
            ["allDescendants"] = allDescendants,
        };

    public static Dictionary<string, object?> Equal(string fieldPath, Dictionary<string, object?> value) =>
        FieldFilter(fieldPath, "EQUAL", value);

    public static Dictionary<string, object?> LessThanOrEqual(string fieldPath, Dictionary<string, object?> value) =>
        FieldFilter(fieldPath, "LESS_THAN_OR_EQUAL", value);

    public static Dictionary<string, object?> ArrayContains(string fieldPath, Dictionary<string, object?> value) =>
        FieldFilter(fieldPath, "ARRAY_CONTAINS", value);

    public static Dictionary<string, object?> In(string fieldPath, IEnumerable<Dictionary<string, object?>> values) =>
        new()
        {
            ["fieldFilter"] = new Dictionary<string, object?>
            {
                ["field"] = new Dictionary<string, object?> { ["fieldPath"] = fieldPath },
                ["op"] = "IN",
                ["value"] = new Dictionary<string, object?>
                {
                    ["arrayValue"] = new Dictionary<string, object?>
                    {
                        ["values"] = values.Cast<object>().ToArray(),
                    },
                },
            },
        };

    private static Dictionary<string, object?> FieldFilter(
        string fieldPath, string op, Dictionary<string, object?> value) =>
        new()
        {
            ["fieldFilter"] = new Dictionary<string, object?>
            {
                ["field"] = new Dictionary<string, object?> { ["fieldPath"] = fieldPath },
                ["op"] = op,
                ["value"] = value,
            },
        };

    /// <summary>Combines filters with AND. A single filter is passed through unwrapped.</summary>
    public static Dictionary<string, object?> And(params Dictionary<string, object?>[] filters) =>
        filters.Length == 1
            ? filters[0]
            : new Dictionary<string, object?>
            {
                ["compositeFilter"] = new Dictionary<string, object?>
                {
                    ["op"] = "AND",
                    ["filters"] = filters.Cast<object>().ToArray(),
                },
            };

    public static Dictionary<string, object?> OrderBy(string fieldPath, bool descending = false) =>
        new()
        {
            ["field"] = new Dictionary<string, object?> { ["fieldPath"] = fieldPath },
            ["direction"] = descending ? "DESCENDING" : "ASCENDING",
        };

    /// <summary>
    /// Assembles the query. <paramref name="offset"/> drives feed paging: Firestore also
    /// supports keyset cursors, but an offset keeps <see cref="Core.ListingPage.NextCursor"/>
    /// a plain string, and a campus feed never runs deep enough for the extra read cost
    /// of skipping to matter.
    /// </summary>
    public static Dictionary<string, object?> Build(
        Dictionary<string, object?> from,
        Dictionary<string, object?>? where = null,
        IEnumerable<Dictionary<string, object?>>? orderBy = null,
        int? limit = null,
        int? offset = null)
    {
        var query = new Dictionary<string, object?>
        {
            ["from"] = new object[] { from },
        };

        if (where is not null) query["where"] = where;
        if (orderBy is not null) query["orderBy"] = orderBy.Cast<object>().ToArray();
        if (limit is { } l) query["limit"] = l;
        if (offset is { } o and > 0) query["offset"] = o;

        return query;
    }
}
