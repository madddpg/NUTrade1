using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NUTrade1.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be refilled in one notification.
///
/// <c>Clear()</c> followed by an <c>Add</c> per item raises N+1 CollectionChanged events,
/// and a bound <see cref="Microsoft.Maui.Controls.CollectionView"/> re-measures its layout
/// on each one — which is what makes a feed refresh visibly stutter on a phone once the
/// list is a couple of pages deep. <see cref="ReplaceAll"/> mutates the backing list and
/// raises a single Reset instead.
/// </summary>
public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items) Items.Add(item);

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
