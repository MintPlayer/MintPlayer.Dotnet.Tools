namespace MintPlayer.StringBuilder.Extensions.SplitLines;

public ref struct LineSplitEnumerator
{
    private ReadOnlySpan<char> _str;

    public LineSplitEnumerator(ReadOnlySpan<char> str)
    {
        _str = str;
        Current = default;
    }

    // Needed to be compatible with the foreach operator
    public LineSplitEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        var span = _str;
        if (span.Length == 0) // Reach the end of the string
            return false;

        var index = span.IndexOfAny('\r', '\n');
        if (index == -1) // The string is composed of only one line
        {
            _str = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
            Current = new LineSplitEntry(span, ReadOnlySpan<char>.Empty);
            return true;
        }

        if (index < span.Length - 1 && span[index] == '\r')
        {
            // Try to consume the '\n' associated to the '\r'
            var next = span[index + 1];
            if (next == '\n')
            {
                Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 2));
                _str = span.Slice(index + 2);
                return true;
            }
        }

        Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 1));
        _str = span.Slice(index + 1);
        return true;
    }

    public LineSplitEntry Current { get; private set; }
}