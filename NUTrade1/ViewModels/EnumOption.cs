namespace NUTrade1.ViewModels;

/// <summary>Label/value pair backing a dropdown <see cref="Microsoft.Maui.Controls.Picker"/> bound to an enum.</summary>
public sealed class EnumOption<TEnum> where TEnum : struct, Enum
{
    public required string Label { get; init; }
    public required TEnum Value { get; init; }
}
