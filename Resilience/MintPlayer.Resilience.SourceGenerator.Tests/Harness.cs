using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace MintPlayer.Resilience.SourceGenerator.Tests;

/// <summary>The one <see cref="GeneratorHarness"/> every generator test in this project uses.</summary>
/// <remarks>
/// Fixtures are compiled against the runtime (<c>MintPlayer.Resilience</c>: the attributes and everything the generated
/// code calls), <c>System.Threading.RateLimiting</c> (the limiter options the generated code creates), logging (the
/// DI-form sample) and <c>System.Net.Http</c> (the PRD sample's result type). Without them the generated code does not
/// compile and every assertion would fail for the wrong reason.
/// </remarks>
internal static class Harness
{
    public const string Generator = "ResiliencePipelineGenerator";

    public static readonly GeneratorHarness Instance = GeneratorHarness
        .ForAssembly("MintPlayer.Resilience.SourceGenerator")
        .AddReferences(typeof(ResiliencePipelineAttribute), typeof(RateLimiter), typeof(ILogger), typeof(HttpResponseMessage));

    /// <summary>The usings every small fixture starts with.</summary>
    public const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using MintPlayer.Resilience;
        using MintPlayer.Resilience.CircuitBreaker;
        using MintPlayer.Resilience.Fallback;
        using MintPlayer.Resilience.Retry;
        using MintPlayer.Resilience.Timeout;

        """;

    /// <summary>The embedded copy of a file under <c>Samples/</c> or <c>Snapshots/</c>.</summary>
    public static string Resource(string logicalName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"The embedded resource '{logicalName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Every sample source, in file-name order.</summary>
    public static string[] Samples() => Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(n => n.StartsWith("Samples.", StringComparison.Ordinal))
        .OrderBy(n => n, StringComparer.Ordinal)
        .Select(Resource)
        .ToArray();

    /// <summary>Line endings normalized, so a snapshot compares equal on every platform and checkout setting.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
