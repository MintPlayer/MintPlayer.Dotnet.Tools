//using MintPlayer.SourceGenerators.Tools.ValueComparers;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;

//namespace MintPlayer.ValueComparers.NewtonsoftJson;

///// <summary>
///// Value Comparer for JObject types
///// </summary>
//internal sealed class JObjectValueComparer : ValueComparer<JObject>
//{
//    protected override bool AreEqual(JObject x, JObject y)
//    {
//        return IsEquals(x.ToString(Formatting.None), y.ToString(Formatting.None));
//    }
//}