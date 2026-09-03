using Solve.Services;

namespace Solve.Tests._Fakes;

/// <summary>
/// Records everything written instead of printing it, and answers prompts from a queue.
/// </summary>
/// <remarks>
/// Commands communicate almost entirely through this interface, so what lands here IS the
/// observable behaviour — assertions target it rather than the calls a command made on its way
/// there. <see cref="Output"/> is every line in order; the severity-specific lists exist for the
/// cases where "it warned" matters and the wording does not.
///
/// <see cref="Confirm"/> answers from <see cref="ConfirmResponses"/> in order and then falls back
/// to <see cref="DefaultConfirm"/>, so a test that cares about one decision does not have to
/// script the ones after it.
/// </remarks>
internal sealed class FakeConsoleService : IConsoleService
{
    public List<string> Output { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Infos { get; } = [];

    /// <summary>Answers handed to successive <see cref="Confirm"/> calls, oldest first.</summary>
    public Queue<bool> ConfirmResponses { get; } = new();

    /// <summary>Used once <see cref="ConfirmResponses"/> runs dry.</summary>
    public bool DefaultConfirm { get; set; }

    /// <summary>Answers handed to successive <see cref="Prompt"/> calls.</summary>
    public Queue<string?> PromptResponses { get; } = new();

    /// <summary>Questions actually put to the user, in order.</summary>
    public List<string> ConfirmsAsked { get; } = [];

    public bool ShowedGhInstallInstructions { get; private set; }
    public bool ShowedGhAuthInstructions { get; private set; }

    /// <summary>Everything written, joined — for a "mentions this anywhere" assertion.</summary>
    public string AllOutput => string.Join(Environment.NewLine, Output);

    public void WriteLine(string message = "") => Output.Add(message);

    public void WriteInfo(string message)
    {
        Infos.Add(message);
        Output.Add(message);
    }

    public void WriteSuccess(string message) => Output.Add(message);

    public void WriteWarning(string message)
    {
        Warnings.Add(message);
        Output.Add(message);
    }

    public void WriteError(string message)
    {
        Errors.Add(message);
        Output.Add(message);
    }

    public void WriteHeader(string message) => Output.Add(message);

    public bool Confirm(string message)
    {
        ConfirmsAsked.Add(message);
        return ConfirmResponses.Count > 0 ? ConfirmResponses.Dequeue() : DefaultConfirm;
    }

    public string? Prompt(string message)
    {
        Output.Add(message);
        return PromptResponses.Count > 0 ? PromptResponses.Dequeue() : null;
    }

    public void WriteGhInstallInstructions() => ShowedGhInstallInstructions = true;

    public void WriteGhAuthInstructions() => ShowedGhAuthInstructions = true;
}
