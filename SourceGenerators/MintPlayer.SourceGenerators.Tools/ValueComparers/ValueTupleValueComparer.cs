//namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

//internal sealed class ValueTupleValueComparer<T1, T2> : ValueComparer<(T1, T2)>
//{
//    protected override bool AreEqual((T1, T2) x, (T1, T2) y)
//    {
//        if (!IsEquals(x.Item1, y.Item1))
//            return false;

//        if (!IsEquals(x.Item2, y.Item2))
//            return false;

//        return true;
//    }
//}

//internal sealed class ValueTupleValueComparer<T1, T2, T3> : ValueComparer<(T1, T2, T3)>
//{
//    protected override bool AreEqual((T1, T2, T3) x, (T1, T2, T3) y)
//    {
//        if (!IsEquals(x.Item1, y.Item1))
//            return false;

//        if (!IsEquals(x.Item2, y.Item2))
//            return false;

//        if (!IsEquals(x.Item3, y.Item3))
//            return false;

//        return true;
//    }
//}

//internal sealed class ValueTupleValueComparer<T1, T2, T3, T4> : ValueComparer<(T1, T2, T3, T4)>
//{
//    protected override bool AreEqual((T1, T2, T3, T4) x, (T1, T2, T3, T4) y)
//    {
//        if (!IsEquals(x.Item1, y.Item1))
//            return false;

//        if (!IsEquals(x.Item2, y.Item2))
//            return false;

//        if (!IsEquals(x.Item3, y.Item3))
//            return false;

//        if (!IsEquals(x.Item4, y.Item4))
//            return false;

//        return true;
//    }
//}

//internal sealed class ValueTupleValueComparer<T1, T2, T3, T4, T5> : ValueComparer<(T1, T2, T3, T4, T5)>
//{
//    protected override bool AreEqual((T1, T2, T3, T4, T5) x, (T1, T2, T3, T4, T5) y)
//    {
//        if (!IsEquals(x.Item1, y.Item1))
//            return false;

//        if (!IsEquals(x.Item2, y.Item2))
//            return false;

//        if (!IsEquals(x.Item3, y.Item3))
//            return false;

//        if (!IsEquals(x.Item4, y.Item4))
//            return false;
        
//        if (!IsEquals(x.Item5, y.Item5))
//            return false;

//        return true;
//    }
//}

//internal sealed class ValueTupleValueComparer<T1, T2, T3, T4, T5, T6> : ValueComparer<(T1, T2, T3, T4, T5, T6)>
//{
//    protected override bool AreEqual((T1, T2, T3, T4, T5, T6) x, (T1, T2, T3, T4, T5, T6) y)
//    {
//        if (!IsEquals(x.Item1, y.Item1))
//            return false;

//        if (!IsEquals(x.Item2, y.Item2))
//            return false;

//        if (!IsEquals(x.Item3, y.Item3))
//            return false;

//        if (!IsEquals(x.Item4, y.Item4))
//            return false;
        
//        if (!IsEquals(x.Item5, y.Item5))
//            return false;
        
//        if (!IsEquals(x.Item6, y.Item6))
//            return false;

//        return true;
//    }
//}