using MintPlayer.SlnLaunch.Models;

namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Options that influence how <see cref="ILaunchPlanBuilder"/> constructs each <c>dotnet</c> invocation.
/// </summary>
public sealed class LaunchPlanOptions
{
    /// <summary>Use <c>dotnet watch</c> instead of <c>dotnet run</c>.</summary>
    public bool Watch { get; init; }

    /// <summary>Forwarded to every project as <c>--configuration</c>.</summary>
    public string? Configuration { get; init; }

    /// <summary>Forwarded to every project as <c>--framework</c>.</summary>
    public string? Framework { get; init; }

    /// <summary>Forwarded to every project as <c>--no-build</c>.</summary>
    public bool NoBuild { get; init; }

    /// <summary>Forwarded to every project as <c>--verbosity</c>.</summary>
    public string? Verbosity { get; init; }

    /// <summary>The pool of post-<c>--</c> arguments each project selects from via its ForwardArguments.</summary>
    public ForwardableArguments ForwardableArguments { get; init; } = ForwardableArguments.Empty;
}
