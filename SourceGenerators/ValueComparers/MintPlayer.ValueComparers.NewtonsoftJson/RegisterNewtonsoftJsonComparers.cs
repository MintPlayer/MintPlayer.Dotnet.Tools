//using MintPlayer.SourceGenerators.Tools.ValueComparers;
//using Newtonsoft.Json.Linq;

//namespace MintPlayer.ValueComparers.NewtonsoftJson;

////public static class NewtonsoftJsonComparers
////{
////    private static bool isRegistered = false;
////    private static readonly object mutex = new();

////    public static void Register()
////    {
////        lock (mutex)
////        {
////            if (isRegistered) return;
////            isRegistered = true;

////            ValueComparer<JObject>.RegisterCustomComparer<JObjectValueComparer>();
////        }
////    }
////}