using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using MintPlayer.ObservableCollection.Events.EventHandlers;
using System.ComponentModel;

namespace MintPlayer.ObservableCollection
{
    public class ObservableCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
    {
        #region Constructors
        public ObservableCollection() : base()
        {
        }

        public ObservableCollection(IEnumerable<T> items) : this()
        {
            InternalAddRange(items);
        }
        #endregion

        #region Private fields

        private bool isAddingOrRemovingRange = false;

        #endregion

        #region Public methods
        public void AddRange(IEnumerable<T> items)
        {
            CheckReentrancy();

            InternalAddRange(items);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Items[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToList()));
        }

        public void RemoveRange(IEnumerable<T> items)
        {
            try
            {
                CheckReentrancy();

                isAddingOrRemovingRange = true;
                foreach (var item in items)
                    Remove(item);
            }
            finally
            {
                isAddingOrRemovingRange = false;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Items[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, items.ToList()));
            }
        }
        #endregion

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!isAddingOrRemovingRange)
            {
                // Only invoke the CollectionChanged with all items, not individually
                base.OnCollectionChanged(e);

                // Also only attach the PropertyChanged event handler when we're not into
                // the process of adding a number of items one by one.
                var itf = typeof(T).GetInterfaces();
                
                if (itf.Contains(typeof(System.ComponentModel.INotifyPropertyChanged)))
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

        #region ItemPropertyChanged
        public event ItemPropertyChangedEventHandler<T> ItemPropertyChanged;
        protected void OnItemPropertyChanged(T item, string propertyName/*, object oldValue, object newValue*/)
        {
            if (ItemPropertyChanged != null)
                ItemPropertyChanged(this, new Events.EventArgs.ItemPropertyChangedEventArgs<T>(item, propertyName/*, oldValue, newValue*/));
        }
        #endregion

        #region Private methods
        private void ObservableCollection_Item_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnItemPropertyChanged((T)sender, e.PropertyName);
        }

        private void InternalAddRange(IEnumerable<T> items)
        {
            try
            {
                isAddingOrRemovingRange = true;
                ((List<T>)Items).InsertRange(Count, items);
            }
            finally
            {
                isAddingOrRemovingRange = false;
            }
        }
        #endregion
    }
}
