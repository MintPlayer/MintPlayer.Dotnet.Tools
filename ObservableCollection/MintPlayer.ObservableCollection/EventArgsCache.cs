using System.Collections.Specialized;
using System.ComponentModel;

namespace MintPlayer.ObservableCollection
{
    internal static class EventArgsCache
    {
        internal static readonly PropertyChangedEventArgs CountPropertyChangedEventArgs = new PropertyChangedEventArgs("Count");
        internal static readonly PropertyChangedEventArgs IndexerPropertyChangedEventArgs = new PropertyChangedEventArgs("Item[]");
        internal static readonly NotifyCollectionChangedEventArgs ResetCollectionChangedEventArgs = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
    }
}
