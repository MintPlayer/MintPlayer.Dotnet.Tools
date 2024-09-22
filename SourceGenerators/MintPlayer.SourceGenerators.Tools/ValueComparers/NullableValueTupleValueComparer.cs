namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal sealed class NullableValueTupleValueComparer<T1, T2> : ValueComparer<(T1, T2)?>
{
    protected override bool AreEqual((T1, T2)? x, (T1, T2)? y)
    {
        if (!IsEquals(x!.Value.Item1, y!.Value.Item1))
            return false;

        if (!IsEquals(x!.Value.Item2, y!.Value.Item2))
            return false;

        return true;
    }
}

internal sealed class NullableValueTupleValueComparer<T1, T2, T3> : ValueComparer<(T1, T2, T3)?>
{
    protected override bool AreEqual((T1, T2, T3)? x, (T1, T2, T3)? y)
    {
        if (!IsEquals(x!.Value.Item1, y!.Value.Item1))
            return false;

        if (!IsEquals(x!.Value.Item2, y!.Value.Item2))
            return false;

        if (!IsEquals(x!.Value.Item3, y!.Value.Item3))
            return false;

        return true;
    }
}

internal sealed class NullableValueTupleValueComparer<T1, T2, T3, T4> : ValueComparer<(T1, T2, T3, T4)?>
{
    protected override bool AreEqual((T1, T2, T3, T4)? x, (T1, T2, T3, T4)? y)
    {
        if (!IsEquals(x!.Value.Item1, y!.Value.Item1))
            return false;

        if (!IsEquals(x!.Value.Item2, y!.Value.Item2))
            return false;

        if (!IsEquals(x!.Value.Item3, y!.Value.Item3))
            return false;

        if (!IsEquals(x!.Value.Item4, y!.Value.Item4))
            return false;

        return true;
    }
}

internal sealed class NullableValueTupleValueComparer<T1, T2, T3, T4, T5> : ValueComparer<(T1, T2, T3, T4, T5)?>
{
    protected override bool AreEqual((T1, T2, T3, T4, T5)? x, (T1, T2, T3, T4, T5)? y)
    {
        if (!IsEquals(x!.Value.Item1, y!.Value.Item1))
            return false;

        if (!IsEquals(x!.Value.Item2, y!.Value.Item2))
            return false;

        if (!IsEquals(x!.Value.Item3, y!.Value.Item3))
            return false;

        if (!IsEquals(x!.Value.Item4, y!.Value.Item4))
            return false;
        
        if (!IsEquals(x!.Value.Item5, y!.Value.Item5))
            return false;

        return true;
    }
}

internal sealed class NullableValueTupleValueComparer<T1, T2, T3, T4, T5, T6> : ValueComparer<(T1, T2, T3, T4, T5, T6)?>
{
    protected override bool AreEqual((T1, T2, T3, T4, T5, T6)? x, (T1, T2, T3, T4, T5, T6)? y)
    {
        if (!IsEquals(x!.Value.Item1, y!.Value.Item1))
            return false;

        if (!IsEquals(x!.Value.Item2, y!.Value.Item2))
            return false;

        if (!IsEquals(x!.Value.Item3, y!.Value.Item3))
            return false;

        if (!IsEquals(x!.Value.Item4, y!.Value.Item4))
            return false;
        
        if (!IsEquals(x!.Value.Item5, y!.Value.Item5))
            return false;
        
        if (!IsEquals(x!.Value.Item6, y!.Value.Item6))
            return false;

        return true;
    }
}
