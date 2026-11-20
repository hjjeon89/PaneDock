using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PaneDock.Core.Services;

/// <summary>One Reset notification per loaded batch instead of one per file.</summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CheckReentrancy();
        var added = false;
        foreach (var entry in entries)
        {
            Items.Add(entry);
            added = true;
        }
        if (!added) return;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
