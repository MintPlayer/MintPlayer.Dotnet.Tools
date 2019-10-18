namespace MintPlayer.ObservableCollection.Events.EventArgs
{
    public class ItemPropertyChangedEventArgs<TItem>
    {
        public ItemPropertyChangedEventArgs(TItem item, string propertyName/*, object oldValue, object newValue*/)
        {
            Item = item;
            PropertyName = propertyName;
            //OldValue = oldValue;
            //NewValue = newValue;
        }

        public TItem Item { get; }
        public string PropertyName { get; }
        //public object OldValue { get; }
        //public object NewValue { get; }
    }
}
