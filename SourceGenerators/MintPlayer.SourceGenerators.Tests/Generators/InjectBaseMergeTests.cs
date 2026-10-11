using System.Reflection;
using MintPlayer.SourceGenerators.Tests._Infrastructure;
using MintPlayer.SourceGenerators.Tests.Generators.InjectBaseMergeFixtures;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// One constructor parameter per service type (#196): a dependency is identified by its type, compared with
/// <c>SymbolEqualityComparer</c> and ignoring nullability, never by its member name. The <c>base(...)</c> call maps
/// every slot of the base constructor's signature to the derived parameter of that slot's type, so a base that
/// declares its dependencies in a different order never gets them swapped.
/// </summary>
/// <remarks>
/// Every runtime check builds the generated type with one distinct instance per parameter type, then asserts which
/// instance landed in which member, so a swapped binding fails here instead of compiling silently.
/// </remarks>
public class InjectBaseMergeTests
{
    private const string Generator = "InjectSourceGenerator";

    private static GeneratorRun Run(string source) => GeneratorHarness.Run(Generator, [Dependencies + source], "Demo",
        referenceTypes: [typeof(MetadataBase)]);

    private const string Dependencies = """
        #nullable enable
        using MintPlayer.SourceGenerators.Attributes;
        using Microsoft.Extensions.Configuration;
        using F = MintPlayer.SourceGenerators.Tests.Generators.InjectBaseMergeFixtures;

        namespace Demo;

        public interface IA { }
        public sealed class A : IA { }
        public interface IB { }
        public sealed class B : IB { }
        public interface IDep { }
        public sealed class Dep : IDep { }
        public interface IDep1 { }
        public sealed class Dep1 : IDep1 { }
        public interface IDep2 { }
        public sealed class Dep2 : IDep2 { }
        public interface IOther { }
        public sealed class Other : IOther { }
        public interface IRepo<T> { }
        public sealed class Repo<T> : IRepo<T> { }
        public sealed class Car { }

        """;

    #region Merging by type

    [Fact]
    public void BaseNullable_DerivedNonNullable_IsOneRequiredParameter()
    {
        var run = Run("""
            public partial class Base { [Inject] private readonly IDep? dep; }
            public partial class Derived : Base { [Inject] private readonly IDep dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IDep dep)")
            .And.Contain(": base(dep)");

        var (derived, args) = Construct(run, "Demo.Derived");
        Field(derived, "Derived", "dep").Should().BeSameAs(args["IDep"]);
        Field(derived, "Base", "dep").Should().BeSameAs(args["IDep"]);
    }

    [Fact]
    public void BaseNonNullable_DerivedNullable_IsOneRequiredParameter()
    {
        var run = Run("""
            public partial class Base { [Inject] private readonly IDep dep; }
            public partial class Derived : Base { [Inject] private readonly IDep? dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IDep dep)")
            .And.Contain(": base(dep)");
    }

    [Fact]
    public void BothNullable_IsOneOptionalParameter()
    {
        var run = Run("""
            public partial class Base { [Inject] private readonly IDep? dep; }
            public partial class Derived : Base { [Inject] private readonly IDep? dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IDep? dep = default)");
    }

    [Fact]
    public void SameType_DifferentNames_IsOneParameter_NamedAfterTheDerivedMember()
    {
        var run = Run("""
            public partial class Base { [Inject] private readonly IDep dep; }
            public partial class Derived : Base { [Inject] private readonly IDep dep2; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IDep dep2)")
            .And.Contain(": base(dep2)");

        var (derived, args) = Construct(run, "Demo.Derived");
        Field(derived, "Derived", "dep2").Should().BeSameAs(args["IDep"]);
        Field(derived, "Base", "dep").Should().BeSameAs(args["IDep"]);
    }

    [Fact]
    public void SameName_DifferentTypes_AreTwoParameters_TheLaterOneSuffixed()
    {
        var run = Run("""
            public partial class Base { [Inject] private readonly IDep dep; }
            public partial class Derived : Base { [Inject] private readonly IOther dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IOther dep, global::Demo.IDep dep2)")
            .And.Contain(": base(dep2)");

        var (derived, args) = Construct(run, "Demo.Derived");
        Field(derived, "Derived", "dep").Should().BeSameAs(args["IOther"]);
        Field(derived, "Base", "dep").Should().BeSameAs(args["IDep"]);
    }

    [Fact]
    public void TwoMembersOfOneTypeInOneClass_ShareOneParameter()
    {
        var run = Run("""
            public partial class Service
            {
                [Inject] private readonly IDep first;
                [Inject] private readonly IDep? second;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Service(global::Demo.IDep first)")
            .And.Contain("this.second = first;");

        var (service, args) = Construct(run, "Demo.Service");
        Field(service, "Service", "first").Should().BeSameAs(args["IDep"]);
        Field(service, "Service", "second").Should().BeSameAs(args["IDep"]);
    }

    [Fact]
    public void AGenericBase_MergesOnTheConstructedType()
    {
        var run = Run("""
            public partial class Base<T> { [Inject] private readonly IRepo<T>? repo; }
            public partial class Derived : Base<Car> { [Inject] private readonly IRepo<Car> repo; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IRepo<global::Demo.Car> repo)")
            .And.Contain(": base(repo)");

        var (derived, args) = Construct(run, "Demo.Derived");
        Field(derived, "Base", "repo").Should().BeSameAs(args["IRepo`1"]);
    }

    [Fact]
    public void NameClashesAcrossAChain_AreSuffixedFromTheMemberName()
    {
        var run = Run("""
            public partial class Root { [Inject] private readonly IA dep; }
            public partial class Mid : Root { [Inject] private readonly IB dep; }
            public partial class Leaf : Mid { [Inject] private readonly IDep dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        var source = run.SourceFor("Inject.g.cs");
        source.Should().Contain("public Mid(global::Demo.IB dep, global::Demo.IA dep2)")
            .And.Contain(": base(dep2)")
            .And.Contain("public Leaf(global::Demo.IDep dep, global::Demo.IB dep2, global::Demo.IA dep3)")
            .And.Contain(": base(dep2, dep3)");

        var (leaf, args) = Construct(run, "Demo.Leaf");
        Field(leaf, "Leaf", "dep").Should().BeSameAs(args["IDep"]);
        Field(leaf, "Mid", "dep").Should().BeSameAs(args["IB"]);
        Field(leaf, "Root", "dep").Should().BeSameAs(args["IA"]);
    }

    #endregion

    #region Base-call order

    [Fact]
    public void TheBaseCall_FollowsEachBaseSignature_ThroughAChain()
    {
        var run = Run("""
            public class ClassC
            {
                public ClassC(IDep1 dep1) { C1 = dep1; }
                public IDep1 C1 { get; }
            }

            public partial class ClassB : ClassC { [Inject] private readonly IDep2 dep2; }
            public partial class ClassA : ClassB { [Inject] private readonly IDep1 dep1; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should()
            .Contain("public ClassB(global::Demo.IDep2 dep2, global::Demo.IDep1 dep1)")
            .And.Contain(": base(dep1)")
            .And.Contain("public ClassA(global::Demo.IDep1 dep1, global::Demo.IDep2 dep2)")
            .And.Contain(": base(dep2, dep1)");

        var (a, args) = Construct(run, "Demo.ClassA");
        Field(a, "ClassA", "dep1").Should().BeSameAs(args["IDep1"]);
        Field(a, "ClassB", "dep2").Should().BeSameAs(args["IDep2"]);
        Property(a, "C1").Should().BeSameAs(args["IDep1"]);
    }

    [Fact]
    public void AGeneratedBase_DeclaringInAnotherOrder_GetsItsArgumentsByType()
    {
        var run = Run("""
            public partial class Base
            {
                [Inject] private readonly IB b;
                [Inject] private readonly IA a;
            }

            public partial class Derived : Base
            {
                [Inject] private readonly IA myA;
                [Inject] private readonly IB myB;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should()
            .Contain("public Base(global::Demo.IB b, global::Demo.IA a)")
            .And.Contain("public Derived(global::Demo.IA myA, global::Demo.IB myB)")
            .And.Contain(": base(myB, myA)");

        var (derived, args) = Construct(run, "Demo.Derived");
        Field(derived, "Base", "a").Should().BeSameAs(args["IA"]);
        Field(derived, "Base", "b").Should().BeSameAs(args["IB"]);
    }

    [Fact]
    public void AHandWrittenBase_DeclaringInAnotherOrder_GetsItsArgumentsByType()
    {
        var run = Run("""
            public class Base
            {
                public Base(IB b, IA a) { B = b; A = a; }
                public IB B { get; }
                public IA A { get; }
            }

            public partial class Derived : Base { [Inject] private readonly IA a; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IA a, global::Demo.IB b)")
            .And.Contain(": base(b, a)");

        var (derived, args) = Construct(run, "Demo.Derived");
        Property(derived, "A").Should().BeSameAs(args["IA"]);
        Property(derived, "B").Should().BeSameAs(args["IB"]);
    }

    [Fact]
    public void ACompiledBase_DeclaringInAnotherOrder_GetsItsArgumentsByType()
    {
        var run = Run("""
            public partial class Derived : F.MetadataBase { [Inject] private readonly F.IMa myA; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain(": base(b, myA)");

        var (derived, args) = Construct(run, "Demo.Derived");
        ((MetadataBase)derived).A.Should().BeSameAs(args["IMa"]);
        ((MetadataBase)derived).B.Should().BeSameAs(args["IMb"]);
    }

    [Fact]
    public void ABaseWithTwoSlotsOfOneType_GetsTheOneParameterTwice()
    {
        var run = Run("""
            public class Base
            {
                public Base(IDep x, IDep y) { X = x; Y = y; }
                public IDep X { get; }
                public IDep Y { get; }
            }

            public partial class Derived : Base { [Inject] private readonly IDep dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IDep dep)")
            .And.Contain(": base(dep, dep)");
    }

    [Fact]
    public void AnExplicitIConfiguration_MergesWithTheBasesAutoInjectedOne()
    {
        var run = Run("""
            public partial class Root { [Inject] private readonly IDep? dep; }
            public partial class Middle : Root { [Config("Key")] private readonly string? key; }
            public partial class Leaf : Middle
            {
                [Inject] private readonly IConfiguration configuration;
                [Inject] private readonly IA a;
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should()
            .Contain("public Middle(global::Microsoft.Extensions.Configuration.IConfiguration __configuration, global::Demo.IDep? dep = default)")
            .And.Contain(": base(dep)")
            .And.Contain("public Leaf(global::Microsoft.Extensions.Configuration.IConfiguration configuration, global::Demo.IA a, global::Demo.IDep? dep = default)")
            .And.Contain(": base(configuration, dep)");
    }

    #endregion

    #region Bases that get no generated constructor

    [Fact]
    public void ANonPartialBaseWithInjectMembers_IsCalledThroughItsRealConstructor()
    {
        var run = Run("""
            public class Base
            {
                [Inject] private readonly IDep dep = null!;
                public Base(IOther other) { }
            }

            public partial class Derived : Base { [Inject] private readonly IA a; }
            """);

        run.Of("INJECT006").Should().NotBeEmpty();
        run.Errors.Where(e => e.Id != "INJECT006").Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IA a, global::Demo.IOther other)")
            .And.Contain(": base(other)");
    }

    [Fact]
    public void APrimaryConstructorBaseWithInjectMembers_IsCalledThroughItsRealConstructor()
    {
        var run = Run("""
            public partial class Base(IOther other)
            {
                [Inject] private readonly IDep dep = null!;
                public IOther Other { get; } = other;
            }

            public partial class Derived : Base { [Inject] private readonly IA a; }
            """);

        run.Of("INJECT007").Should().NotBeEmpty();
        run.Errors.Where(e => e.Id != "INJECT007").Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("public Derived(global::Demo.IA a, global::Demo.IOther other)")
            .And.Contain(": base(other)");
    }

    [Fact]
    public void ACompiledBaseParameterWithADefault_StaysOptional()
    {
        var run = Run("""
            public partial class Derived : F.MetadataBaseWithDefault { [Inject] private readonly IDep dep; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should()
            .Contain("public Derived(global::Demo.IDep dep, global::MintPlayer.SourceGenerators.Tests.Generators.InjectBaseMergeFixtures.IMa a, global::MintPlayer.SourceGenerators.Tests.Generators.InjectBaseMergeFixtures.IMb? b = default)")
            .And.Contain(": base(a, b)");
    }

    #endregion

    [Fact]
    public void ANullableUriConfig_IsBoundAsAUri()
    {
        var run = Run("""
            public partial class Service { [Config("Endpoint")] private readonly System.Uri? endpoint; }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.SourceFor("Inject.g.cs").Should().Contain("new global::System.Uri(")
            .And.NotContain("GetSection(\"Endpoint\")");
    }

    #region Helpers

    /// <summary>
    /// Builds <paramref name="typeName"/> through its generated constructor, with a fresh instance per parameter type.
    /// The instances are keyed by the parameter type's name.
    /// </summary>
    private static (object Instance, Dictionary<string, object> Args) Construct(GeneratorRun run, string typeName)
    {
        var assembly = run.Emit();
        var constructor = assembly.GetGeneratedType(typeName).GetConstructors().Single(c => c.GetParameters().Length > 0);
        var parameters = constructor.GetParameters();
        var args = parameters.ToDictionary(p => p.ParameterType.Name, p => Implementation(assembly, p.ParameterType));
        args.Should().HaveCount(parameters.Length, "every parameter has a distinct type");

        var instance = constructor.Invoke(parameters.Select(p => args[p.ParameterType.Name]).ToArray());
        return (instance, args);
    }

    private static object Implementation(Assembly assembly, Type service)
    {
        var candidates = assembly.GetTypes().Concat(typeof(MetadataBase).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MetadataBase).Namespace));

        var closed = candidates.FirstOrDefault(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && service.IsAssignableFrom(t));
        if (closed is not null) return Activator.CreateInstance(closed)!;

        var open = candidates.First(t => t.IsGenericTypeDefinition && service.IsGenericType
            && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == service.GetGenericTypeDefinition()));
        return Activator.CreateInstance(open.MakeGenericType(service.GetGenericArguments()))!;
    }

    /// <summary>The value of the field <paramref name="field"/> declared on <paramref name="declaringType"/> (arity ignored).</summary>
    private static object? Field(object instance, string declaringType, string field)
    {
        for (var type = instance.GetType(); type is not null; type = type.BaseType)
        {
            if (type.Name.Split('`')[0] == declaringType
                && type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } info)
                return info.GetValue(instance);
        }

        throw new InvalidOperationException($"No field '{field}' on '{declaringType}'.");
    }

    private static object? Property(object instance, string property)
        => instance.GetType().GetProperty(property)!.GetValue(instance);

    #endregion
}
