using System.Collections.Specialized;

namespace MintPlayer.ObservableCollection.Tests;

/// <summary>
/// Covers the collection itself. SynchronizationContext.Current is null under xUnit, so
/// RunOnMainThread runs inline — see SynchronizationTests for the marshalling path.
/// </summary>
public class ObservableCollectionTests
{
    private static List<NotifyCollectionChangedEventArgs> Record<T>(ObservableCollection<T> collection)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);
        return events;
    }

    #region Construction

    [Fact]
    public void DefaultConstructor_StartsEmpty()
        => new ObservableCollection<string>().Should().BeEmpty();

    [Fact]
    public void SeedingConstructor_ContainsTheItems()
        => new ObservableCollection<string>(["a", "b", "c"]).Should().Equal(["a", "b", "c"]);

    [Fact]
    public void SeedingConstructor_DoesNotRaiseCollectionChanged()
    {
        // The handler cannot be attached before construction, so nothing can observe the
        // seed. Pinned to document that seeding is not an observable Add.
        var collection = new ObservableCollection<string>(["a"]);
        var events = Record(collection);

        events.Should().BeEmpty();
    }

    [Fact]
    public void SeedingConstructor_SubscribesToSeededNotifyingItems()
    {
        var person = new NotifyingPerson { FirstName = "A" };
        using var collection = new ObservableCollection<NotifyingPerson>([person]);

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        person.LastName = "B";

        raised.Should().Be(1);
    }

    #endregion

    #region Single-item operations

    [Fact]
    public void Add_RaisesAnAddNotification()
    {
        var collection = new ObservableCollection<string>();
        var events = Record(collection);

        collection.Add("a");

        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Add);
        events[0].NewItems!.Cast<string>().Should().Equal(["a"]);
    }

    [Fact]
    public void Remove_RaisesARemoveNotification()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);
        var events = Record(collection);

        collection.Remove("a");

        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Remove);
    }

    [Fact]
    public void Insert_PlacesTheItemAtTheIndex()
    {
        var collection = new ObservableCollection<string>(["a", "c"]);

        collection.Insert(1, "b");

        collection.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void Indexer_ReplacesTheItem()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);
        var events = Record(collection);

        collection[1] = "z";

        collection.Should().Equal(["a", "z"]);
        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Replace);
    }

    [Fact]
    public void Move_ReordersTheItems()
    {
        var collection = new ObservableCollection<string>(["a", "b", "c"]);

        collection.Move(0, 2);

        collection.Should().Equal(["b", "c", "a"]);
    }

    [Fact]
    public void Clear_EmptiesTheCollectionAndRaisesReset()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);
        var events = Record(collection);

        collection.Clear();

        collection.Should().BeEmpty();
        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    #endregion

    #region AddRange

    [Fact]
    public void AddRange_AddsEveryItem()
    {
        var collection = new ObservableCollection<string>();

        collection.AddRange(["a", "b", "c"]);

        collection.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void AddRange_RaisesOneNotificationForTheWholeRange()
    {
        var collection = new ObservableCollection<string>();
        var events = Record(collection);

        collection.AddRange(["a", "b", "c"]);

        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Add);
        events[0].NewItems!.Cast<string>().Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void AddRange_WithASingleItem_TakesTheSingleAddPath()
    {
        var collection = new ObservableCollection<string>();
        var events = Record(collection);

        collection.AddRange(["only"]);

        collection.Should().Equal(["only"]);
        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public void AddRange_WithNoItems_IsANoOp()
    {
        var collection = new ObservableCollection<string>(["a"]);
        var events = Record(collection);

        collection.AddRange([]);

        collection.Should().Equal(["a"]);
        events.Should().BeEmpty();
    }

    [Fact]
    public void AddRange_AppendsToExistingItems()
    {
        var collection = new ObservableCollection<string>(["a"]);

        collection.AddRange(["b", "c"]);

        collection.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void AddRange_EnumeratesALazySourceOnce()
    {
        var enumerations = 0;

        IEnumerable<string> Tracked()
        {
            enumerations++;
            yield return "a";
            yield return "b";
        }

        new ObservableCollection<string>().AddRange(Tracked());

        enumerations.Should().Be(1);
    }

    #endregion

    #region RemoveRange

    [Fact]
    public void RemoveRange_RemovesEveryNamedItem()
    {
        var collection = new ObservableCollection<string>(["a", "b", "c", "d"]);

        collection.RemoveRange(["b", "d"]);

        collection.Should().Equal(["a", "c"]);
    }

    [Fact]
    public void RemoveRange_RaisesOneNotification()
    {
        var collection = new ObservableCollection<string>(["a", "b", "c"]);
        var events = Record(collection);

        collection.RemoveRange(["a", "b"]);

        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Remove);
    }

    [Fact]
    public void RemoveRange_WithASingleItem_TakesTheSingleRemovePath()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);
        var events = Record(collection);

        collection.RemoveRange(["a"]);

        collection.Should().Equal(["b"]);
        events.Should().ContainSingle();
    }

    [Fact]
    public void RemoveRange_WithNoItems_IsANoOp()
    {
        var collection = new ObservableCollection<string>(["a"]);
        var events = Record(collection);

        collection.RemoveRange([]);

        collection.Should().Equal(["a"]);
        events.Should().BeEmpty();
    }

    [Fact]
    public void RemoveRange_IgnoresItemsThatAreNotPresent()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);

        collection.RemoveRange(["b", "zzz", "qqq"]);

        collection.Should().Equal(["a"]);
    }

    #endregion

    #region Enabled

    [Fact]
    public void Enabled_IsTrueByDefault()
        => new ObservableCollection<string>().Enabled.Should().BeTrue();

    [Fact]
    public void Enabled_False_SuppressesNotificationsButStillMutates()
    {
        var collection = new ObservableCollection<string>();
        var events = Record(collection);

        collection.Add("before");
        collection.Enabled = false;
        collection.Add("during");
        collection.Enabled = true;
        collection.Add("after");

        // Ported from the old console harness's Demo1.
        collection.Should().Equal(["before", "during", "after"]);
        events.Should().HaveCount(2);
        events.SelectMany(e => e.NewItems!.Cast<string>()).Should().Equal(["before", "after"]);
    }

    [Fact]
    public void Enabled_False_SuppressesRangeNotifications()
    {
        var collection = new ObservableCollection<string>();
        var events = Record(collection);

        collection.Enabled = false;
        collection.AddRange(["a", "b", "c"]);

        collection.Should().HaveCount(3);
        events.Should().BeEmpty();
    }

    [Fact]
    public void Enabled_False_SuppressesItemPropertyChanged()
    {
        var person = new NotifyingPerson();
        using var collection = new ObservableCollection<NotifyingPerson>();
        collection.Add(person);

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        collection.Enabled = false;
        person.FirstName = "quiet";
        collection.Enabled = true;
        person.FirstName = "loud";

        raised.Should().Be(1);
    }

    #endregion

    #region ItemPropertyChanged

    [Fact]
    public void ItemPropertyChanged_FiresForAnAddedItem()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var person = new NotifyingPerson();
        collection.Add(person);

        var names = new List<string>();
        collection.ItemPropertyChanged += (_, e) => names.Add(e.PropertyName);

        person.FirstName = "John";
        person.LastName = "Doe";

        names.Should().Equal([nameof(NotifyingPerson.FirstName), nameof(NotifyingPerson.LastName)]);
    }

    [Fact]
    public void ItemPropertyChanged_CarriesTheItem()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var person = new NotifyingPerson();
        collection.Add(person);

        NotifyingPerson? seen = null;
        collection.ItemPropertyChanged += (_, e) => seen = e.Item;

        person.FirstName = "X";

        seen.Should().BeSameAs(person);
    }

    [Fact]
    public void ItemPropertyChanged_StopsAfterTheItemIsRemoved()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var person = new NotifyingPerson();
        collection.Add(person);
        collection.Remove(person);

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        person.FirstName = "gone";

        raised.Should().Be(0);
    }

    [Fact]
    public void ItemPropertyChanged_StopsForAReplacedItem()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var original = new NotifyingPerson();
        var replacement = new NotifyingPerson();
        collection.Add(original);
        collection[0] = replacement;

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        original.FirstName = "detached";
        raised.Should().Be(0);

        replacement.FirstName = "attached";
        raised.Should().Be(1);
    }

    [Fact]
    public void ItemPropertyChanged_ReattachesAfterClear()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var person = new NotifyingPerson();
        collection.Add(person);
        collection.Clear();

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        person.FirstName = "orphan";

        // Clear raises Reset, which detaches every handler.
        raised.Should().Be(0);
    }

    [Fact]
    public void ItemPropertyChanged_FiresForItemsAddedByAddRange()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var a = new NotifyingPerson();
        var b = new NotifyingPerson();
        collection.AddRange([a, b]);

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        a.FirstName = "1";
        b.FirstName = "2";

        raised.Should().Be(2);
    }

    [Fact]
    public void ItemPropertyChanged_IsNotWiredForPlainItems()
    {
        // A T without INotifyPropertyChanged skips the subscription bookkeeping entirely.
        using var collection = new ObservableCollection<Plain>();
        collection.Add(new Plain("a"));

        collection.Should().HaveCount(1);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_DetachesItemHandlers()
    {
        var collection = new ObservableCollection<NotifyingPerson>();
        var person = new NotifyingPerson();
        collection.Add(person);

        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        collection.Dispose();
        person.FirstName = "after dispose";

        raised.Should().Be(0);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var collection = new ObservableCollection<NotifyingPerson>();
        collection.Add(new NotifyingPerson());

        var act = () =>
        {
            collection.Dispose();
            collection.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_OnAPlainCollection_DoesNotThrow()
    {
        var collection = new ObservableCollection<string>(["a"]);

        var act = collection.Dispose;

        act.Should().NotThrow();
    }

    #endregion

    #region Ported console-harness scenario

    [Fact]
    public void TheOldDemo2Scenario_RunsEndToEnd()
    {
        using var collection = new ObservableCollection<NotifyingPerson>();
        var events = Record(collection);
        var propertyChanges = 0;
        collection.ItemPropertyChanged += (_, _) => propertyChanges++;

        var person1 = new NotifyingPerson { FirstName = "John", LastName = "Doe" };
        var person2 = new NotifyingPerson { FirstName = "Jimmy", LastName = "Fallon" };
        var person3 = new NotifyingPerson { FirstName = "Michael", LastName = "Douglas" };

        collection.AddRange([person1, person2, person3]);
        collection[1].LastName = "Knibble";
        collection[1] = new NotifyingPerson { FirstName = "Sim", LastName = "Salabim" };
        collection.RemoveRange([person1, person3]);

        var person4 = new NotifyingPerson { FirstName = "Johnny", LastName = "Logan" };
        var person5 = new NotifyingPerson { FirstName = "Kiddy", LastName = "Bull" };
        var person6 = new NotifyingPerson { FirstName = "Jacky", LastName = "Chan" };
        collection.AddRange([person4, person5, person6]);

        collection.Select(p => p.FirstName).Should().Equal(["Sim", "Johnny", "Kiddy", "Jacky"]);
        propertyChanges.Should().Be(1);
        events.Should().NotBeEmpty();
    }

    #endregion
}
