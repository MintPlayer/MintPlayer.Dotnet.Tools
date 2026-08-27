using System.ComponentModel;
using MintPlayer.Assertions.Events;

namespace MintPlayer.Assertions.Tests;

public class EventMonitorTests
{
    private sealed class CustomArgs : EventArgs
    {
        public int Value { get; init; }
    }

    private delegate void CustomHandler(object sender, CustomArgs e);

    private delegate int UnsupportedHandler(string a, string b, string c);

    private sealed class Publisher : INotifyPropertyChanged
    {
        private string name = string.Empty;

        public event EventHandler? SomethingHappened;
        public event EventHandler<CustomArgs>? ValueChanged;
        public event CustomHandler? CustomRaised;
        public event Action? Ticked;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event UnsupportedHandler? Unsupported;

        public string Name
        {
            get => name;
            set
            {
                name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public void RaiseSomething() => SomethingHappened?.Invoke(this, EventArgs.Empty);
        public void RaiseSomethingFrom(object sender) => SomethingHappened?.Invoke(sender, EventArgs.Empty);
        public void RaiseValueChanged(int value) => ValueChanged?.Invoke(this, new CustomArgs { Value = value });
        public void RaiseCustom(int value) => CustomRaised?.Invoke(this, new CustomArgs { Value = value });
        public void Tick() => Ticked?.Invoke();
        public int UnsupportedSubscriberCount => Unsupported?.GetInvocationList().Length ?? 0;
    }

    [Fact]
    public void Raise_Passes_WhenEventWasRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething();

        monitor.Raise("SomethingHappened");
    }

    [Fact]
    public void Raise_Fails_WhenEventWasNeverRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        var ex = Record.Exception(() => monitor.Raise("SomethingHappened", "we expected {0} activity", "some"));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("SomethingHappened", afe.Message);
        Assert.Contains("never raised", afe.Message);
        Assert.Contains("because we expected some activity", afe.Message);
    }

    [Fact]
    public void NotRaise_Passes_WhenEventWasNeverRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        monitor.NotRaise("SomethingHappened");
    }

    [Fact]
    public void NotRaise_Fails_WhenEventWasRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething();

        var ex = Record.Exception(() => monitor.NotRaise("SomethingHappened"));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", afe.Message);
        Assert.Contains("SomethingHappened", afe.Message);
    }

    [Fact]
    public void WithSender_Passes_WhenAnOccurrenceHasTheExpectedSender()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething();

        monitor.Raise("SomethingHappened").WithSender(publisher);
    }

    [Fact]
    public void WithSender_Fails_WhenNoOccurrenceHasTheExpectedSender()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomethingFrom(new object());

        var ex = Record.Exception(() => monitor.Raise("SomethingHappened").WithSender(publisher));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("with sender", afe.Message);
        Assert.Contains("no occurrence had that sender", afe.Message);
    }

    [Fact]
    public void WithArgs_Passes_WhenAnOccurrenceMatchesThePredicate()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseValueChanged(1);
        publisher.RaiseValueChanged(42);

        var assertions = monitor.Raise("ValueChanged").WithArgs<CustomArgs>(a => a.Value == 42);

        Assert.Single(assertions.Occurrences);
    }

    [Fact]
    public void WithArgs_Fails_WhenNoOccurrenceMatchesThePredicate()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseValueChanged(1);

        var ex = Record.Exception(() => monitor.Raise("ValueChanged").WithArgs<CustomArgs>(a => a.Value == 42));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("a.Value == 42", afe.Message);
        Assert.Contains("no occurrence did", afe.Message);
    }

    [Fact]
    public void ChainedConstraints_NarrowToTheSameOccurrence()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseValueChanged(42);

        monitor.Raise("ValueChanged")
            .WithSender(publisher)
            .WithArgs<CustomArgs>(a => a.Value == 42);
    }

    [Fact]
    public void CustomTwoParameterDelegate_IsMonitored()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseCustom(7);

        monitor.Raise("CustomRaised").WithSender(publisher).WithArgs<CustomArgs>(a => a.Value == 7);
    }

    [Fact]
    public void ParameterlessEvent_IsMonitored()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.Tick();

        var assertions = monitor.Raise("Ticked");
        Assert.Null(assertions.Occurrences[0].Sender);
        Assert.Empty(assertions.Occurrences[0].Parameters);
    }

    [Fact]
    public void UnsupportedEventShape_IsListedAsUnmonitored()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        Assert.Contains("Unsupported", monitor.UnmonitoredEvents);
        Assert.Equal(0, publisher.UnsupportedSubscriberCount);
        Assert.Throws<InvalidOperationException>(() => monitor.Raise("Unsupported"));
    }

    [Fact]
    public void UnknownEventName_Throws()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        Assert.Throws<InvalidOperationException>(() => monitor.Raise("NoSuchEvent"));
    }

    [Fact]
    public void RaisePropertyChangeFor_Passes_WhenPropertyChangedWasRaisedForTheProperty()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.Name = "changed";

        monitor.RaisePropertyChangeFor(p => p.Name).WithSender(publisher);
    }

    [Fact]
    public void RaisePropertyChangeFor_Fails_WhenPropertyChangedWasNotRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        var ex = Record.Exception(() => monitor.RaisePropertyChangeFor(p => p.Name));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("PropertyChanged", afe.Message);
        Assert.Contains("Name", afe.Message);
    }

    [Fact]
    public void NotRaisePropertyChangeFor_Passes_WhenPropertyChangedWasNotRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        monitor.NotRaisePropertyChangeFor(p => p.Name);
    }

    [Fact]
    public void NotRaisePropertyChangeFor_Fails_WhenPropertyChangedWasRaised()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.Name = "changed";

        var ex = Record.Exception(() => monitor.NotRaisePropertyChangeFor(p => p.Name));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", afe.Message);
        Assert.Contains("Name", afe.Message);
    }

    [Fact]
    public void OccurredEvents_RecordsOrderAndDetails()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething();
        publisher.RaiseValueChanged(3);

        var events = monitor.OccurredEvents;
        Assert.Equal(2, events.Count);
        Assert.Equal("SomethingHappened", events[0].EventName);
        Assert.Equal("ValueChanged", events[1].EventName);
        Assert.Equal(0, events[0].Sequence);
        Assert.Equal(1, events[1].Sequence);
        Assert.Same(publisher, events[1].Sender);
        var args = Assert.IsType<CustomArgs>(events[1].Parameters[0]);
        Assert.Equal(3, args.Value);
    }

    [Fact]
    public void Clear_DiscardsRecordedOccurrences()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething();
        monitor.Clear();

        Assert.Empty(monitor.OccurredEvents);
        monitor.NotRaise("SomethingHappened");
    }

    [Fact]
    public void Dispose_StopsRecording()
    {
        var publisher = new Publisher();
        var monitor = publisher.Monitor();
        monitor.Dispose();

        publisher.RaiseSomething();

        Assert.Empty(monitor.OccurredEvents);
    }

    [Fact]
    public void FailureMessage_ContainsTheSubjectExpression()
    {
        var thePublisher = new Publisher();
        using var monitor = thePublisher.Monitor();

        var ex = Record.Exception(() => monitor.Raise("SomethingHappened"));

        var afe = Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("thePublisher", afe.Message);
    }
}
