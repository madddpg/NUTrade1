using CommunityToolkit.Mvvm.ComponentModel;
using NUTrade1.Services;

namespace NUTrade1.ViewModels;

/// <summary>Common state for every ViewModel: a title, a busy flag and the three message channels.</summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// The screen's content is on its way and it shows its skeleton instead. Kept apart
    /// from <see cref="IsBusy"/>, which actions set too: approving a bid or cashing out
    /// must not blank the screen into a skeleton.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    partial void OnIsLoadingChanged(bool value) => OnIsLoadingChangedCore(value);

    /// <summary>Hook for a ViewModel whose own state depends on loading, e.g. its empty state.</summary>
    protected virtual void OnIsLoadingChangedCore(bool value) { }

    /// <summary>
    /// Bound to a RefreshView's IsRefreshing. A pull sets it; the reload then shows the
    /// skeleton, so the ViewModel clears it straight away and the platform spinner goes.
    /// </summary>
    [ObservableProperty] private bool _isRefreshing;

    // ---- Messages ------------------------------------------------------------
    // These used to live on each ViewModel and were drawn as a coloured label wedged
    // into whichever form raised them, so the same failure looked different on every
    // screen and could scroll out of sight. They now live here, and setting any of them
    // slides a toast in at the top-centre of the screen instead — one place, one
    // treatment, wherever the message comes from.
    //
    // Every existing `ErrorMessage = "..."` still works untouched, and the blanket
    // `ErrorMessage = null` that commands run before their work is a no-op, because
    // Toaster ignores empty messages.

    /// <summary>Something failed. Shown in red, and held on screen longer than the rest.</summary>
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Something worked. Shown in green.</summary>
    [ObservableProperty] private string? _infoMessage;

    /// <summary>Progress worth reporting but not a failure — an admin decision landing, say.</summary>
    [ObservableProperty] private string? _statusMessage;

    partial void OnErrorMessageChanged(string? value)
    {
        Toaster.Error(value);
        OnErrorMessageChangedCore(value);
    }

    partial void OnInfoMessageChanged(string? value) => Toaster.Success(value);
    partial void OnStatusMessageChanged(string? value) => Toaster.Success(value);

    /// <summary>Hook for a ViewModel with state of its own that depends on the error.</summary>
    protected virtual void OnErrorMessageChangedCore(string? value) { }

    /// <summary>Called by the page in <c>OnAppearing</c>. Override to load data.</summary>
    public virtual Task OnAppearingAsync() => Task.CompletedTask;

    /// <summary>Called by the page in <c>OnDisappearing</c>. Override to detach listeners.</summary>
    public virtual Task OnDisappearingAsync() => Task.CompletedTask;
}
