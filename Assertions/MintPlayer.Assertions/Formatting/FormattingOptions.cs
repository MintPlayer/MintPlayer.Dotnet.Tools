namespace MintPlayer.Assertions.Formatting;

/// <summary>
/// The knobs <see cref="Formatter"/> renders failure messages with. All of them were constants;
/// they are settable now because the right value depends on what is being asserted, and a truncated
/// message is the difference between a failure you can read and one you have to reproduce.
/// </summary>
/// <remarks>
/// <para>
/// Two layers, and the distinction matters. The <b>properties</b> are process-wide defaults, meant to
/// be set once at start-up. <see cref="With"/> applies a <b>thread-local override</b> for the
/// duration of a <c>using</c> block, and is what you want anywhere else.
/// </para>
/// <para>
/// ⚠️ <b>Setting a property from inside a test is the PRD §9.9 trap, verbatim.</b> Test runners
/// execute classes in parallel, so a test that raises <see cref="MaxDepth"/> changes the failure
/// messages of every test running beside it — and the resulting flakiness looks like anything but
/// its cause. That exact shape already cost this repo a debugging session: process-wide counters plus
/// parallel test classes reported a walker node count of 259 where the graph had 133, and the fix was
/// thread-local state rather than switching parallelism off. <see cref="With"/> is that same fix,
/// offered before the mistake rather than after it.
/// </para>
/// <para>
/// These are not per-assertion parameters, which would be the third option. Threading a formatting
/// context through every assertion would put an argument on the passing path of ~360 methods to serve
/// something only failures read.
/// </para>
/// </remarks>
public static class FormattingOptions
{
    private static int maxDepth = 3;
    private static int maxCollectionItems = 32;
    private static int maxStringLength = 512;
    private static int maxLines;
    private static bool useLineBreaks;

    [ThreadStatic] private static Overrides? overrides;

    /// <summary>
    /// How deep into an object graph a rendered value descends before collapsing to
    /// <c>Type {… depth N reached}</c>. Default 3.
    /// </summary>
    public static int MaxDepth
    {
        get => overrides?.MaxDepth ?? maxDepth;
        set => maxDepth = Require(value, 0, nameof(MaxDepth));
    }

    /// <summary>Items of a collection or dictionary rendered before eliding the rest. Default 32.</summary>
    public static int MaxCollectionItems
    {
        get => overrides?.MaxCollectionItems ?? maxCollectionItems;
        set => maxCollectionItems = Require(value, 1, nameof(MaxCollectionItems));
    }

    /// <summary>Characters of a string rendered before truncating. Default 512.</summary>
    public static int MaxStringLength
    {
        get => overrides?.MaxStringLength ?? maxStringLength;
        set => maxStringLength = Require(value, 1, nameof(MaxStringLength));
    }

    /// <summary>
    /// Renders each collection item, dictionary entry and member on its own line. Default false.
    /// </summary>
    public static bool UseLineBreaks
    {
        get => overrides?.UseLineBreaks ?? useLineBreaks;
        set => useLineBreaks = value;
    }

    /// <summary>
    /// Caps the lines one rendered value may produce; 0 (the default) means no cap. Only meaningful
    /// together with <see cref="UseLineBreaks"/>, since a single-line rendering produces one line.
    /// </summary>
    public static int MaxLines
    {
        get => overrides?.MaxLines ?? maxLines;
        set => maxLines = Require(value, 0, nameof(MaxLines));
    }

    /// <summary>
    /// Applies the given options on the current thread until the returned handle is disposed.
    /// Anything left null keeps whatever was in effect. Nests.
    /// </summary>
    /// <example>
    /// using (FormattingOptions.With(maxDepth: 10, useLineBreaks: true))
    /// {
    ///     deepGraph.Should().BeEquivalentTo(expected);
    /// }
    /// </example>
    /// <remarks>
    /// Thread-local, so a parallel test suite is unaffected by what one test does — see the remark on
    /// the class. Async code that awaits inside the block can resume on another thread, where the
    /// override is not in effect; keep the block synchronous around the assertion.
    /// </remarks>
    public static IDisposable With(
        int? maxDepth = null,
        int? maxCollectionItems = null,
        int? maxStringLength = null,
        bool? useLineBreaks = null,
        int? maxLines = null)
    {
        var applied = new Overrides(
            maxDepth is { } d ? Require(d, 0, nameof(MaxDepth)) : MaxDepth,
            maxCollectionItems is { } c ? Require(c, 1, nameof(MaxCollectionItems)) : MaxCollectionItems,
            maxStringLength is { } s ? Require(s, 1, nameof(MaxStringLength)) : MaxStringLength,
            useLineBreaks ?? UseLineBreaks,
            maxLines is { } l ? Require(l, 0, nameof(MaxLines)) : MaxLines,
            overrides);

        overrides = applied;
        return applied;
    }

    /// <summary>Restores every process-wide option to its default. Does not affect active <see cref="With"/> scopes.</summary>
    public static void Reset()
    {
        maxDepth = 3;
        maxCollectionItems = 32;
        maxStringLength = 512;
        maxLines = 0;
        useLineBreaks = false;
    }

    private sealed class Overrides(
        int maxDepth, int maxCollectionItems, int maxStringLength, bool useLineBreaks, int maxLines, Overrides? previous)
        : IDisposable
    {
        public int MaxDepth { get; } = maxDepth;
        public int MaxCollectionItems { get; } = maxCollectionItems;
        public int MaxStringLength { get; } = maxStringLength;
        public bool UseLineBreaks { get; } = useLineBreaks;
        public int MaxLines { get; } = maxLines;

        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Restores the scope this one replaced rather than clearing, so nesting works and a
            // double dispose cannot pop someone else's scope.
            overrides = previous;
        }
    }

    private static int Require(int value, int minimum, string name)
        => value >= minimum
            ? value
            : throw new ArgumentOutOfRangeException(name, value, $"{name} must be at least {minimum}.");
}
