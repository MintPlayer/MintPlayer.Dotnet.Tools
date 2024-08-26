namespace MintPlayer.GraphQL.Tools;

public enum EGraphQLCleaningMode
{
    Classic,

    /// <summary>
    /// <list type="bullet">
    /// <item>Removes null-values which would cause a failed result</item>
    /// <item>Removes newlines which cause an exception</item>
    /// <item>Wraps the expression inside a new object on property "query"</item>
    /// <item>Changes to camelCasing</item>
    /// </list>
    /// </summary>
    Cleanup,
}
