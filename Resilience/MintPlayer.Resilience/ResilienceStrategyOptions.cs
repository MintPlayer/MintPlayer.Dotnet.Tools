using System.ComponentModel.DataAnnotations;

namespace MintPlayer.Resilience;

/// <summary>Base class of every strategy's options.</summary>
public abstract class ResilienceStrategyOptions
{
    /// <summary>Gets or sets the name of the strategy, used by telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>Checks the options when they are added to a builder; throws <see cref="ValidationException"/> when they are invalid.</summary>
    internal virtual void Validate()
    {
    }

    /// <summary>Throws the <see cref="ValidationException"/> Polly throws for invalid options.</summary>
    private protected void Invalid(string error)
        => throw new ValidationException($"The '{GetType().Name}' are invalid.{Environment.NewLine}{Environment.NewLine}Validation Errors:{Environment.NewLine}{error}");

    private protected void RequireRange(TimeSpan value, TimeSpan min, TimeSpan max, string name)
    {
        if (value < min || value > max)
        {
            Invalid($"The field {name} must be between {min} and {max}.");
        }
    }

    private protected void RequireNotNull(object? value, string name)
    {
        if (value is null)
        {
            Invalid($"The {name} field is required.");
        }
    }
}
