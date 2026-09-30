namespace NUTrade1.Core;

/// <summary>A lightweight success/failure wrapper so services can report errors without throwing across layers.</summary>
public readonly record struct OperationResult(bool Succeeded, string? Error = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>An <see cref="OperationResult"/> that also carries a value on success.</summary>
public readonly record struct OperationResult<T>(bool Succeeded, T? Value = default, string? Error = null)
{
    public static OperationResult<T> Ok(T value) => new(true, value);
    public static OperationResult<T> Fail(string error) => new(false, default, error);
}
