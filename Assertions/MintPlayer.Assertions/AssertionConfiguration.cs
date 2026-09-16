namespace MintPlayer.Assertions;

/// <summary>
/// Process-wide configuration for how a failed assertion surfaces.
/// </summary>
public static class AssertionConfiguration
{
    /// <summary>
    /// Builds the exception a failed assertion throws. Defaults to
    /// <see cref="AssertionFailedException"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the answer to "should the library throw the test framework's own exception type"
    /// (PRD §13, open question 1), and the answer is: it should let you say so, and never guess.
    /// FluentAssertions probes the loaded assemblies for <c>Xunit.Sdk.XunitException</c>,
    /// <c>NUnit.Framework.AssertionException</c> and friends, and constructs whichever it finds. That
    /// is a reflective type lookup by name, which is precisely what an AOT- and trimming-friendly
    /// library cannot rely on: after trimming the probe finds nothing and silently falls back, so the
    /// behaviour depends on the build rather than on the test framework.
    /// </para>
    /// <para>
    /// One line in a fixture says it explicitly and works under every publish mode:
    /// <code>
    /// AssertionConfiguration.ExceptionFactory = message => new Xunit.Sdk.XunitException(message);
    /// </code>
    /// </para>
    /// <para>
    /// It matters less than it looks. Every runner reports an unexpected exception as a failed test;
    /// the native type only changes how the result is categorised and rendered.
    /// </para>
    /// </remarks>
    public static Func<string, Exception> ExceptionFactory { get; set; } = static message => new AssertionFailedException(message);

    /// <summary>Builds the exception for <paramref name="message"/>, falling back when a factory misbehaves.</summary>
    internal static Exception BuildException(string message)
    {
        try
        {
            return ExceptionFactory(message) ?? new AssertionFailedException(message);
        }
        catch (Exception ex)
        {
            // A broken factory must not replace the assertion failure with its own error — the
            // caller would be left debugging the wrong thing entirely.
            return new AssertionFailedException(
                $"{message}{Environment.NewLine}{Environment.NewLine}(AssertionConfiguration.ExceptionFactory threw {ex.GetType().Name}: {ex.Message})");
        }
    }
}
