using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using MintPlayer.ObservableCollection.Events.EventHandlers;
using System.ComponentModel;
using System.Reflection;
using System.Diagnostics;

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
        [NonSerialized] private DeferredEventsCollection _deferredEvents;

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

        private bool IsCollectionView(object target)
        {
            #region Build a type tree
            var typeTree = new List<Type>();
            var curType = target.GetType();
            while (curType != null)
            {
                typeTree.Add(curType);
                curType = curType.BaseType;
            }
            #endregion

            #region Check if any of the types matches the CollectionView
            return typeTree.Any(t => t.FullName == "System.Windows.Data.CollectionView");
            #endregion
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!isAddingOrRemovingRange)
            {
                var _deferredEv = (ICollection<NotifyCollectionChangedEventArgs>)_deferredEvents;
                if (_deferredEv == null)
                {
                    foreach (var handler in GetHandlers())
                    {
                        var isCollectionView = IsCollectionView(handler.Target);
                        if (IsRange(e) && isCollectionView)
                        {
                            // Call the Refresh method if the target is a WPF CollectionView
                            handler.Target.GetType().GetMethod("Refresh").Invoke(handler.Target, new object[0]);
                        }
                        else
                        {
                            handler(this, e);
                        }
                    }
                }
                else
                {
                    _deferredEv.Add(e);
                }

                // Also only attach the PropertyChanged event handler when we're not into
                // the process of adding a number of items one by one.

                if (typeof(T).GetInterfaces().Contains(typeof(INotifyPropertyChanged)))
                {
                    // First detach all event handlers
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                        {
                            ((INotifyPropertyChanged)item).PropertyChanged -= ObservableCollection_Item_PropertyChanged;
                        }
                    }
                    // Then attach all event handlers
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            ((INotifyPropertyChanged)item).PropertyChanged += ObservableCollection_Item_PropertyChanged;
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

        private bool IsRange(NotifyCollectionChangedEventArgs e)
        {
            return e.NewItems?.Count > 1 || e.OldItems?.Count > 1;
        }

        private IEnumerable<NotifyCollectionChangedEventHandler> GetHandlers()
        {
            var info = typeof(System.Collections.ObjectModel.ObservableCollection<T>).GetField(nameof(CollectionChanged), BindingFlags.Instance | BindingFlags.NonPublic);
            var @event = (MulticastDelegate)info.GetValue(this);
            return
                @event?.GetInvocationList().Cast<NotifyCollectionChangedEventHandler>().Distinct()
                ?? Enumerable.Empty<NotifyCollectionChangedEventHandler>();
        }

        #endregion

        #region Private Types
        sealed class DeferredEventsCollection : List<NotifyCollectionChangedEventArgs>, IDisposable
        {
            readonly ObservableCollection<T> _collection;
            public DeferredEventsCollection(ObservableCollection<T> collection)
            {
                Debug.Assert(collection != null);
                Debug.Assert(collection._deferredEvents == null);
                _collection = collection;
                _collection._deferredEvents = this;
            }

            public void Dispose()
            {
                _collection._deferredEvents = null;
                foreach (var args in this)
                    _collection.OnCollectionChanged(args);
            }
        }
        #endregion Private Types

    }
}
