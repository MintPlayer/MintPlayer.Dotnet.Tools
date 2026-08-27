namespace MintPlayer.Assertions;

/// <summary>
/// Marks a static bool-returning method (first parameter = the subject) for which the source
/// generator emits a fluent assertion extension. A method <c>static bool IsEven(int value)</c>
/// produces <c>value.Should().BeEven()</c> (name derived from the method, overridable via
/// <see cref="Name"/>), including because/becauseArgs support and a formatted failure message.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GenerateAssertionAttribute : Attribute
{
    /// <summary>Overrides the generated assertion method name (default: derived from the method name, e.g. IsEven → BeEven).</summary>
    public string? Name { get; set; }
}
