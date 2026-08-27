using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Snapshots;

/// <summary>
/// Layer 4, over the four large producers (Mapper ~25KB, Inject ~20KB, Registrations ~14KB,
/// Cli ~17KB) where hand-written assertions over the whole output are unmaintainable.
///
/// Every test here also asserts the generated code compiles. A snapshot only proves the output
/// did not change; without the compile check an accepted-but-wrong snapshot locks the bug in.
/// </summary>
public class ProducerSnapshotTests
{
    private static void Verify(GeneratorRun run, string? snapshotOverride = null,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedSources.Should().NotBeEmpty();

        var text = string.Join(
            Environment.NewLine,
            run.GeneratedSources.Select(s =>
                $"//---- {s.HintName} ----{Environment.NewLine}{s.Source}"));

        Snapshot.Match(text, caller: snapshotOverride ?? caller);
    }

    [Fact]
    public void ServiceRegistrations()
        => Verify(GeneratorHarness.Run("ServiceRegistrationsGenerator", ["""
            using Microsoft.Extensions.DependencyInjection;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IGreeter { }
            public interface IClock { }

            [Register(typeof(IGreeter), ServiceLifetime.Scoped)]
            public class Greeter : IGreeter { }

            [Register(typeof(IClock), ServiceLifetime.Singleton)]
            public class Clock : IClock { }
            """], "Demo"));

    [Fact]
    public void InjectConstructors()
        => Verify(GeneratorHarness.Run("InjectSourceGenerator", ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IA { }
            public interface IB { }

            public partial class Consumer
            {
                [Inject] private readonly IA a;
                [Inject] private readonly IB b;
            }
            """], "Demo"));

    [Fact]
    public void Mapper()
        => Verify(GeneratorHarness.Run("MapperGenerator", ["""
            using MintPlayer.Mapper.Attributes;

            namespace Demo;

            public class PersonDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            [GenerateMapper(typeof(PersonDto))]
            public class Person
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """], "Demo", generatorAssemblyName: "MintPlayer.Mapper"));

    [Fact]
    public void CliCommandTree()
        => Verify(GeneratorHarness.Run("CliCommandSourceGenerator", ["""
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliRootCommand("Demo tool")]
            public partial class RootCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }

            [CliCommand("build")]
            public partial class BuildCommand : ICliCommand
            {
                [CliOption("--verbose")] public bool Verbose { get; set; }

                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """], "Demo", generatorAssemblyName: "MintPlayer.CliGenerator"));

    [Fact]
    public void ValueComparers()
        => Verify(GeneratorHarness.Run("ValueComparerGenerator", ["""
            using MintPlayer.ValueComparerGenerator.Attributes;

            namespace Demo;

            [AutoValueComparer]
            public abstract partial class Shape
            {
                public string Name { get; set; } = "";
            }

            public partial class Circle : Shape
            {
                public double Radius { get; set; }
            }
            """], "Demo", generatorAssemblyName: "MintPlayer.ValueComparerGenerator"));
}
