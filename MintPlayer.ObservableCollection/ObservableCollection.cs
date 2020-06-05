using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using MintPlayer.ObservableCollection.Events.EventHandlers;

namespace MintPlayer.ObservableCollection
{
    public class ObservableCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
    {
        private bool isAddingOrRemovingRange = false;

        public void AddRange(IEnumerable<T> items)
        {
            try
            {
                isAddingOrRemovingRange = true;
                foreach (var item in items)
                    Add(item);
            }
            finally
            {
                isAddingOrRemovingRange = false;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList()));
            }
        }

        public void RemoveRange(IEnumerable<T> items)
        {
            try
            {
                isAddingOrRemovingRange = true;
                foreach (var item in items)
                    Remove(item);
            }
            finally
            {
                isAddingOrRemovingRange = false;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, items.ToList()));
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!isAddingOrRemovingRange)
            {
                // Only invoke the CollectionChanged with all items, not individually
                base.OnCollectionChanged(e);

                // Also only attach the PropertyChanged event handler when we're not into
                // the process of adding a number of items one by one.
                var itf = typeof(T).GetInterfaces();
                var itf_arr = new Type[itf.Length];
                itf.CopyTo(itf_arr, 0);
                if (itf_arr.Contains(typeof(System.ComponentModel.INotifyPropertyChanged)))
                {
                    // First detach all event handlers
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                        {
                            ((System.ComponentModel.INotifyPropertyChanged)item).PropertyChanged -= ObservableCollection_Item_PropertyChanged;
                        }
                    }
                    // Then attach all event handlers
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            ((System.ComponentModel.INotifyPropertyChanged)item).PropertyChanged += ObservableCollection_Item_PropertyChanged;
                        }
                    }
                }
            }
        }

        private void ObservableCollection_Item_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnItemPropertyChanged((T)sender, e.PropertyName);
        }

        #region ItemPropertyChanged
        public event ItemPropertyChangedEventHandler<T> ItemPropertyChanged;
        protected void OnItemPropertyChanged(T item, string propertyName/*, object oldValue, object newValue*/)
        {
            if (ItemPropertyChanged != null)
                ItemPropertyChanged(this, new Events.EventArgs.ItemPropertyChangedEventArgs<T>(item, propertyName/*, oldValue, newValue*/));
        }
        #endregion
    }
}
