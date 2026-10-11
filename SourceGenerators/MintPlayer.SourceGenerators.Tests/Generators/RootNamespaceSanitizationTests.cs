using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// The SDK defaults RootNamespace to the project name with only spaces replaced, so a consumer project named
/// <c>My-App</c> reaches every generator as <c>build_property.rootnamespace = My-App</c>. Each generator that writes
/// <c>namespace {RootNamespace}</c> (or <c>global::{RootNamespace}.…</c>) then emitted CS0246, CS0102 and CS0548 into
/// the consumer's build. Tools now sanitizes the value once (<c>StringExtensions.ToSafeNamespace</c>, applied in
/// <c>Settings</c> and the <c>Producer</c> base); one test per generator that emits it.
/// </summary>
public class RootNamespaceSanitizationTests
{
    private const string HostileRoot = "My-App.1st.class";
    private const string SafeRoot = "My_App._1st.@class";

    [Fact]
    public void ServiceRegistrations_CompilesWithAHyphenatedRootNamespace()
    {
        var run = GeneratorHarness.Run("ServiceRegistrationsGenerator", ["""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter { }
            """], rootNamespace: HostileRoot);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain($"namespace {SafeRoot}");
    }

    [Fact]
    public void ClassNames_CompilesWithAHyphenatedRootNamespace()
    {
        var run = GeneratorHarness.Run("ClassNamesSourceGenerator", ["""
            namespace Demo;

            public class Alpha { }
            public class Beta { }
            """], rootNamespace: HostileRoot);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain($"namespace {SafeRoot}");
    }

    [Fact]
    public void Mapper_CompilesWithAHyphenatedRootNamespace()
    {
        var run = GeneratorHarness.Run("MapperGenerator", ["""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class AddressDto { public string Street { get; set; } = ""; }

            [GenerateMapper(typeof(AddressDto))]
            public class Address { public string Street { get; set; } = ""; }

            public class PersonDto { public int Id { get; set; } public AddressDto Home { get; set; } = new(); }

            [GenerateMapper(typeof(PersonDto))]
            public class Person { public int Id { get; set; } public Address Home { get; set; } = new(); }
            """], rootNamespace: HostileRoot, generatorAssemblyName: "MintPlayer.Mapper");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain($"namespace {SafeRoot}");
        run.AllSources.Should().Contain($"global::{SafeRoot}.MapperExtensions.");
    }

    [Fact]
    public void CliCommand_CompilesWithAHyphenatedRootNamespace()
    {
        var run = GeneratorHarness.Run("CliCommandSourceGenerator", ["""
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliRootCommand("Demo tool")]
            public partial class RootCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """], rootNamespace: HostileRoot, generatorAssemblyName: "MintPlayer.CliGenerator");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain($"namespace {SafeRoot}");
    }
}
