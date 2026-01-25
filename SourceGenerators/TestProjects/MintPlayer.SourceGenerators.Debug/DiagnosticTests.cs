using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

// NOTE: Uncomment these lines to test the diagnostic errors

// ERROR REGISTER001: Using Pattern 1 constructor (ServiceLifetime only) on assembly
// Pattern 1 is class-only, requires a class to be the implementation
// Uncomment to test:
// [assembly: Register(ServiceLifetime.Scoped)]

namespace DiagnosticTests
{
    public interface IWrongUsage { }
    public class WrongUsage : IWrongUsage { }

    // ERROR REGISTER002: Using Pattern 4 constructor (3 types) on class
    // Pattern 4 is assembly-only, class doesn't need to specify implementation type
    // Uncomment to test:
    // [Register(typeof(IWrongUsage), typeof(WrongUsage), ServiceLifetime.Scoped)]
    // public class WrongClassUsage : IWrongUsage { }
}
