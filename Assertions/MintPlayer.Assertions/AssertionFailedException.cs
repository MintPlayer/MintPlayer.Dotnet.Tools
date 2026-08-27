namespace MintPlayer.Assertions;

/// <summary>
/// Thrown when an assertion fails. All test frameworks (xUnit, NUnit, MSTest, TUnit) render
/// unknown exception types as test failures, so no framework-specific exception is needed.
/// </summary>
public class AssertionFailedException : Exception
{
    public AssertionFailedException(string message) : base(message) { }
    public AssertionFailedException(string message, Exception innerException) : base(message, innerException) { }
}
