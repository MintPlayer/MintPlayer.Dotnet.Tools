//namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

//internal sealed class IEnumerableValueComparer<TValue> : ValueComparer<IEnumerable<TValue>>
//{
//    protected override bool AreEqual(IEnumerable<TValue> x, IEnumerable<TValue> y)
//    {
//        using var enumX = x.GetEnumerator();
//        using var enumY = y.GetEnumerator();

//        while (true)
//        {
//            bool moveNextX = enumX.MoveNext();
//            bool moveNextY = enumY.MoveNext();

//            if (moveNextX != moveNextY)
//                return false;

//            if (!moveNextX)
//                return true;

//            if (!IsEquals(enumX.Current, enumY.Current))
//                return false;
//        }
//    }
//}
