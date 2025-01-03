using MintPlayer.ObservableCollection.Events.EventArgs;

namespace MintPlayer.ObservableCollection.Events.EventHandlers;

public delegate void ItemPropertyChangedEventHandler<TItem>(object sender, ItemPropertyChangedEventArgs<TItem> e);
