using System.Collections.Specialized;

namespace MintPlayer.ObservableCollection.Tests;

/// <summary>
/// The collection captures SynchronizationContext.Current in a FIELD INITIALIZER, so the
/// context has to be installed before the constructor runs.
///
/// Note that xUnit v2 DOES install an ambient context (AsyncTestSyncContext) around every
/// test, so Current is not null here. It is the same context the mutations then run on, so
/// RunOnMainThread short-circuits and the marshalling branch stays unexercised unless a
/// test deliberately constructs under one context and mutates under another — which is what
/// WithContext below sets up.
/// </summary>
public class SynchronizationTests
{
    /// <summary>Records every callback and runs it inline, standing in for a UI dispatcher.</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int SendCount { get; private set; }
        public int PostCount { get; private set; }

        public override void Send(SendOrPostCallback d, object? state)
        {
            SendCount++;
            d(state);
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            d(state);
        }
    }

    private static T WithContext<T>(SynchronizationContext context, Func<T> body)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return body();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void WithNoContext_TheCollectionStillWorks()
    {
        // Explicitly clear the ambient xUnit context so the null-context branch of
        // RunOnMainThread is the one that runs.
        var collection = WithContext(null!, () =>
        {
            var c = new ObservableCollection<string>();
            c.Add("a");
            return c;
        });

        collection.Should().Equal(["a"]);
    }

    [Fact]
    public void ACollectionConstructedWithoutAContext_IgnoresOneInstalledLater()
    {
        var collection = WithContext(null!, () => new ObservableCollection<string>());
        var context = new RecordingSynchronizationContext();

        WithContext(context, () => { collection.Add("a"); return 0; });

        // Nothing was captured at construction, so there is nothing to marshal to.
        collection.Should().Equal(["a"]);
        context.SendCount.Should().Be(0);
    }

    [Fact]
    public void MutationsAreMarshalledThroughTheCapturedContext()
    {
        var context = new RecordingSynchronizationContext();

        // Construct INSIDE the context so the field initializer captures it, then mutate
        // from a different context so RunOnMainThread takes its Send branch.
        var collection = WithContext(context, () => new ObservableCollection<string>());

        collection.Add("a");

        collection.Should().Equal(["a"]);
        context.SendCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MutationsRunInlineWhenAlreadyOnTheCapturedContext()
    {
        var context = new RecordingSynchronizationContext();

        var collection = WithContext(context, () =>
        {
            var c = new ObservableCollection<string>();
            c.Add("a");
            return c;
        });

        // Same context, so RunOnMainThread short-circuits rather than calling Send.
        collection.Should().Equal(["a"]);
        context.SendCount.Should().Be(0);
    }

    [Fact]
    public void AddRangeIsMarshalledThroughTheContext()
    {
        var context = new RecordingSynchronizationContext();
        var collection = WithContext(context, () => new ObservableCollection<string>());

        collection.AddRange(["a", "b", "c"]);

        collection.Should().Equal(["a", "b", "c"]);
        context.SendCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RangeNotificationsStillReachHandlersUnderAContext()
    {
        var context = new RecordingSynchronizationContext();
        var collection = WithContext(context, () => new ObservableCollection<string>());

        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);

        collection.AddRange(["a", "b"]);

        events.Should().ContainSingle();
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public void ItemPropertyChangedIsMarshalledThroughTheContext()
    {
        var context = new RecordingSynchronizationContext();
        using var collection = WithContext(context, () => new ObservableCollection<NotifyingPerson>());

        var person = new NotifyingPerson();
        collection.Add(person);

        var before = context.SendCount;
        var raised = 0;
        collection.ItemPropertyChanged += (_, _) => raised++;

        person.FirstName = "X";

        raised.Should().Be(1);
        context.SendCount.Should().BeGreaterThan(before);
    }

    [Fact]
    public void ARangeHandlerWhoseTargetIsNull_IsSkippedRatherThanThrowing()
    {
        // IsCollectionView throws TargetNullException for a null target, which
        // OnCollectionChanged catches and logs. A static handler has a null Target.
        var collection = new ObservableCollection<string>();
        collection.CollectionChanged += StaticHandler;

        var act = () => collection.AddRange(["a", "b"]);

        act.Should().NotThrow();
        collection.Should().Equal(["a", "b"]);

        collection.CollectionChanged -= StaticHandler;
    }

    private static void StaticHandler(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Deliberately empty: the point is that Target is null.
    }
}
