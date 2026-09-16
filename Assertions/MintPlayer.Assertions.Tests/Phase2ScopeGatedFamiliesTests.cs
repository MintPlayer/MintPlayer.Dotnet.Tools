using System.ComponentModel;
using System.Reflection;
using MintPlayer.Assertions.Events;
using MintPlayer.Assertions.Reflection;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The scope-gated families from Phase 2 M7 (PRD §7, §9): streams, the reflection family, and the
/// event gaps that do not need <c>Reflection.Emit</c>.
/// </summary>
/// <remarks>
/// Each of these lives on an assertion type nobody else touches, which is the second corollary of
/// §0 and the only reason hot-path reflection is admissible here at all.
/// </remarks>
public class Phase2ScopeGatedFamiliesTests
{
    #region Streams

    [Fact]
    public void StreamCapabilities()
    {
        using var stream = new MemoryStream();

        stream.Should().BeReadable().And.BeWritable().And.BeSeekable();

        using var readOnly = new MemoryStream([1, 2, 3], writable: false);
        readOnly.Should().BeReadOnly();
        Assert.IsType<AssertionFailedException>(Record.Exception(() => readOnly.Should().BeWritable()));
    }

    [Fact]
    public void StreamLengthAndPosition()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);

        stream.Should().HaveLength(4).And.BeAtStart();
        stream.Position = 4;
        stream.Should().BeAtEnd().And.HavePosition(4);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => stream.Should().HaveLength(9)));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }

    /// <summary>
    /// <see cref="Stream.Length"/> throws on a non-seekable stream. An assertion that lets that
    /// escape reports a crash where the honest answer is a failure with a reason.
    /// </summary>
    [Fact]
    public void ANonSeekableStreamFailsRatherThanThrowing()
    {
        using var stream = new NonSeekableStream();

        var ex = Record.Exception(() => stream.Should().HaveLength(0));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("not seekable", ex!.Message);
    }

    [Fact]
    public void BufferedStreamExposesItsBufferSize()
    {
        using var inner = new MemoryStream();
        using var buffered = new BufferedStream(inner, 1024);

        buffered.Should().HaveBufferSize(1024).And.BeWritable();
        Assert.IsType<AssertionFailedException>(Record.Exception(() => buffered.Should().HaveBufferSize(2048)));
    }

    [Fact]
    public void ANullStreamFailsWithAReadableMessage()
    {
        Stream? stream = null;

        var ex = Record.Exception(() => stream.Should().BeReadable());

        Assert.Contains("<null>", ex!.Message);
    }

    #endregion

    #region Type members

    private sealed class Sample
    {
        public Sample() { }
        public Sample(int id) => Id = id;

        public int Id { get; init; }
        public string Name { get; set; } = "";
        public string this[int index] => Name;

        public void Act() { }
        public int Compute(int a, string b) => a;
        public static int Shared() => 1;
        public async Task Wait() => await Task.Yield();
    }

    [Fact]
    public void HaveProperty_AndItsTypedForm()
    {
        typeof(Sample).Should().HaveProperty("Name").Which.Should().BeOfType<string>();
        typeof(Sample).Should().HaveProperty<int>("Id");

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => typeof(Sample).Should().HaveProperty("Missing")));
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => typeof(Sample).Should().HaveProperty<int>("Name")));
    }

    /// <summary>
    /// The parameter types are required rather than optional: <c>GetMethod(name)</c> throws
    /// <see cref="AmbiguousMatchException"/> for an overloaded method, which turns an assertion into
    /// a crash.
    /// </summary>
    [Fact]
    public void HaveMethod_TakesTheParameterTypes()
    {
        typeof(Sample).Should().HaveMethod("Act", []).Which.Should().ReturnVoid();
        typeof(Sample).Should().HaveMethod("Compute", [typeof(int), typeof(string)]).Which.Should().Return<int>();

        typeof(Sample).Should().NotHaveMethod("Compute", [typeof(string)]);
    }

    [Fact]
    public void HaveConstructor_AndHaveIndexer()
    {
        typeof(Sample).Should().HaveDefaultConstructor();
        typeof(Sample).Should().HaveConstructor([typeof(int)]);
        typeof(Sample).Should().HaveIndexer([typeof(int)]);

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => typeof(Sample).Should().HaveConstructor([typeof(Guid)])));
    }

    [Fact]
    public void Namespaces()
    {
        typeof(Sample).Should().BeInNamespace("MintPlayer.Assertions.Tests");
        typeof(Sample).Should().BeUnderNamespace("MintPlayer.Assertions");

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => typeof(Sample).Should().BeInNamespace("MintPlayer.Assertions")));
    }

    [Fact]
    public void MethodInfoAssertions()
    {
        typeof(Sample).GetMethod(nameof(Sample.Shared))!.Should().BeStatic().And.HaveName("Shared");
        typeof(Sample).GetMethod(nameof(Sample.Wait))!.Should().BeAsync();
        typeof(Sample).GetMethod(nameof(Sample.Act))!.Should().NotBeVirtual();

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => typeof(Sample).GetMethod(nameof(Sample.Act))!.Should().BeAsync()));
    }

    [Fact]
    public void PropertyInfoAssertions()
    {
        typeof(Sample).GetProperty(nameof(Sample.Name))!.Should().BeWritable().And.BeDeclaredOn<Sample>();

        // init counts as writable: the difference between init and set is enforced by the compiler,
        // not by metadata this can read.
        typeof(Sample).GetProperty(nameof(Sample.Id))!.Should().BeWritable();
    }

    #endregion

    #region Assemblies and selectors

    [Fact]
    public void AssemblyReferences()
    {
        var tests = typeof(Phase2ScopeGatedFamiliesTests).Assembly;
        var library = typeof(AssertionFailedException).Assembly;

        tests.Should().Reference(library);
        library.Should().NotReference(tests);
    }

    [Fact]
    public void AssemblyDefinesType()
    {
        typeof(AssertionFailedException).Assembly.Should().DefineType(typeof(AssertionFailedException));

        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => typeof(AssertionFailedException).Assembly.Should().DefineType(typeof(Phase2ScopeGatedFamiliesTests))));
    }

    private interface IMarker;

    private sealed class FirstMarked : IMarker;

    private sealed class SecondMarked : IMarker;

    private class UnsealedMarked : IMarker;

    [Fact]
    public void ATypeSelectorReportsEveryOffender()
    {
        var selector = AllTypes.FromAssemblyContaining<Phase2ScopeGatedFamiliesTests>()
            .ThatImplement<IMarker>();

        selector.Should().NotBeEmpty();

        var ex = Record.Exception(() => selector.Should().BeSealed());

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains(nameof(UnsealedMarked), ex!.Message);
        Assert.DoesNotContain(nameof(FirstMarked), ex.Message);
    }

    [Fact]
    public void ASelectorThatMatchesNothingPassesEveryRule_WhichIsWhyNotBeEmptyExists()
    {
        var empty = AllTypes.FromAssemblyContaining<Phase2ScopeGatedFamiliesTests>()
            .Where(t => t.Name == "NoSuchTypeAnywhere");

        // Vacuously true, and that is the trap:
        empty.Should().BeSealed();

        var ex = Record.Exception(() => empty.Should().NotBeEmpty());
        Assert.Contains("vacuously", ex!.Message);
    }

    [Fact]
    public void SelectorFiltersCompose()
    {
        var selector = AllTypes.FromAssemblyContaining<Phase2ScopeGatedFamiliesTests>()
            .ThatImplement<IMarker>()
            .Where(t => t.Name.StartsWith("First", StringComparison.Ordinal));

        Assert.Equal([typeof(FirstMarked)], selector.Types);
        selector.Should().BeSealed().And.BeUnderNamespace("MintPlayer.Assertions.Tests");
    }

    #endregion

    #region Event gaps

    private sealed class Publisher : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? Something;

        public void RaiseSomething(string payload) => Something?.Invoke(this, payload);
        public void RaiseChanged(string property) => PropertyChanged?.Invoke(this, new(property));
    }

    /// <summary>
    /// "Never raised" and "never watched" produce the same failure and mean opposite things, which
    /// is why the monitored set is readable.
    /// </summary>
    [Fact]
    public void MonitoredEvents_SaysWhatIsActuallyBeingWatched()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        Assert.Contains(nameof(Publisher.Something), monitor.MonitoredEvents);
        Assert.Contains(nameof(INotifyPropertyChanged.PropertyChanged), monitor.MonitoredEvents);
    }

    [Fact]
    public void GetRecordingFor_ReturnsOccurrencesWithoutAsserting()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        Assert.Empty(monitor.GetRecordingFor(nameof(Publisher.Something)));

        publisher.RaiseSomething("a");
        publisher.RaiseSomething("b");

        var recorded = monitor.GetRecordingFor(nameof(Publisher.Something));
        Assert.Equal(2, recorded.Count);
        Assert.Equal(["a", "b"], recorded.Select(r => r.Parameters[0]));
    }

    /// <summary>Still refuses an event nobody is watching — answering "none" is how a test stops asserting.</summary>
    [Fact]
    public void GetRecordingFor_RefusesAnUnmonitoredEvent()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        Assert.Throws<InvalidOperationException>(() => monitor.GetRecordingFor("NoSuchEvent"));
    }

    [Fact]
    public void NotRaiseAnyEvents()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        monitor.NotRaiseAnyEvents();

        publisher.RaiseSomething("x");

        var ex = Record.Exception(() => monitor.NotRaiseAnyEvents());
        Assert.Contains("Something (1x)", ex!.Message);
    }

    [Fact]
    public void MultiPredicateWithArgs_RequiresOneOccurrenceToSatisfyAll()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething("alpha");
        publisher.RaiseSomething("beta");

        // One occurrence satisfies both.
        monitor.Raise(nameof(Publisher.Something))
            .WithArgs<string>(s => s.StartsWith('a'), s => s.EndsWith('a'));

        // No single occurrence does.
        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => monitor.Raise(nameof(Publisher.Something))
                .WithArgs<string>(s => s.StartsWith('a'), s => s.StartsWith('b'))));
    }

    [Fact]
    public void Times_CountsOccurrences()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor();

        publisher.RaiseSomething("x");
        publisher.RaiseSomething("y");

        monitor.Raise(nameof(Publisher.Something)).Times(2);
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => monitor.Raise(nameof(Publisher.Something)).Times(3)));
    }

    private sealed class ExplicitPublisher : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? handler;

        event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
        {
            add => handler += value;
            remove => handler -= value;
        }

        public void Raise(string property) => handler?.Invoke(this, new(property));
    }

    /// <summary>
    /// An explicitly implemented event is not listed by <c>typeof(T).GetEvents()</c>, so the monitor
    /// recorded nothing and every assertion failed with "does not expose a public event named
    /// PropertyChanged" — true, and useless.
    /// </summary>
    [Fact]
    public void IncludeInterfaceEvents_ReachesAnExplicitlyImplementedEvent()
    {
        var publisher = new ExplicitPublisher();

        using (var blind = publisher.Monitor())
        {
            Assert.DoesNotContain(nameof(INotifyPropertyChanged.PropertyChanged), blind.MonitoredEvents);
        }

        using var monitor = publisher.Monitor(EventMonitorOptions.Default with { IncludeInterfaceEvents = true });
        Assert.Contains(nameof(INotifyPropertyChanged.PropertyChanged), monitor.MonitoredEvents);

        publisher.Raise("Name");
        monitor.Raise(nameof(INotifyPropertyChanged.PropertyChanged));
    }

    /// <summary>An implicitly implemented event reached twice must not be recorded twice.</summary>
    [Fact]
    public void IncludeInterfaceEvents_DoesNotDoubleRecord()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor(EventMonitorOptions.Default with { IncludeInterfaceEvents = true });

        publisher.RaiseChanged("Name");

        Assert.Single(monitor.GetRecordingFor(nameof(INotifyPropertyChanged.PropertyChanged)));
    }

    [Fact]
    public void AnEventFilterNarrowsWhatIsMonitored()
    {
        var publisher = new Publisher();
        using var monitor = publisher.Monitor(EventMonitorOptions.Default with
        {
            EventFilter = e => e.Name == nameof(Publisher.Something),
        });

        Assert.Equal([nameof(Publisher.Something)], monitor.MonitoredEvents);
    }

    private sealed class Unmonitorable
    {
        public delegate int Weird(int a, int b, int c);

        public event Weird? Odd;

        public void Use() => Odd?.Invoke(1, 2, 3);
    }

    [Fact]
    public void ThrowOnUnmonitoredEvents_ReportsWhatItCannotWatch()
    {
        var subject = new Unmonitorable();

        using (var quiet = subject.Monitor())
        {
            Assert.Contains("Odd", quiet.UnmonitoredEvents);
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => subject.Monitor(EventMonitorOptions.Default with { ThrowOnUnmonitoredEvents = true }));
        Assert.Contains("Odd", ex.Message);
    }

    #endregion
}
