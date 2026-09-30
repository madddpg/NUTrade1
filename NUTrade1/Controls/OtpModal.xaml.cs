using System.Windows.Input;

namespace NUTrade1.Controls;

/// <summary>
/// The six-digit code step, as a modal over whatever screen asked for it: registration,
/// forgot-password and email verification all show the same sheet.
///
/// It is an in-page overlay rather than a pushed modal page so the flow keeps one
/// ViewModel — the code, the cooldown and the error all belong to the wizard that opened
/// it, and a second page would have to hand results back. The host page wraps its content
/// in a Grid and adds this last, so it paints on top:
/// <code>
/// &lt;Grid&gt;
///   &lt;ScrollView&gt;...&lt;/ScrollView&gt;
///   &lt;controls:OtpModal IsOpen="{Binding IsCodeStep}" Code="{Binding Code}" ... /&gt;
/// &lt;/Grid&gt;
/// </code>
/// </summary>
public partial class OtpModal : ContentView
{
    public OtpModal() => InitializeComponent();

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(OtpModal), false,
        propertyChanged: (b, _, v) => ((OtpModal)b).OnOpenChanged((bool)v));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly BindableProperty CodeProperty = BindableProperty.Create(
        nameof(Code), typeof(string), typeof(OtpModal), string.Empty,
        defaultBindingMode: BindingMode.TwoWay);

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(OtpModal), string.Empty);

    /// <summary>Where the code went, in the host's own words.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly BindableProperty VerifyTextProperty = BindableProperty.Create(
        nameof(VerifyText), typeof(string), typeof(OtpModal), "Verify code");

    public string VerifyText
    {
        get => (string)GetValue(VerifyTextProperty);
        set => SetValue(VerifyTextProperty, value);
    }

    public static readonly BindableProperty VerifyCommandProperty = BindableProperty.Create(
        nameof(VerifyCommand), typeof(ICommand), typeof(OtpModal));

    public ICommand? VerifyCommand
    {
        get => (ICommand?)GetValue(VerifyCommandProperty);
        set => SetValue(VerifyCommandProperty, value);
    }

    public static readonly BindableProperty ResendCommandProperty = BindableProperty.Create(
        nameof(ResendCommand), typeof(ICommand), typeof(OtpModal));

    public ICommand? ResendCommand
    {
        get => (ICommand?)GetValue(ResendCommandProperty);
        set => SetValue(ResendCommandProperty, value);
    }

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(ICommand), typeof(OtpModal));

    /// <summary>Runs on the X and on a scrim tap. Usually the host's "one step back".</summary>
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public static readonly BindableProperty ResendTextProperty = BindableProperty.Create(
        nameof(ResendText), typeof(string), typeof(OtpModal), "Send another code");

    public string ResendText
    {
        get => (string)GetValue(ResendTextProperty);
        set => SetValue(ResendTextProperty, value);
    }

    public static readonly BindableProperty CanResendProperty = BindableProperty.Create(
        nameof(CanResend), typeof(bool), typeof(OtpModal), true);

    public bool CanResend
    {
        get => (bool)GetValue(CanResendProperty);
        set => SetValue(CanResendProperty, value);
    }

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(OtpModal), false,
        propertyChanged: (b, _, _) => ((OtpModal)b).OnPropertyChanged(nameof(IsNotBusy)));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>The host's IsNotBusy is on its ViewModel, which this control cannot reach.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Focus the code box when the sheet opens. Deferred to the next dispatcher turn
    /// because the Entry is not yet laid out when IsVisible flips.
    /// </summary>
    private void OnOpenChanged(bool open)
    {
        if (!open) return;
        Dispatcher.Dispatch(() => CodeEntry.Focus());
    }
}
