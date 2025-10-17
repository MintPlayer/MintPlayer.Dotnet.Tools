//namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

//internal sealed class KeyValuePairValueComparer<TKey, TValue> : ValueComparer<KeyValuePair<TKey, TValue>>
//{
//    protected override bool AreEqual(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y)
//    {
//        if (!IsEquals(x.Key, y.Key))
//            return false;

//        if (!IsEquals(x.Value, y.Value))
//            return false;

//        return true;
//    }
//}

//internal sealed class NullableKeyValuePairValueComparer<TKey, TValue> : ValueComparer<KeyValuePair<TKey, TValue>?>
//{
//    protected override bool AreEqual(KeyValuePair<TKey, TValue>? x, KeyValuePair<TKey, TValue>? y)
//    {
//        if (!IsEquals(x!.Value.Key, y!.Value.Key))
//            return false;

//        if (!IsEquals(x!.Value.Value, y!.Value.Value))
//            return false;

//        return true;
//    }
//}