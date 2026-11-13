namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// A lock-free rolling counter over the last <c>N</c> time segments: the sliding-window limiter and the
/// retry budget are built on it. Each of the <c>N</c> slots is one <see cref="long"/> packing the segment
/// number it currently counts (high 32 bits, wrapping) and its count (low 32 bits), so a slot is recycled
/// for a new segment with a single compare-and-swap and no reader ever sees a half-updated slot.
/// </summary>
/// <remarks>
/// Segment numbers are compared by wrapping distance, so the counter keeps working after 2³² segments;
/// only a slot left untouched for exactly a multiple of 2³² segments could alias, which is ignored.
/// Never allocates after construction.
/// </remarks>
internal sealed class SegmentedCounter
{
    private readonly long[] _slots;
    private readonly int _segments;

    public SegmentedCounter(int segments)
    {
        _segments = segments;
        _slots = new long[segments];
    }

    /// <summary>The number of segments the window spans.</summary>
    public int Segments => _segments;

    /// <summary>
    /// Adds one to the count of <paramref name="segment"/>, recycling its slot when it still holds an
    /// older segment. Returns <see langword="false"/> (and changes nothing) when the slot already holds a
    /// newer segment, i.e. the caller's clock reading is stale; the caller then reads the clock again.
    /// </summary>
    public bool TryIncrement(long segment)
    {
        var id = (uint)segment;
        ref var slot = ref _slots[Index(segment)];
        while (true)
        {
            var current = Volatile.Read(ref slot);
            var currentId = (uint)(current >>> 32);
            long next;
            if (currentId == id)
            {
                next = current + 1;
            }
            else if ((int)(id - currentId) > 0)
            {
                next = Pack(id, 1);
            }
            else
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref slot, next, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Takes back one increment of <paramref name="segment"/>. When the slot has moved on to a newer
    /// segment the increment has expired with its segment, and there is nothing to take back.
    /// </summary>
    public void Decrement(long segment)
    {
        var id = (uint)segment;
        ref var slot = ref _slots[Index(segment)];
        while (true)
        {
            var current = Volatile.Read(ref slot);
            if ((uint)(current >>> 32) != id || (int)current == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref slot, current - 1, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>The total count of the window that ends with <paramref name="segment"/> (it and the <c>N − 1</c> before it).</summary>
    public long Sum(long segment)
    {
        var id = (uint)segment;
        long sum = 0;
        var slots = _slots;
        for (var i = 0; i < slots.Length; i++)
        {
            var value = Volatile.Read(ref slots[i]);
            var age = (int)(id - (uint)(value >>> 32));
            if (age >= 0 && age < _segments)
            {
                sum += (uint)value;
            }
        }

        return sum;
    }

    /// <summary>
    /// How many segments after <paramref name="segment"/> the oldest non-empty segment of its window
    /// expires (1 = when the next segment starts); 0 when the window is empty.
    /// </summary>
    public int SegmentsUntilOldestExpires(long segment)
    {
        var id = (uint)segment;
        var oldest = -1;
        var slots = _slots;
        for (var i = 0; i < slots.Length; i++)
        {
            var value = Volatile.Read(ref slots[i]);
            var age = (int)(id - (uint)(value >>> 32));
            if ((uint)value != 0 && age >= 0 && age < _segments && age > oldest)
            {
                oldest = age;
            }
        }

        return oldest < 0 ? 0 : _segments - oldest;
    }

    private int Index(long segment) => (int)((ulong)segment % (ulong)_segments);

    private static long Pack(uint id, uint count) => ((long)id << 32) | count;
}
