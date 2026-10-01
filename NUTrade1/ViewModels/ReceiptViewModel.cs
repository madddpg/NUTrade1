using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUTrade1.Core;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>Shows one receipt and lets the student save or share that same text.</summary>
public partial class ReceiptViewModel : BaseViewModel, IQueryAttributable
{
    public const string ReceiptKey = "receipt";

    private readonly IReceiptFile _files;
    private readonly INavigationService _nav;

    public ReceiptViewModel(IReceiptFile files, INavigationService nav)
    {
        _files = files;
        _nav = nav;
        Title = "Receipt";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Body))]
    [NotifyPropertyChangedFor(nameof(AmountDisplay))]
    [NotifyPropertyChangedFor(nameof(WhenDisplay))]
    private Receipt? _receipt;

    public string Body => Receipt?.ToText() ?? string.Empty;
    public string AmountDisplay => Receipt?.AmountDisplay ?? string.Empty;
    public string WhenDisplay => Receipt?.WhenDisplay ?? string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(ReceiptKey, out var value) && value is Receipt receipt)
            Receipt = receipt;
    }

    public static Task OpenAsync(INavigationService nav, Receipt receipt) =>
        nav.GoToAsync(Routes.Receipt, new Dictionary<string, object> { [ReceiptKey] = receipt });

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Receipt is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _files.SaveAsync(Receipt.ToText(), Receipt.FileName);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Error ?? "Couldn't save the receipt.";
                return;
            }
            InfoMessage = result.Value ?? "Receipt saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (Receipt is null || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _files.ShareAsync(Receipt.ToText(), Receipt.FileName, Receipt.Title);
            if (!result.Succeeded)
                ErrorMessage = result.Error ?? "Couldn't share the receipt.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CloseAsync() => _nav.GoBackAsync();
}
