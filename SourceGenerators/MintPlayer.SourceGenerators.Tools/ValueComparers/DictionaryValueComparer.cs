//namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

//internal sealed class DictionaryValueComparer<TKey, TValue> : ValueComparer<Dictionary<TKey, TValue>>
//    where TKey : notnull
//{
//    protected override bool AreEqual(Dictionary<TKey, TValue> x, Dictionary<TKey, TValue> y)
//    {
//        if (!IsEquals(x.Count, y.Count))
//            return false;

//        if (x.Keys.Except(y.Keys).Any())
//            return false;

//        if (y.Keys.Except(x.Keys).Any())
//            return false;

//        foreach (var item in x)
//        {
//            if (!IsEquals(item.Value, y[item.Key]))
//                return false;
//        }

//        return true;
//    }
//}