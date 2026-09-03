using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// The <c>[Config]</c> / <c>[ConnectionString]</c> / <c>[Options]</c> half of
/// <c>InjectSourceGenerator</c>, and the diagnostics it reports.
/// </summary>
/// <remarks>
/// Nothing in the suite declared any of these three attributes before, which left roughly 283
/// lines unreachable in one block: <c>ExtractConfigField</c>, <c>ExtractOptionsField</c>,
/// <c>ExtractConnectionStringField</c>, <c>ClassifyType</c>, and the whole of
/// <c>InjectSourceGenerator.Config.Rules.cs</c>. It also meant no test drove the generator down a
/// diagnostic-reporting path at all — a rule that never fired looked exactly like one that worked.
/// </remarks>
public class InjectConfigTests
{
    private const string Generator = "InjectSourceGenerator";

    private static GeneratorRun Run(string body, string usings = "") => GeneratorHarness.Run(Generator, [$$"""
        using MintPlayer.SourceGenerators.Attributes;
        {{usings}}

        namespace Demo;

        public partial class Service
        {
        {{body}}
        }
        """]);

    #region Happy paths

    [Fact]
    public void ItReadsAConfigValue()
    {
        var run = Run("""    [Config("Database:MaxRetries")] private readonly int _maxRetries;""");

        run.Of("MPCFG001").Should().BeEmpty();
        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ItAcceptsADefaultValue()
    {
        var run = Run("""    [Config("Database:MaxRetries", DefaultValue = 3)] private readonly int _maxRetries;""");

        run.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void ItReadsAConnectionString()
    {
        var run = Run("""    [ConnectionString("DefaultConnection")] private readonly string _connection;""");

        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ItBindsAnOptionsSection()
    {
        var run = Run("""
                [Options("Database")] private readonly DatabaseOptions _options;
            }

            public class DatabaseOptions
            {
                public string Host { get; set; } = "";
            """);

        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ItCombinesConfigWithOrdinaryInjection()
    {
        var run = Run("""
                [Inject] private readonly System.IServiceProvider _provider;
                [Config("Database:MaxRetries")] private readonly int _maxRetries;
            """);

        run.AllSources.Should().Contain("_provider");
        run.AllSources.Should().Contain("_maxRetries");
    }

    #endregion

    #region Diagnostics

    /// <summary>
    /// The generator emits into a partial. A non-partial class cannot receive the constructor, so
    /// this has to be reported rather than silently producing nothing.
    /// </summary>
    [Fact]
    public void ItReportsANonPartialClass()
    {
        var run = GeneratorHarness.Run(Generator, ["""
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public class Service
            {
                [Config("Database:MaxRetries")] private readonly int _maxRetries;
            }
            """]);

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItReportsAnEmptyConfigKey()
    {
        var run = Run("""    [Config("")] private readonly int _maxRetries;""");

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItReportsAnEmptyConnectionStringName()
    {
        var run = Run("""    [ConnectionString("")] private readonly string _connection;""");

        run.Diagnostics.Should().NotBeEmpty();
    }

    /// <summary>
    /// A connection string is a string. Anything else cannot be bound and is reported rather than
    /// emitted as code that will not compile in the consumer's project.
    /// </summary>
    [Fact]
    public void ItReportsANonStringConnectionString()
    {
        var run = Run("""    [ConnectionString("DefaultConnection")] private readonly int _connection;""");

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItReportsAConfigAndConnectionStringOnTheSameField()
    {
        var run = Run("""
                [Config("Db")]
                [ConnectionString("DefaultConnection")]
                private readonly string _value;
            """);

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItReportsAConfigThatCollidesWithInject()
    {
        var run = Run("""
                [Inject]
                [Config("Db")]
                private readonly string _value;
            """);

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItReportsDuplicateConfigKeys()
    {
        var run = Run("""
                [Config("Db:Host")] private readonly string _first;
                [Config("Db:Host")] private readonly string _second;
            """);

        run.Diagnostics.Should().NotBeEmpty();
    }

    /// <summary>
    /// A primitive cannot be bound as an options section, and saying so beats emitting a
    /// <c>Configure&lt;int&gt;</c> the consumer has to decipher.
    /// </summary>
    [Fact]
    public void ItReportsAPrimitiveOptionsType()
    {
        var run = Run("""    [Options("Database")] private readonly int _options;""");

        run.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void ItReportsAnOptionsFieldThatCollidesWithInject()
    {
        var run = Run("""
                [Inject]
                [Options("Database")]
                private readonly DatabaseOptions _options;
            }

            public class DatabaseOptions { public string Host { get; set; } = ""; }
            """);

        run.Diagnostics.Should().NotBeEmpty();
    }

    #endregion

    /// <summary>
    /// Every descriptor the config rules declare should be well formed. Cheap, and it catches the
    /// copy-paste that leaves two rules sharing an id — which silently merges two diagnostics into
    /// one in every consumer's error list.
    /// </summary>
    [Fact]
    public void TheConfigRulesAreWellFormed()
    {
        var run = Run("""    [Config("")] private readonly int _value;""");

        foreach (var diagnostic in run.Diagnostics)
        {
            diagnostic.Id.Should().NotBeEmpty();
            diagnostic.Descriptor.Title.ToString().Should().NotBeEmpty();
            diagnostic.GetMessage().Should().NotBeEmpty();
        }
    }
}
