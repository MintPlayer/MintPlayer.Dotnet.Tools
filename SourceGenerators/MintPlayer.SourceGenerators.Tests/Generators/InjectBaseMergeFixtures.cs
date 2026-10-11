namespace MintPlayer.SourceGenerators.Tests.Generators.InjectBaseMergeFixtures;

// Compiled base classes for InjectBaseMergeTests: a fixture derives from them through a metadata reference to this
// assembly, so the generator sees their constructors as symbols from metadata, not from source.

public interface IMa { }
public sealed class Ma : IMa { }
public interface IMb { }
public sealed class Mb : IMb { }

/// <summary>Declares its dependencies in the opposite order to the fixture deriving from it.</summary>
public class MetadataBase
{
    public MetadataBase(IMb b, IMa a)
    {
        B = b;
        A = a;
    }

    public IMb B { get; }
    public IMa A { get; }
}

/// <summary>A compiled constructor whose last parameter has a default value.</summary>
public class MetadataBaseWithDefault
{
    public MetadataBaseWithDefault(IMa a, IMb? b = null)
    {
        A = a;
        B = b;
    }

    public IMa A { get; }
    public IMb? B { get; }
}
