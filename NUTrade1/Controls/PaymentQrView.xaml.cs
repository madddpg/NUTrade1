using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.Controls;

/// <summary>
/// Draws a PayMongo QR Ph code and lets the student save or share it.
///
/// Scanning only works when the QR is on a second screen. A student paying from the phone
/// that shows it has to get the picture into GCash or Maya, whose "Upload QR" reads from the
/// photo library — so Save writes it there, and Share hands it to any app directly.
/// </summary>
public partial class PaymentQrView : ContentView
{
    // Only for the rare hosted-image case; PayMongo normally sends the QR inline.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static readonly BindableProperty ImageUrlProperty = BindableProperty.Create(
        nameof(ImageUrl), typeof(string), typeof(PaymentQrView), propertyChanged: OnImageChanged);

    public static readonly BindableProperty ImageBase64Property = BindableProperty.Create(
        nameof(ImageBase64), typeof(string), typeof(PaymentQrView), propertyChanged: OnImageChanged);

    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(
        nameof(IsLoading), typeof(bool), typeof(PaymentQrView), false, propertyChanged: OnImageChanged);

    /// <summary>A code is being minted: the QR shows in bones until it arrives.</summary>
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public static readonly BindableProperty FileNameProperty = BindableProperty.Create(
        nameof(FileName), typeof(string), typeof(PaymentQrView), "nutrade-qr");

    private byte[]? _png;
    private bool _working;

    public PaymentQrView()
    {
        InitializeComponent();
    }

    /// <summary>PayMongo's <c>code.image_url</c> — usually a data URI, occasionally a web address.</summary>
    public string? ImageUrl
    {
        get => (string?)GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    /// <summary>PayMongo's <c>code.image</c>, bare base64, used when there is no data URI.</summary>
    public string? ImageBase64
    {
        get => (string?)GetValue(ImageBase64Property);
        set => SetValue(ImageBase64Property, value);
    }

    /// <summary>What the saved picture is called, so a student can find it in their gallery.</summary>
    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    private static void OnImageChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((PaymentQrView)bindable).Render();

    private void Render()
    {
        _png = QrImageData.TryDecode(ImageUrl, ImageBase64);
        var remote = _png is null ? QrImageData.RemoteUri(ImageUrl) : null;

        if (_png is { } bytes)
            QrImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        else if (remote is not null)
            QrImage.Source = ImageSource.FromUri(remote);
        else
            QrImage.Source = null;

        var hasQr = _png is not null || remote is not null;
        LoadingBones.IsActive = IsLoading;
        Placeholder.IsVisible = !hasQr && !IsLoading;
        Actions.IsVisible = hasQr;
        Hint.IsVisible = hasQr;
    }

    private async void OnSaveClicked(object? sender, EventArgs e) =>
        await RunAsync(async (saver, png) =>
        {
            var result = await saver.SaveToGalleryAsync(png, FileName);
            if (result.Succeeded) Toaster.Success($"QR code saved to {result.Value}.");
            else Toaster.Error(result.Error);
        });

    private async void OnShareClicked(object? sender, EventArgs e) =>
        await RunAsync(async (saver, png) =>
        {
            var result = await saver.ShareAsync(png, FileName, "Pay with QR Ph");
            if (!result.Succeeded) Toaster.Error(result.Error);
        });

    /// <summary>One action at a time, with the buttons off while it runs, so a double tap saves once.</summary>
    private async Task RunAsync(Func<IQrImageSaver, byte[], Task> action)
    {
        if (_working) return;
        var saver = Handler?.MauiContext?.Services.GetService<IQrImageSaver>();
        if (saver is null) return;

        _working = true;
        SaveButton.IsEnabled = ShareButton.IsEnabled = false;
        try
        {
            var png = _png ?? await DownloadAsync();
            if (png is null)
            {
                Toaster.Error("The QR code isn't ready yet.");
                return;
            }
            await action(saver, png);
        }
        finally
        {
            _working = false;
            SaveButton.IsEnabled = ShareButton.IsEnabled = true;
        }
    }

    private async Task<byte[]?> DownloadAsync()
    {
        if (QrImageData.RemoteUri(ImageUrl) is not { } uri) return null;
        try
        {
            return _png = await Http.GetByteArrayAsync(uri);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
