namespace MintPlayer.ObservableCollection.Events.EventArgs
{
    public class ItemPropertyChangedEventArgs<TItem>
    {
        public ItemPropertyChangedEventArgs(TItem item, string propertyName)
        {
            Item = item;
            PropertyName = propertyName;
        }

        public TItem Item { get; }
        public string PropertyName { get; }
    }
}
