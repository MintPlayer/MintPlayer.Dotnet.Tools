#if NETSTANDARD2_0
// Polyfill for System.Runtime.CompilerServices.ModuleInitializerAttribute (missing on .NET Standard 2.0)
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
#endif
