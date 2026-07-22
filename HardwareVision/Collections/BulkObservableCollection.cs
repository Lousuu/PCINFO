using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;

namespace HardwareVision.Collections;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    public bool ReplaceAll(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        dispatcher.VerifyAccess();

        if (Count == items.Count)
        {
            bool unchanged = true;
            for (int index = 0; index < items.Count; index++)
            {
                if (!ReferenceEquals(this[index], items[index]))
                {
                    unchanged = false;
                    break;
                }
            }

            if (unchanged)
            {
                return false;
            }
        }

        CheckReentrancy();
        Items.Clear();
        for (int index = 0; index < items.Count; index++)
        {
            Items.Add(items[index]);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return true;
    }

    protected override void ClearItems()
    {
        dispatcher.VerifyAccess();
        base.ClearItems();
    }

    protected override void InsertItem(int index, T item)
    {
        dispatcher.VerifyAccess();
        base.InsertItem(index, item);
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        dispatcher.VerifyAccess();
        base.MoveItem(oldIndex, newIndex);
    }

    protected override void RemoveItem(int index)
    {
        dispatcher.VerifyAccess();
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, T item)
    {
        dispatcher.VerifyAccess();
        base.SetItem(index, item);
    }
}
