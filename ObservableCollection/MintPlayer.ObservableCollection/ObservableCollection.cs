/**
 * 
 * Goto Github -> settings -> Developer settings -> Personal access tokens
 * Create a new token with following scopes: write:packages, read:packages, delete:packages.
 * This will automatically add the repo scope.
 * dotnet nuget add source --username PieterjanDeClippel --password <api-key> --name github https://nuget.pkg.github.com/MintPlayer/index.json
 * dotnet nuget push --source github bin\Release\MintPlayer.ObservableCollection.1.2.0.nupkg
 * 
 **/

using MintPlayer.ObservableCollection.Events.EventHandlers;
using MintPlayer.ObservableCollection.Exceptions;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MintPlayer.ObservableCollection;

public class ObservableCollection
    <[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicMethods)] T> 
    : System.Collections.ObjectModel.ObservableCollection<T>, IDisposable
{
    #region Constructors
    public ObservableCollection()
    {
        areItemsImplementingINotifyPropertyChanged = typeof(T).GetInterfaces().Contains(typeof(INotifyPropertyChanged));
    }

    public ObservableCollection(IEnumerable<T> items) : this()
    {
        InternalAddRange(items);
        if (areItemsImplementingINotifyPropertyChanged)
        {
            var notifyItems = Items.Cast<INotifyPropertyChanged>();
            itemNotifyPropertyChangedList.AddRange(notifyItems);
            foreach (var item in notifyItems)
            {
                item.PropertyChanged += ObservableCollection_Item_PropertyChanged;
            }
        }
    }
    #endregion Constructors

    #region Finalizer

    ~ObservableCollection()
    {
        Dispose(false);
    }

    #endregion

    #region Private fields

    private bool isAddingOrRemovingRange;
    [NonSerialized] private DeferredEventsCollection? deferredEvents;
    private readonly SynchronizationContext? synchronizationContext = SynchronizationContext.Current;

    private readonly bool areItemsImplementingINotifyPropertyChanged;
    private readonly List<INotifyPropertyChanged> itemNotifyPropertyChangedList = [];
    private bool _disposed;

    #endregion Private fields

    #region Public methods

    public virtual void AddRange(IEnumerable<T> items)
    {
        var enumerable = items as T[] ?? items.ToArray();
        if (enumerable.Length == 0) return;
        if (enumerable.Length == 1)
        {
            Add(enumerable[0]);
            return;
        }

        CheckReentrancy();

        RunOnMainThread(param =>
        {
            InternalAddRange(param.items);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, param.items));
        }, new { items = enumerable.ToArray() });
    }

    public virtual void RemoveRange(IEnumerable<T> items)
    {
        var enumerable = items as T[] ?? items.ToArray();
        if (enumerable.Length == 0) return;
        if (enumerable.Length == 1)
        {
            Remove(enumerable[0]);
            return;
        }

        CheckReentrancy();
        RunOnMainThread(param =>
        {
            InternalRemoveRange(param.items);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, param.items));
        }, new { items = enumerable.ToArray() });
    }

    #endregion Public methods

    #region Public properties

    /// <summary>
    /// Enable or disable the change notifications and events for this collection.
    /// </summary>
    public bool Enabled { get; set; } = true;

    #endregion Public properties

    #region Events

    public event ItemPropertyChangedEventHandler<T>? ItemPropertyChanged;

    #endregion Events

    #region Make CollectionChanged + PropertyChanged + ItemPropertyChanged events threadsafe

    // Let's allow this method to be overridden, to use custom UI frameworks
    protected virtual bool IsCollectionView(object? target)
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
        return typeTree.Any(t => 
            t.FullName == "System.Windows.Data.CollectionView" // WPF
            //|| t.FullName == "Avalonia.Collections.DataGridCollectionView" // Avalonia DataGrid (Working without Refresh requirement)
            || t.DeclaringType?.FullName == "Avalonia.Utilities.WeakEvents");
        #endregion
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (isAddingOrRemovingRange) return;

        if (Enabled)
        {
            // If there's only one item being added/removed, there's no need to do anything special.
            if (!IsRange(e))
            {
                base.OnCollectionChanged(e);
            }
            else
            {
                if (deferredEvents is ICollection<NotifyCollectionChangedEventArgs> deferredEv)
                {
                    deferredEv.Add(e);
                }
                else
                {
                    var handlers = GetHandlers();
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            var isCollectionView = IsCollectionView(handler.Target);
                            if (isCollectionView)
                            {
                                var method = handler.Target?.GetType().GetMethod("Refresh");

                                if (method is null)
                                {
                                    var newNotification = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                                    RunOnMainThread(
                                        param => handler(this, param.e),
                                        new { e = newNotification }
                                    );
                                }
                                else
                                {
                                    // Call the Refresh method if the target is a WPF CollectionView
                                    RunOnMainThread(
                                        param => param.Method.Invoke(param.Target, []),
                                        new { handler.Target, Method = method }
                                    );
                                }

                            }
                            else
                            {
                                RunOnMainThread(
                                    param => handler(this, param.e),
                                    new { e }
                                );
                            }
                        }
                        catch (TargetNullException)
                        {
                            Debug.WriteLine($"The target of EventHandler {handler.Method.Name} is null.");
                        }
                    }
                }
            }
        }

        // Also only attach the PropertyChanged event handler
        if (areItemsImplementingINotifyPropertyChanged)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var item in itemNotifyPropertyChangedList)
                {
                    item.PropertyChanged -= ObservableCollection_Item_PropertyChanged;
                }
                itemNotifyPropertyChangedList.Clear();

                // Attach event handlers to all items
                if (Count > 0)
                {
                    var notifyItems = Items.Cast<INotifyPropertyChanged>();
                    itemNotifyPropertyChangedList.AddRange(notifyItems);
                    foreach (var item in notifyItems)
                    {
                        item.PropertyChanged += ObservableCollection_Item_PropertyChanged;
                    }
                }
            }
            else
            {
                // First detach all event handlers
                if (e.OldItems != null)
                {
                    var notifyItems = e.OldItems.Cast<INotifyPropertyChanged>();
                    foreach (var item in notifyItems)
                    {
                        item.PropertyChanged -= ObservableCollection_Item_PropertyChanged;
                        itemNotifyPropertyChangedList.Remove(item);
                    }
                }
                // Then attach all event handlers
                if (e.NewItems != null)
                {
                    var notifyItems = e.NewItems.Cast<INotifyPropertyChanged>();
                    foreach (var item in notifyItems)
                    {
                        item.PropertyChanged += ObservableCollection_Item_PropertyChanged;
                    }
                    itemNotifyPropertyChangedList.AddRange(notifyItems);
                }
            }
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (Enabled)
        {
            RunOnMainThread(
                param => base.OnPropertyChanged(param.e),
                new { e }
            );
        }
    }

    private void ObservableCollection_Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Enabled || sender is null || ItemPropertyChanged is null) return;

        RunOnMainThread(
            param =>
            {
                ItemPropertyChanged(
                    (T)param.sender,
                    new Events.EventArgs.ItemPropertyChangedEventArgs<T>(
                        (T)param.sender,
                        param.e.PropertyName!
                    )
                );
            },
            new { sender, e }
        );
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
        RunOnMainThread(param => base.InsertItem(param.index, param.item), new { index, item });
    }

    protected override void ClearItems()
    {
        RunOnMainThread<object>(param => base.ClearItems(), null);
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        RunOnMainThread((param) => base.MoveItem(param.oldIndex, param.newIndex), new { oldIndex, newIndex });
    }

    protected override void RemoveItem(int index)
    {
        RunOnMainThread(param => base.RemoveItem(param), index);
    }

    protected override void SetItem(int index, T item)
    {
        RunOnMainThread(param => base.SetItem(param.index, param.item), new { index, item });
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

    private static bool IsRange(NotifyCollectionChangedEventArgs e)
    {
        var totalChanged = 0;

        if (e.NewItems != null)
        {
            totalChanged += e.NewItems.Count;
        }
        if (e.OldItems != null)
        {
            totalChanged += e.OldItems.Count;
        }

        return totalChanged != 1;
    }

    private IEnumerable<NotifyCollectionChangedEventHandler> GetHandlers()
    {
        var info = typeof(System.Collections.ObjectModel.ObservableCollection<T>).GetField(nameof(CollectionChanged), BindingFlags.Instance | BindingFlags.NonPublic);
        var @event = info?.GetValue(this) as MulticastDelegate;
        var result =
            @event?.GetInvocationList().Cast<NotifyCollectionChangedEventHandler>().Distinct()
            ?? [];

        return result;
    }

    #endregion Support range operations

    #region Synchronization to main thread

    protected virtual void RunOnMainThread<TState>(Action<TState> action, TState? state)
    {
        if ((synchronizationContext == null) || (synchronizationContext == SynchronizationContext.Current))
        {
            action(state!);
        }
        else
        {
            synchronizationContext.Send((param) => action((TState)param!), state);
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

    #region Disposable
    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            deferredEvents?.Dispose();
            if (areItemsImplementingINotifyPropertyChanged)
            {
                foreach (var item in itemNotifyPropertyChangedList)
                {
                    item.PropertyChanged -= ObservableCollection_Item_PropertyChanged;
                }
                itemNotifyPropertyChangedList.Clear();
            }
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    #endregion
}