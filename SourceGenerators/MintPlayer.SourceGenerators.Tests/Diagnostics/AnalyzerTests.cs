using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Diagnostics;

public class UnusedUsingsAnalyzerTests
{
    private const string Id = "MP001";

    [Fact]
    public async Task ItReportsAnUnusedUsing()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", ["""
            using System;
            using System.Text;

            namespace Demo;

            public class Thing
            {
                public string Describe() => string.Empty;
            }
            """]);

        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().OnlyContain(d => d.Id == Id);
    }

    [Fact]
    public async Task ItReportsEveryUnusedUsing()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", ["""
            using System;
            using System.Text;
            using System.Collections.Generic;

            namespace Demo;

            public class Thing { }
            """]);

        diagnostics.Should().HaveCount(3);
    }

    [Fact]
    public async Task ItStaysQuietWhenEveryUsingIsUsed()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", ["""
            using System.Text;

            namespace Demo;

            public class Thing
            {
                public string Describe() => new StringBuilder().Append("x").ToString();
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItStaysQuietOnAFileWithNoUsings()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", ["""
            namespace Demo;

            public class Thing { }
            """]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItReportsAtTheLocationOfTheUsing()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("UnusedUsingsAnalyzer", ["""
            using System.Text;

            namespace Demo;

            public class Thing { }
            """]);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(0);
    }
}

public class InterfaceImplementationAnalyzerTests
{
    private const string Id = "INTF001";

    [Fact]
    public async Task ItReportsAMissingInterfaceMember()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            namespace Demo;

            public interface IThing
            {
                void DoIt();
                string Name { get; }
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public string Name => "x";
                public void Extra() { }
            }
            """]);

        // Extra() is a public member with no interface counterpart.
        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().OnlyContain(d => d.Id == Id);
    }

    [Fact]
    public async Task ItStaysQuietWhenTheClassMatchesTheInterfaceExactly()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresAClassWithNoInterfaces()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            namespace Demo;

            public class Thing
            {
                public void Anything() { }
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresInterfacesThatOnlyExistInMetadata()
    {
        // IDisposable is not declared in source, so the analyzer skips it — otherwise every
        // IDisposable implementation with any extra public member would light up.
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            using System;

            namespace Demo;

            public class Thing : IDisposable
            {
                public void Dispose() { }
                public void Extra() { }
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItHonoursNoInterfaceMember()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }

                [NoInterfaceMember]
                public void Extra() { }
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresStaticAndNonPublicMembers()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing
            {
                public void DoIt() { }
                public static void Helper() { }
                private void Hidden() { }
                internal void AlsoHidden() { }
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// A member carried by one implemented interface is not missing merely because another
    /// implemented interface lacks it.
    /// </summary>
    /// <remarks>
    /// The analyzer used to evaluate membership once per interface, against that interface alone.
    /// Every public member therefore had to appear on EVERY implemented interface or be reported,
    /// so this fixture — where each member sits on exactly the interface that declares it, and
    /// nothing is missing at all — produced two diagnostics, both false. Neither was suppressible
    /// without suppressing the rule, which made INTF001 unusable on any class implementing more
    /// than one interface.
    /// </remarks>
    [Fact]
    public async Task ItDoesNotReportAMemberCarriedByAnotherImplementedInterface()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            namespace Demo;

            public interface IPerson
            {
                string Name { get; }
            }

            public interface IAuditable
            {
                int Version { get; }
            }

            public class Person : IPerson, IAuditable
            {
                public string Name => "x";
                public int Version => 1;
            }
            """]);

        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// A member on none of the implemented interfaces is reported once, not once per interface.
    /// </summary>
    /// <remarks>
    /// Cardinality is the point. Per-interface reporting would raise two diagnostics for the one
    /// member, and "fix all occurrences" would then add it to BOTH interfaces — the same
    /// wrong-target damage the fix's own defect caused, arrived at from the other direction. One
    /// diagnostic keeps the ambiguity where it belongs: a choice between code actions.
    /// </remarks>
    [Fact]
    public async Task ItReportsAMemberMissingFromEveryInterfaceExactlyOnce()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            namespace Demo;

            public interface IPerson
            {
                string Name { get; }
            }

            public interface IAuditable
            {
                int Version { get; }
            }

            public class Person : IPerson, IAuditable
            {
                public string Name => "x";
                public int Version => 1;
                public string LastName => "y";
            }
            """]);

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be(Id);
        diagnostic.GetMessage().Should().Contain("LastName");
    }

    /// <summary>
    /// A metadata-only interface still satisfies a member, even though it can never be a fix target.
    /// </summary>
    /// <remarks>
    /// The two roles are separate and conflating them reintroduces false positives: interfaces the
    /// fix cannot edit are excluded from the candidate list, but excluding them from the membership
    /// test too would report every member that IDisposable happens to carry.
    /// </remarks>
    [Fact]
    public async Task ItTreatsAMetadataInterfaceAsSatisfyingAMember()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("InterfaceImplementationAnalyzer", ["""
            using System;

            namespace Demo;

            public interface IThing
            {
                void DoIt();
            }

            public class Thing : IThing, IDisposable
            {
                public void DoIt() { }
                public void Dispose() { }
            }
            """]);

        diagnostics.Should().BeEmpty();
    }
}

public class CliCommandInterfaceAnalyzerTests
{
    private const string Id = "MINTCLI001";

    [Fact]
    public async Task ItReportsACommandThatDoesNotImplementICliCommand()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("CliCommandInterfaceAnalyzer", ["""
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliCommand("build")]
            public class BuildCommand { }
            """], analyzerAssemblyName: "MintPlayer.CliGenerator");

        diagnostics.Should().ContainSingle().Which.Id.Should().Be(Id);
    }

    [Fact]
    public async Task ItReportsARootCommandThatDoesNotImplementICliCommand()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("CliCommandInterfaceAnalyzer", ["""
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliRootCommand("tool")]
            public class RootCommand { }
            """], analyzerAssemblyName: "MintPlayer.CliGenerator");

        diagnostics.Should().ContainSingle().Which.Id.Should().Be(Id);
    }

    [Fact]
    public async Task ItStaysQuietWhenTheCommandImplementsICliCommand()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("CliCommandInterfaceAnalyzer", ["""
            using System.Threading;
            using System.Threading.Tasks;
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliCommand("build")]
            public class BuildCommand : ICliCommand
            {
                public Task<int> Execute(CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """], analyzerAssemblyName: "MintPlayer.CliGenerator");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task ItIgnoresAClassWithNoCommandAttribute()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("CliCommandInterfaceAnalyzer", ["""
            namespace Demo;

            public class NotACommand { }
            """], analyzerAssemblyName: "MintPlayer.CliGenerator");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task TheDiagnosticIsAnErrorAndNamesTheClass()
    {
        var diagnostics = await GeneratorHarness.RunAnalyzerAsync("CliCommandInterfaceAnalyzer", ["""
            using MintPlayer.CliGenerator.Attributes;

            namespace Demo;

            [CliCommand("build")]
            public class BuildCommand { }
            """], analyzerAssemblyName: "MintPlayer.CliGenerator");

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("BuildCommand");
    }
}
