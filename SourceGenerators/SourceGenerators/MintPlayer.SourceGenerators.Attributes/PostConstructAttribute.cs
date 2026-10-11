namespace MintPlayer.SourceGenerators.Attributes;

/// <summary>
/// Marks a method that the generated constructor calls last, after every injected member is assigned.
/// </summary>
/// <remarks>
/// The method must be an instance method (INJECT003), parameterless (INJECT001) and return <c>void</c> (INJECT005):
/// a constructor cannot await a <c>Task</c>, so an async method would run fire-and-forget. One per type (INJECT002).
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class PostConstructAttribute : Attribute
{
}
