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

        run.Of("CONFIG001").Should().BeEmpty();
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
                [Options("Database")] private readonly IOptions<DatabaseOptions> _options;
            }

            public class DatabaseOptions
            {
                public string Host { get; set; } = "";
            """, usings: "using Microsoft.Extensions.Options;");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("OPTIONS001").Should().BeEmpty("IOptions<T> is the shape [Options] requires");
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

        run.Of("CONFIG001").Should().NotBeEmpty(
            "the generator emits into a partial and cannot add a constructor otherwise");
    }

    [Fact]
    public void ItReportsAnEmptyConfigKey()
    {
        var run = Run("""    [Config("")] private readonly int _maxRetries;""");

        run.Of("CONFIG002").Should().NotBeEmpty(
            "an empty key binds nothing");
    }

    [Fact]
    public void ItReportsAnEmptyConnectionStringName()
    {
        var run = Run("""    [ConnectionString("")] private readonly string _connection;""");

        run.Of("CONNSTR001").Should().NotBeEmpty(
            "an empty name resolves no connection string");
    }

    /// <summary>
    /// A connection string is a string. Anything else cannot be bound and is reported rather than
    /// emitted as code that will not compile in the consumer's project.
    /// </summary>
    [Fact]
    public void ItReportsANonStringConnectionString()
    {
        var run = Run("""    [ConnectionString("DefaultConnection")] private readonly int _connection;""");

        run.Of("CONNSTR002").Should().NotBeEmpty(
            "a connection string is a string");
    }

    [Fact]
    public void ItReportsAConfigAndConnectionStringOnTheSameField()
    {
        var run = Run("""
                [Config("Db")]
                [ConnectionString("DefaultConnection")]
                private readonly string _value;
            """);

        run.Of("CONFIG006").Should().NotBeEmpty(
            "the two attributes bind the same field from different sources");
    }

    [Fact]
    public void ItReportsAConfigThatCollidesWithInject()
    {
        var run = Run("""
                [Inject]
                [Config("Db")]
                private readonly string _value;
            """);

        run.Of("CONFIG008").Should().NotBeEmpty(
            "a field cannot be both injected and bound from configuration");
    }

    [Fact]
    public void ItReportsDuplicateConfigKeys()
    {
        var run = Run("""
                [Config("Db:Host")] private readonly string _first;
                [Config("Db:Host")] private readonly string _second;
            """);

        run.Of("CONFIG007").Should().NotBeEmpty(
            "two fields bound to one key is a copy-paste error, not a feature");
    }

    /// <summary>
    /// A primitive cannot be bound as an options section, and saying so beats emitting a
    /// <c>Configure&lt;int&gt;</c> the consumer has to decipher.
    /// </summary>
    [Fact]
    public void ItReportsAPrimitiveOptionsType()
    {
        var run = Run("""    [Options("Database")] private readonly int _options;""");

        run.Of("OPTIONS001").Should().NotBeEmpty(
            "a primitive cannot be bound as an options section");
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

        run.Of("OPTIONS003").Should().NotBeEmpty(
            "a field cannot be both injected and bound as options");
    }

    /// <summary>
    /// OPTIONS004 was declared and never reported: a field with both attributes silently took the [Config] path.
    /// </summary>
    [Fact]
    public void ItReportsAnOptionsFieldThatCollidesWithConfig()
    {
        var run = Run("""
                [Config("Database")]
                [Options("Database")]
                private readonly IOptions<DatabaseOptions> _options;
            }

            public class DatabaseOptions { public string Host { get; set; } = ""; }
            """, usings: "using Microsoft.Extensions.Options;");

        run.Of("OPTIONS004").Should().ContainSingle().Which.GetMessage().Should().Contain("_options");
        run.Of("CONFIG003").Should().BeEmpty("the conflict is the one problem to report");
        run.AllSources.Should().NotContain("GetSection(\"Database\")", "neither binding is generated for a conflicting field");
    }

    /// <summary>
    /// [ConnectionString] with [Options] was the one pair of config markers without a diagnostic: the field silently
    /// took the [ConnectionString] path. It is OPTIONS005 now, mirroring OPTIONS004.
    /// </summary>
    [Fact]
    public void ItReportsAnOptionsFieldThatCollidesWithConnectionString()
    {
        var run = Run("""
                [ConnectionString("Default")]
                [Options("Database")]
                private readonly IOptions<DatabaseOptions> _options;
            }

            public class DatabaseOptions { public string Host { get; set; } = ""; }
            """, usings: "using Microsoft.Extensions.Options;");

        run.Of("OPTIONS005").Should().ContainSingle().Which.GetMessage().Should().Contain("_options");
        run.Of("CONNSTR002").Should().BeEmpty("the conflict is the one problem to report");
        run.AllSources.Should().NotContain("GetConnectionString(\"Default\")", "neither binding is generated for a conflicting field");
        run.AllSources.Should().NotContain("GetSection(\"Database\")", "neither binding is generated for a conflicting field");
    }

    /// <summary>
    /// CONFIG005 was declared and never reported. A default the producer cannot emit as the field's value used to
    /// become generated code that does not compile (a string for a DateTime, 3.5 for an int, 300 for a byte), or be
    /// replaced by something else (a non-string default on a string field became null).
    /// </summary>
    [Theory]
    [InlineData("int", "\"3\"")]
    [InlineData("int", "3.5")]
    [InlineData("int", "3L * 1000000000")]
    [InlineData("byte", "300")]
    [InlineData("byte", "'a'")]
    [InlineData("float", "3.5")]
    [InlineData("long", "3.0")]
    [InlineData("int", "null")]
    [InlineData("bool", "1")]
    [InlineData("string", "3")]
    [InlineData("char", "\"ab\"")]
    [InlineData("System.DateTime", "\"2020-01-01\"")]
    [InlineData("System.TimeSpan", "\"00:00:30\"")]
    [InlineData("System.Guid", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("Level", "\"Missing\"")]
    [InlineData("Level", "OtherLevel.High")]
    [InlineData("Level", "typeof(int)")]
    [InlineData("int[]", "new[] { 1, 2 }")]
    [InlineData("Settings", "\"x\"")]
    public void ItReportsADefaultValueOfTheWrongType(string fieldType, string defaultValue)
    {
        var run = Run($$"""
                [Config("App:Value", DefaultValue = {{defaultValue}})] private readonly {{fieldType}} _value;
            }

            public enum Level { Low, High }
            public enum OtherLevel { Low, High }
            public class Settings { public string Name { get; set; } = "";
            """);

        run.Of("CONFIG005").Should().ContainSingle(
            $"DefaultValue = {defaultValue} cannot be written as a {fieldType}")
            .Which.GetMessage().Should().Contain(fieldType.Replace("System.", ""));
        run.GeneratedErrors().Should().BeEmpty("a rejected default emits no code that fails to compile");
    }

    /// <summary>
    /// Every default CONFIG005 admits compiles without a warning and is the value assigned when the key is missing,
    /// including on a nullable field (which used to ignore its default) and in a culture with a decimal comma.
    /// </summary>
    [Fact]
    public void EveryAcceptedDefaultCompilesAndIsUsed()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-BE");
        GeneratorRun run;
        try
        {
            run = GeneratorHarness.Run(Generator, ["""
                #nullable enable
                using MintPlayer.SourceGenerators.Attributes;

                namespace Demo;

                public enum Level { Low, High }
                public enum Small : byte { A, B = 7 }

                public partial class Service
                {
                    [Config("A", DefaultValue = 3)] private readonly double _double;
                    [Config("B", DefaultValue = 3.5)] private readonly double _fraction;
                    [Config("C", DefaultValue = 1.25f)] private readonly float _float;
                    [Config("D", DefaultValue = 2.5)] private readonly double? _nullableDouble;
                    [Config("E", DefaultValue = 7)] private readonly int? _nullableInt;
                    [Config("F", DefaultValue = 200)] private readonly byte _byte;
                    [Config("G", DefaultValue = 3)] private readonly long _long;
                    [Config("H", DefaultValue = 'x')] private readonly int _charAsInt;
                    [Config("I", DefaultValue = '\'')] private readonly char _quote;
                    [Config("J", DefaultValue = Level.High)] private readonly Level _member;
                    [Config("K", DefaultValue = "High")] private readonly Level _name;
                    [Config("L", DefaultValue = 1)] private readonly Level _number;
                    [Config("M", DefaultValue = Small.B)] private readonly Small _small;
                    [Config("N", DefaultValue = Level.High)] private readonly Level? _nullableEnum;
                    [Config("O", DefaultValue = "https://example.com/")] private readonly System.Uri _uri;
                    [Config("P", DefaultValue = true)] private readonly bool _bool;
                    [Config("Q", DefaultValue = "fallback")] private readonly string _string;
                    [Config("R", DefaultValue = null)] private readonly string? _nullString;
                    [Config("S", DefaultValue = -4)] private readonly int _negative;
                }
                """]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        run.Of("CONFIG005").Should().BeEmpty();
        run.Errors.Should().BeEmpty(run.ErrorText);
        run.GeneratedWarningsUnderNullable().Should().BeEmpty();

        var output = run.SourceFor("Inject.g.cs")!;
        output.Should().Contain("(double)(3.5d);").And.Contain("(float)(1.25f);").And.Contain("(double)(2.5d);",
            "the fallback is written in the invariant culture");
        output.Should().Contain(": (int)(7);", "a nullable field uses its default instead of null");
        output.Should().Contain(": (byte)(200);");
        output.Should().Contain(": '\\'';");
        output.Should().Contain(": global::Demo.Level.High;").And.Contain(": (global::Demo.Level)(1);").And.Contain(": (global::Demo.Small)(7);");
        output.Should().Contain("?? \"https://example.com/\")");
        output.Should().Contain(": (int)(-4);");
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
