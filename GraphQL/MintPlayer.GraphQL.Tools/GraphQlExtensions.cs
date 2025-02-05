using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Octokit.GraphQL;
using Octokit.GraphQL.Core;
using System.Text.RegularExpressions;

namespace MintPlayer.GraphQL.Tools;

public static partial class GraphQlExtensions
{
    /// <summary>
    /// Not all GraphQL queries produced by Octokit.GraphQL work seamlessly.
    /// This method resolves some problems by removing null-properties from the query.
    /// </summary>
    /// <param name="connection">GraphQL connection</param>
    /// <param name="expression">The GraphQL query to run</param>
    /// <param name="mode">Specifies whether null-values should be removed from the query.</param>
    public static async Task Run<T>(this Connection connection, IQueryableValue<T> expression, EGraphQLCleaningMode mode)
    {
        switch (mode)
        {
            case EGraphQLCleaningMode.Classic:
                await connection.Run(expression);
                break;
            case EGraphQLCleaningMode.Cleanup:
                var expr = expression.Compile().ToString() ?? string.Empty;

                // If the input:value contains null fields, the Run command returns errors, even though the action succeeded.
                foreach (var rgx in replacers)
                    expr = rgx().Replace(expr, (match) => match.Groups["c1"]?.Value == "," && match.Groups["c2"]?.Value == "," ? "," : " ");

                expr = expr
                    .Replace(Environment.NewLine, " ")
                    .Replace("\n", " ");

                var query = JsonConvert.SerializeObject(
                    new { Query = expr },
                    new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }
                );

                var result = await connection.Run(query);
                break;
        }
    }

    [GeneratedRegex(@"(?<c1>\,?)text:\s?null(?<c2>\,?)\s?")]
    private static partial Regex textRegex();

    [GeneratedRegex(@"(?<c1>\,?)number:\s?null(?<c2>\,?)\s?")]
    private static partial Regex numberRegex();

    [GeneratedRegex(@"(?<c1>\,?)date:\s?null(?<c2>\,?)\s?")]
    private static partial Regex dateRegex();

    [GeneratedRegex(@"(?<c1>\,?)singleSelectOptionId:\s?null(?<c2>\,?)\s?")]
    private static partial Regex singleSelectOptionIdRegex();

    [GeneratedRegex(@"(?<c1>\,?)iterationId:\s?null(?<c2>\,?)\s?")]
    private static partial Regex iterationIdRegex();

    private static Func<Regex>[] replacers = [textRegex, numberRegex, dateRegex, singleSelectOptionIdRegex, iterationIdRegex];
}
