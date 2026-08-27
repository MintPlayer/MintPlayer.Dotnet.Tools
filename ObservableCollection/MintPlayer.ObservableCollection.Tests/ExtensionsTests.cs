using MintPlayer.ObservableCollection.Extensions;
using MintPlayer.ObservableCollection.Extensions.Enums;

namespace MintPlayer.ObservableCollection.Tests;

public class ExtensionsTests
{
    private static ObservableCollection<int> Range(int count)
        => new(Enumerable.Range(0, count));

    #region Add with maxItemCount

    [Fact]
    public void Add_UnderTheLimit_KeepsEverything()
    {
        var collection = Range(3);

        collection.Add(99, 10);

        collection.Should().Equal([0, 1, 2, 99]);
    }

    [Fact]
    public void Add_AtTheLimit_TrimsFromTheHead()
    {
        var collection = Range(3);

        collection.Add(99, 3);

        collection.Should().Equal([1, 2, 99]);
    }

    [Fact]
    public void Add_TrimsExactlyTheOverflow()
    {
        var collection = Range(5);

        collection.Add(99, 3);

        collection.Should().Equal([3, 4, 99]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Add_WithANonPositiveLimit_Throws(int maxItemCount)
    {
        var collection = Range(3);

        var act = () => collection.Add(99, maxItemCount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region AddDistinct

    [Fact]
    public void AddDistinct_AddsANewItemAndReportsTrue()
    {
        var collection = new ObservableCollection<string>(["a"]);

        collection.AddDistinct("b").Should().BeTrue();
        collection.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void AddDistinct_SkipsADuplicateAndReportsFalse()
    {
        var collection = new ObservableCollection<string>(["a"]);

        collection.AddDistinct("a").Should().BeFalse();
        collection.Should().Equal(["a"]);
    }

    [Fact]
    public void AddDistinct_WithALimit_TrimsAfterAdding()
    {
        var collection = Range(3);

        collection.AddDistinct(99, 3).Should().BeTrue();
        collection.Should().Equal([1, 2, 99]);
    }

    [Fact]
    public void AddDistinct_WithALimit_DoesNotTrimWhenTheItemIsADuplicate()
    {
        var collection = Range(3);

        collection.AddDistinct(1, 3).Should().BeFalse();
        collection.Should().Equal([0, 1, 2]);
    }

    [Fact]
    public void AddDistinct_WithAComparer_UsesIt()
    {
        var collection = new ObservableCollection<string>(["apple"]);

        collection.AddDistinct("avocado", new FirstLetterComparer()).Should().BeFalse();
        collection.AddDistinct("banana", new FirstLetterComparer()).Should().BeTrue();

        collection.Should().Equal(["apple", "banana"]);
    }

    [Fact]
    public void AddDistinct_WithALimitAndAComparer_TrimsAfterAdding()
    {
        var collection = new ObservableCollection<string>(["apple", "banana"]);

        collection.AddDistinct("cherry", 2, new FirstLetterComparer()).Should().BeTrue();

        collection.Should().Equal(["banana", "cherry"]);
    }

    #endregion

    #region AddRange overloads

    [Fact]
    public void AddRange_FromANonGenericEnumerable_Casts()
    {
        var collection = new ObservableCollection<int>();
        System.Collections.IEnumerable items = new List<object> { 1, 2, 3 };

        collection.AddRange(items);

        collection.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void AddRange_FromANonGenericEnumerable_WithALimit_Trims()
    {
        var collection = Range(2);
        System.Collections.IEnumerable items = new List<object> { 7, 8 };

        collection.AddRange(items, 3);

        collection.Should().Equal([1, 7, 8]);
    }

    [Fact]
    public void AddRange_Generic_WithALimit_Trims()
    {
        var collection = Range(2);

        collection.AddRange([7, 8], 3);

        collection.Should().Equal([1, 7, 8]);
    }

    #endregion

    #region AddDistinctRange - D12 regression

    /// <summary>
    /// Regression for D12 in docs/PRD-TestCoverage.md. Every AddDistinctRange overload
    /// returned the filtered query LAZILY. AddRange enumerated it to do the inserting, and
    /// then the caller's enumeration re-ran `!collection.Contains(item)` — by which point
    /// every item was in the collection. So the documented "items that were actually added"
    /// return value was always empty.
    /// </summary>
    [Fact]
    public void AddDistinctRange_ReturnsTheItemsItActuallyAdded()
    {
        var collection = new ObservableCollection<string>(["a"]);

        var added = collection.AddDistinctRange(["a", "b", "c"]);

        added.Should().Equal(["b", "c"]);
        collection.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void AddDistinctRange_ReturnValueIsStableAcrossRepeatedEnumeration()
    {
        var collection = new ObservableCollection<string>();

        var added = collection.AddDistinctRange(["x", "y"]);

        added.Should().Equal(["x", "y"]);
        added.Should().Equal(["x", "y"]);
    }

    [Fact]
    public void AddDistinctRange_DeduplicatesWithinTheIncomingItems()
    {
        var collection = new ObservableCollection<string>();

        var added = collection.AddDistinctRange(["a", "a", "b"]);

        added.Should().Equal(["a", "b"]);
        collection.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void AddDistinctRange_WhenEverythingIsADuplicate_AddsNothing()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);

        var added = collection.AddDistinctRange(["a", "b"]);

        added.Should().BeEmpty();
        collection.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void AddDistinctRange_WithALimit_TrimsAndStillReportsWhatWasAdded()
    {
        var collection = Range(3);

        var added = collection.AddDistinctRange([7, 8], 3);

        added.Should().Equal([7, 8]);
        collection.Should().Equal([2, 7, 8]);
    }

    [Fact]
    public void AddDistinctRange_FromANonGenericEnumerable_Works()
    {
        var collection = new ObservableCollection<int>([1]);
        System.Collections.IEnumerable items = new List<object> { 1, 2, 2, 3 };

        var added = collection.AddDistinctRange<int>(items);

        added.Should().Equal([2, 3]);
        collection.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void AddDistinctRange_FromANonGenericEnumerable_WithALimit_Works()
    {
        var collection = new ObservableCollection<int>([1]);
        System.Collections.IEnumerable items = new List<object> { 2, 3 };

        var added = collection.AddDistinctRange<int>(items, 2);

        added.Should().Equal([2, 3]);
        collection.Should().Equal([2, 3]);
    }

    [Fact]
    public void AddDistinctRange_WithAComparer_Works()
    {
        var collection = new ObservableCollection<string>(["apple"]);

        var added = collection.AddDistinctRange(["avocado", "banana", "blueberry"], new FirstLetterComparer());

        added.Should().Equal(["banana"]);
        collection.Should().Equal(["apple", "banana"]);
    }

    [Fact]
    public void AddDistinctRange_WithAComparerAndALimit_Works()
    {
        var collection = new ObservableCollection<string>(["apple"]);

        var added = collection.AddDistinctRange(["banana", "cherry"], 2, new FirstLetterComparer());

        added.Should().Equal(["banana", "cherry"]);
        collection.Should().Equal(["banana", "cherry"]);
    }

    [Fact]
    public void AddDistinctRange_FromANonGenericEnumerable_WithAComparer_Works()
    {
        var collection = new ObservableCollection<string>(["apple"]);
        System.Collections.IEnumerable items = new List<object> { "avocado", "banana" };

        var added = collection.AddDistinctRange(items, new FirstLetterComparer());

        added.Should().Equal(["banana"]);
    }

    [Fact]
    public void AddDistinctRange_FromANonGenericEnumerable_WithAComparerAndALimit_Works()
    {
        var collection = new ObservableCollection<string>(["apple"]);
        System.Collections.IEnumerable items = new List<object> { "banana", "cherry" };

        var added = collection.AddDistinctRange(items, 2, new FirstLetterComparer());

        added.Should().Equal(["banana", "cherry"]);
        collection.Should().Equal(["banana", "cherry"]);
    }

    #endregion

    #region Insert with maxItemCount

    [Fact]
    public void Insert_NearTheTail_TrimsFromTheHead()
    {
        // index >= half the count, so the head is trimmed.
        var collection = Range(4);

        collection.Insert(3, 99, 4);

        collection.Should().Equal([1, 2, 99, 3]);
    }

    [Fact]
    public void Insert_NearTheHead_TrimsFromTheTail()
    {
        // index < half the count, so the tail is trimmed.
        var collection = Range(4);

        collection.Insert(0, 99, 4);

        collection.Should().Equal([99, 0, 1, 2]);
    }

    [Fact]
    public void Insert_WithAnExplicitSide_HonoursIt()
    {
        var collection = Range(4);

        collection.Insert(0, 99, 4, ECollectionSide.Head);

        collection.Should().Equal([0, 1, 2, 3]);
    }

    [Fact]
    public void Insert_WithAnExplicitTailSide_HonoursIt()
    {
        var collection = Range(4);

        collection.Insert(0, 99, 4, ECollectionSide.Tail);

        collection.Should().Equal([99, 0, 1, 2]);
    }

    [Fact]
    public void Insert_WithAnInvalidSide_Throws()
    {
        var collection = Range(4);

        var act = () => collection.Insert(0, 99, 2, (ECollectionSide)42);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region InsertDistinct

    [Fact]
    public void InsertDistinct_InsertsANewItem()
    {
        var collection = new ObservableCollection<string>(["a", "c"]);

        collection.InsertDistinct(1, "b").Should().BeTrue();
        collection.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void InsertDistinct_SkipsADuplicate()
    {
        var collection = new ObservableCollection<string>(["a", "b"]);

        collection.InsertDistinct(0, "b").Should().BeFalse();
        collection.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void InsertDistinct_WithALimit_Trims()
    {
        var collection = Range(4);

        collection.InsertDistinct(3, 99, 4).Should().BeTrue();
        collection.Should().Equal([1, 2, 99, 3]);
    }

    [Fact]
    public void InsertDistinct_WithALimit_SkipsADuplicateWithoutTrimming()
    {
        var collection = Range(4);

        collection.InsertDistinct(0, 2, 4).Should().BeFalse();
        collection.Should().Equal([0, 1, 2, 3]);
    }

    [Fact]
    public void InsertDistinct_WithALimitAndAnExplicitSide_Trims()
    {
        var collection = Range(4);

        collection.InsertDistinct(0, 99, 4, ECollectionSide.Head).Should().BeTrue();
        collection.Should().Equal([0, 1, 2, 3]);
    }

    #endregion

    #region RemoveRange by index

    [Fact]
    public void RemoveRange_ByIndex_RemovesTheSlice()
    {
        var collection = Range(5);

        collection.RemoveRange(1, 2);

        collection.Should().Equal([0, 3, 4]);
    }

    [Fact]
    public void RemoveRange_ByIndex_WithZeroCount_IsANoOp()
    {
        var collection = Range(3);

        collection.RemoveRange(1, 0);

        collection.Should().Equal([0, 1, 2]);
    }

    [Fact]
    public void RemoveRange_ByIndex_ClampsACountPastTheEnd()
    {
        var collection = Range(3);

        collection.RemoveRange(1, 99);

        collection.Should().Equal([0]);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    public void RemoveRange_ByIndex_WithNegativeArguments_Throws(int start, int count)
    {
        var collection = Range(3);

        var act = () => collection.RemoveRange(start, count);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RemoveRange_ByIndex_PastTheEnd_Throws()
    {
        var collection = Range(3);

        var act = () => collection.RemoveRange(5, 1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("start");
    }

    /// <summary>
    /// Characterization, and a known defect (D13 in docs/PRD-TestCoverage.md) that is
    /// deliberately NOT fixed here. RemoveRange(start, count) resolves the slice by index
    /// but hands it to the value-based RemoveRange, which removes the FIRST match of each
    /// item — so with duplicates it deletes the wrong positions. Fixing it needs
    /// index-range removal on the base collection, which would change one batched
    /// notification into N single ones; that is the owner's call, not a test's.
    /// </summary>
    [Fact]
    public void RemoveRange_ByIndex_WithDuplicates_RemovesTheFirstMatchNotTheSlice()
    {
        var collection = new ObservableCollection<string>(["a", "b", "a", "c"]);

        collection.RemoveRange(2, 1);

        // The slice at index 2 is "a", and the first "a" (index 0) is what goes.
        collection.Should().Equal(["b", "a", "c"]);
    }

    #endregion

    #region Ported console-harness scenario

    [Fact]
    public void TheOldMaxItemCountDemo_TrimsFromTheExpectedSides()
    {
        const int maxItemCount = 20;
        var collection = new ObservableCollection<int>();
        collection.AddRange(Enumerable.Range(0, maxItemCount));

        collection.Add(20, maxItemCount);
        collection.Should().HaveCount(maxItemCount);
        collection.Should().NotContain(0);
        collection.Should().Contain(20);

        collection.Insert(1, 21, maxItemCount);
        collection.Should().HaveCount(maxItemCount);
        collection[1].Should().Be(21);

        collection.Insert(maxItemCount / 2, 22, maxItemCount);
        collection.Should().HaveCount(maxItemCount);
        collection.Should().Contain(22);

        collection.Insert(maxItemCount / 2 - 1, 23, maxItemCount);
        collection.Should().HaveCount(maxItemCount);
        collection.Should().Contain(23);
    }

    #endregion
}
