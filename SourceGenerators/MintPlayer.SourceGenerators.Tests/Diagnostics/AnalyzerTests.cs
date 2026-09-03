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
