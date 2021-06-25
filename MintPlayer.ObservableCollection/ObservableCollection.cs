/**
 * 
 * Goto Github -> settings -> Developer settings -> Personal access tokens
 * Create a new token with following scopes: write:packages, read:packages, delete:packages.
 * This will automatically add the repo scope.
 * dotnet nuget add source --username PieterjanDeClippel --password <api-key> --name github https://nuget.pkg.github.com/MintPlayer/index.json
 * dotnet nuget push --source github bin\Release\MintPlayer.ObservableCollection.1.2.0.nupkg
 * 
 **/

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using MintPlayer.ObservableCollection.Events.EventHandlers;
using System.ComponentModel;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using MintPlayer.ObservableCollection.Exceptions;

namespace MintPlayer.ObservableCollection
{
    public class ObservableCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
    {
        #region Constructors
        public ObservableCollection()
        {
        }

        public ObservableCollection(IEnumerable<T> items) : this()
        {
            InternalAddRange(items);
        }
        #endregion Constructors

        #region Private fields

        private bool isAddingOrRemovingRange;
        [NonSerialized] private DeferredEventsCollection deferredEvents;
        private readonly SynchronizationContext synchronizationContext = SynchronizationContext.Current;

        #endregion Private fields

        #region Public methods

        public void AddRange(IEnumerable<T> items)
        {
            CheckReentrancy();

            RunOnMainThread((param) =>
            {
                InternalAddRange(param.items);
                OnCountPropertyChanged();
                OnIndexerPropertyChanged();
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, param.items));
            }, new { items = items.ToList() });
        }

        public void RemoveRange(IEnumerable<T> items)
        {
            CheckReentrancy();

            RunOnMainThread((param) =>
            {
                InternalRemoveRange(param.items);
                OnCountPropertyChanged();
                OnIndexerPropertyChanged();
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, param.items));
            }, new { items = items.ToList() });
        }

        #endregion Public methods

        #region Public properties

        public bool Enabled { get; set; } = true;

        #endregion Public properties

        #region Events

        public event ItemPropertyChangedEventHandler<T> ItemPropertyChanged;

        #endregion Events

        #region Make CollectionChanged + PropertyChanged + ItemPropertyChanged events threadsafe

        private bool IsCollectionView(object target)
        {
            if (target == null)
            {
                throw new TargetNullException();
            }

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
                var _deferredEv = (ICollection<NotifyCollectionChangedEventArgs>)deferredEvents;
                if (_deferredEv == null)
                {
                    var handlers = GetHandlers();
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            var isCollectionView = IsCollectionView(handler.Target);
                            var isRange = IsRange(e);
                            if (isRange && isCollectionView)
                            {
                                // Call the Refresh method if the target is a WPF CollectionView
                                RunOnMainThread(
                                    (param) => handler.Target.GetType().GetMethod("Refresh").Invoke(param.target, new object[0]),
                                    new { target = handler.Target }
                                );
                            }
                            else if (Enabled)
                            {
                                RunOnMainThread(
                                    (param) => handler(this, param.e),
                                    new { e }
                                );
                            }
                        }
                        catch (TargetNullException ex)
                        {
                            Debug.WriteLine($"The target of EventHandler {handler.Method.Name} is null.");
                            Console.WriteLine($"The target of EventHandler {handler.Method.Name} is null.");
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

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (Enabled)
            {
                RunOnMainThread(
                    (param) => base.OnPropertyChanged(param.e),
                    new { e }
                );
            }
        }

        private void ObservableCollection_Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (Enabled)
            {
                RunOnMainThread(
                    (param) =>
                    {
                        if (ItemPropertyChanged != null)
                        {
                            ItemPropertyChanged(
                                (T)param.sender,
                                new Events.EventArgs.ItemPropertyChangedEventArgs<T>(
                                    (T)param.sender,
                                    param.e.PropertyName
                                )
                            );
                        }
                    },
                    new { sender, e }
                );
            }
        }

        #endregion Make CollectionChanged + PropertyChanged + ItemPropertyChanged events threadsafe

        #region Private methods
        #region Notifications

        private void OnCountPropertyChanged()
        {
            if (Enabled)
            {
                base.OnPropertyChanged(EventArgsCache.CountPropertyChangedEventArgs);
            }
        }

        private void OnIndexerPropertyChanged()
        {
            if (Enabled)
            {
                base.OnPropertyChanged(EventArgsCache.IndexerPropertyChangedEventArgs);
            }
        }

        #endregion Notifications

        #region Make base methods threadsafe

        protected override void InsertItem(int index, T item)
        {
            RunOnMainThread((param) => base.InsertItem(param.index, param.item), new { index, item });
        }

        protected override void ClearItems()
        {
            RunOnMainThread<object>((param) => base.ClearItems(), null);
        }

        protected override void MoveItem(int oldIndex, int newIndex)
        {
            RunOnMainThread((param) => base.MoveItem(param.oldIndex, param.newIndex), new { oldIndex, newIndex });
        }

        protected override void RemoveItem(int index)
        {
            RunOnMainThread((param) => base.RemoveItem(param), index);
        }

        protected override void SetItem(int index, T item)
        {
            RunOnMainThread((param) => base.SetItem(param.index, param.item), new { index, item });
        }

        #endregion Make base methods threadsafe

        #region Support range operations

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

        private void InternalRemoveRange(IEnumerable<T> items)
        {
            try
            {
                isAddingOrRemovingRange = true;
                foreach (var item in items)
                {
                    if (Items.Contains(item))
                    {
                        Remove(item);
                    }
                }
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
            var result =
                @event?.GetInvocationList().Cast<NotifyCollectionChangedEventHandler>().Distinct()
                ?? Enumerable.Empty<NotifyCollectionChangedEventHandler>();

            return result;
        }

        #endregion Support range operations

        #region Synchronization to main thread

        private void RunOnMainThread<TState>(Action<TState> action, TState state)
        {
            if ((synchronizationContext == null) || (synchronizationContext == SynchronizationContext.Current))
            {
                action(state);
            }
            else
            {
                synchronizationContext.Send((param) => action((TState)param), state);
            }
        }

        #endregion Synchronization to main thread

        #endregion Private methods

        #region Private Types

        sealed class DeferredEventsCollection : List<NotifyCollectionChangedEventArgs>, IDisposable
        {
            readonly ObservableCollection<T> collection;
            public DeferredEventsCollection(ObservableCollection<T> collection)
            {
                Debug.Assert(collection != null);
                Debug.Assert(collection.deferredEvents == null);
                this.collection = collection;
                this.collection.deferredEvents = this;
            }

            public void Dispose()
            {
                collection.deferredEvents = null;
                collection.RunOnMainThread(
                    (param) =>
                    {
                        foreach (var args in this)
                        {
                            collection.OnCollectionChanged(args);
                        }
                    },
                    new { }
                );
            }
        }

        #endregion Private Types

    }
}
