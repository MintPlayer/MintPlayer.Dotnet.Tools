using MintPlayer.SourceGenerators.Tools.ValueComparers;
using Newtonsoft.Json.Linq;

namespace MintPlayer.ValueComparers.NewtonsoftJson;

public static class NewtonsoftJsonComparers
{
    private static bool isRegistered = false;
    public static void Register()
    {
        if (isRegistered) return;
        isRegistered = true;

        ValueComparer<JObject>.RegisterCustomComparer<JObjectValueComparer>();
    }
}
