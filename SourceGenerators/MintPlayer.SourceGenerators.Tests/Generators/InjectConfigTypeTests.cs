using MintPlayer.SourceGenerators.Tests._Infrastructure;

namespace MintPlayer.SourceGenerators.Tests.Generators;

/// <summary>
/// The two parts of <c>InjectSourceGenerator</c>'s config half that <see cref="InjectConfigTests"/>
/// never reached: the property-type classification table, and everything declared on a
/// <em>property</em> rather than a field.
/// </summary>
/// <remarks>
/// <para>
/// The extractor is a 2x3 matrix — fields and properties, each handling <c>[Config]</c>,
/// <c>[ConnectionString]</c> and <c>[Options]</c> — and before this file only the field/<c>[Config]</c>
/// cell was exercised. No fixture in the suite decorated a property at all, leaving the whole
/// properties branch (roughly 110 lines) unreachable, along with five of the six sites that report
/// CONFIG001.
/// </para>
/// <para>
/// The type table is the other half. <c>InjectConfigTests</c> only ever declares <c>int</c> and
/// <c>string</c> members, so every other category — Boolean, Char, the eleven numerics, Enum,
/// DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly, Guid, Uri, collections, complex types —
/// was classified by code no test had run. A Theory is the right shape here precisely because these
/// are cases in one table rather than separate features.
/// </para>
/// </remarks>
public class InjectConfigTypeTests
{
    private const string Generator = "InjectSourceGenerator";

    private static GeneratorRun Run(string body, string extra = "") => GeneratorHarness.Run(Generator, [$$"""
        using System;
        using System.Collections.Generic;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public partial class Service
        {
        {{body}}
        }

        {{extra}}
        """]);

    /// <summary>
    /// Non-partial variant: the generator cannot emit a constructor into it, so every one of the
    /// six declaration shapes must report CONFIG001 rather than quietly producing nothing.
    /// </summary>
    private static GeneratorRun RunNonPartial(string body, string extra = "") => GeneratorHarness.Run(Generator, [$$"""
        using System;
        using Microsoft.Extensions.Options;
        using MintPlayer.SourceGenerators.Attributes;

        namespace Demo;

        public class Service
        {
        {{body}}
        }

        {{extra}}
        """]);

    #region The type-classification table

    /// <summary>
    /// Every category the classifier recognises, bound from configuration. The assertion that
    /// carries the weight is <c>Errors</c> being empty: each category emits a different conversion
    /// expression, and a category that produced uncompilable code would surface here rather than
    /// in a consumer's build.
    /// </summary>
    [Theory]
    // Boolean and Char have their own categories.
    [InlineData("bool")]
    [InlineData("char")]
    // The eleven numerics all fold into one category; a representative spread is enough to prove
    // the branch, and the full set would only re-run the same line.
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("int")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    // Each of these is a distinct category with its own parse call.
    [InlineData("string")]
    [InlineData("DateTime")]
    [InlineData("DateTimeOffset")]
    [InlineData("TimeSpan")]
    [InlineData("DateOnly")]
    [InlineData("TimeOnly")]
    [InlineData("Guid")]
    [InlineData("Uri")]
    public void EveryScalarCategory_BindsAndCompiles(string type)
    {
        var run = Run($$"""    [Config("Section:Value")] private readonly {{type}} _value;""");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG003").Should().BeEmpty($"{type} is a supported configuration type");
        run.GeneratedSources.Should().NotBeEmpty();
    }

    /// <summary>
    /// The nullable path is a separate flag through the whole classifier and changes the emitted
    /// conversion, so it needs its own cases rather than being assumed from the non-nullable ones.
    /// </summary>
    [Theory]
    [InlineData("bool?")]
    [InlineData("int?")]
    [InlineData("double?")]
    [InlineData("DateTime?")]
    [InlineData("TimeSpan?")]
    [InlineData("Guid?")]
    public void NullableScalars_BindAndCompile(string type)
    {
        var run = Run($$"""    [Config("Section:Value")] private readonly {{type}} _value;""");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG003").Should().BeEmpty($"{type} is a supported configuration type");
    }

    [Fact]
    public void AnEnum_IsClassifiedAsAnEnumAndParsedByName()
    {
        var run = Run(
            """    [Config("Logging:Level")] private readonly LogKind _level;""",
            "public enum LogKind { Trace, Debug, Warning }");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG003").Should().BeEmpty("an enum is bindable by member name");
        run.AllSources.Should().Contain("LogKind");
    }

    /// <summary>
    /// The recognised collection shapes: arrays, and the four generic interfaces/classes the
    /// classifier names explicitly.
    /// </summary>
    [Theory]
    [InlineData("string[]")]
    [InlineData("int[]")]
    [InlineData("List<string>")]
    [InlineData("IList<string>")]
    [InlineData("IEnumerable<int>")]
    [InlineData("ICollection<int>")]
    public void Collections_AreClassifiedAsCollections(string type)
    {
        var run = Run($$"""    [Config("Section:Items")] private readonly {{type}} _items;""");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG003").Should().BeEmpty($"{type} binds as a configuration collection");
    }

    /// <summary>
    /// Pins the boundary of the collection support, which is a closed list rather than "anything
    /// enumerable": <c>List</c>, <c>IList</c>, <c>IEnumerable</c>, <c>ICollection</c> and arrays.
    /// <c>IReadOnlyList&lt;T&gt;</c> and <c>IReadOnlyCollection&lt;T&gt;</c> are not on it and are
    /// reported as unsupported.
    /// </summary>
    /// <remarks>
    /// Documents current behaviour rather than endorsing it — a read-only interface is a natural
    /// thing to declare and arguably belongs on the list. Note also that CONFIG003's message
    /// enumerates "primitives, enums, DateTime, TimeSpan, Guid, Uri, and POCO classes" and never
    /// mentions collections at all, so a consumer hitting this gets no hint that some collection
    /// types would have worked.
    /// </remarks>
    [Theory]
    [InlineData("IReadOnlyList<int>")]
    [InlineData("IReadOnlyCollection<int>")]
    public void ReadOnlyCollectionInterfaces_AreNotSupported(string type)
    {
        var run = Run($$"""    [Config("Section:Items")] private readonly {{type}} _items;""");

        run.Of("CONFIG003").Should().NotBeEmpty(
            $"{type} is outside the closed list of supported collection shapes");
    }

    [Fact]
    public void AComplexType_IsBoundAsASection()
    {
        var run = Run(
            """    [Config("Database")] private readonly Endpoint _endpoint;""",
            "public class Endpoint { public string Host { get; set; } = \"\"; }");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG003").Should().BeEmpty("a POCO binds as a section");
    }

    /// <summary>
    /// The other end of the table. An interface is neither scalar, collection, class nor struct, so
    /// it falls through to Unsupported — the only path that reports CONFIG003, which no test
    /// reached before.
    /// </summary>
    [Fact]
    public void AnUnsupportedType_ReportsConfig003()
    {
        var run = Run("""    [Config("Section:Value")] private readonly IDisposable _value;""");

        run.Of("CONFIG003").Should().NotBeEmpty(
            "an interface cannot be bound from a configuration value");
    }

    #endregion

    #region Declared on a property rather than a field

    [Fact]
    public void ConfigOnAProperty_IsBound()
    {
        var run = Run("""    [Config("Database:MaxRetries")] public int MaxRetries { get; set; }""");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONFIG001").Should().BeEmpty();
        run.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void ConnectionStringOnAProperty_IsBound()
    {
        var run = Run("""    [ConnectionString("DefaultConnection")] public string Connection { get; set; } = "";""");

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("CONNSTR001").Should().BeEmpty();
        run.Of("CONNSTR002").Should().BeEmpty();
    }

    /// <summary>
    /// <c>[Options]</c> requires an <c>IOptions&lt;T&gt;</c> family wrapper, not the POCO itself —
    /// a bare <c>DatabaseOptions</c> is rejected with OPTIONS001.
    /// </summary>
    [Fact]
    public void OptionsOnAProperty_IsBound()
    {
        var run = GeneratorHarness.Run(Generator, ["""
            using Microsoft.Extensions.Options;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public partial class Service
            {
                [Options("Database")] public IOptions<DatabaseOptions> Options { get; set; } = null!;
            }

            public class DatabaseOptions { public string Host { get; set; } = ""; }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("OPTIONS001").Should().BeEmpty();
    }

    /// <summary>
    /// The property branch has its own copies of the conflict checks, so they need their own
    /// fixtures — the field-based ones in <see cref="InjectConfigTests"/> do not reach them.
    /// </summary>
    [Fact]
    public void ConfigAndInjectOnTheSameProperty_ReportsConfig008()
    {
        var run = Run("""
                [Inject]
                [Config("Db")]
                public string Value { get; set; } = "";
            """);

        run.Of("CONFIG008").Should().NotBeEmpty(
            "a property cannot be both injected and bound from configuration");
    }

    [Fact]
    public void ConnectionStringAndInjectOnTheSameProperty_ReportsConnstr003()
    {
        var run = Run("""
                [Inject]
                [ConnectionString("DefaultConnection")]
                public string Value { get; set; } = "";
            """);

        run.Of("CONNSTR003").Should().NotBeEmpty(
            "a property cannot be both injected and bound to a connection string");
    }

    /// <summary>
    /// CONNSTR003 on a field. Declared at two sites, one per member kind, and neither had a test —
    /// <see cref="InjectConfigTests"/> covers the Config and Options collisions but not this one.
    /// </summary>
    [Fact]
    public void ConnectionStringAndInjectOnTheSameField_ReportsConnstr003()
    {
        var run = Run("""
                [Inject]
                [ConnectionString("DefaultConnection")]
                private readonly string _value;
            """);

        run.Of("CONNSTR003").Should().NotBeEmpty();
    }

    #endregion

    #region CONFIG001 from all six declaration shapes

    [Theory]
    [InlineData("""    [Config("Db:Host")] private readonly string _value;""")]
    [InlineData("""    [ConnectionString("DefaultConnection")] private readonly string _value;""")]
    [InlineData("""    [Options("Database")] private readonly DatabaseOptions _value;""")]
    [InlineData("""    [Config("Db:Host")] public string Value { get; set; } = "";""")]
    [InlineData("""    [ConnectionString("DefaultConnection")] public string Value { get; set; } = "";""")]
    [InlineData("""    [Options("Database")] public DatabaseOptions Value { get; set; } = new();""")]
    public void AnyDeclarationInANonPartialClass_ReportsConfig001(string member)
    {
        var run = RunNonPartial(member, "public class DatabaseOptions { public string Host { get; set; } = \"\"; }");

        run.Of("CONFIG001").Should().NotBeEmpty(
            "the generator emits into a partial; every declaration shape must say so");
    }

    #endregion

    #region IOptions wrapper kinds

    /// <summary>
    /// <c>IOptions</c>, <c>IOptionsSnapshot</c> and <c>IOptionsMonitor</c> are detected separately
    /// and produce different registrations. Only the plain <c>[Options]</c> POCO form was covered.
    /// </summary>
    [Theory]
    [InlineData("IOptions")]
    [InlineData("IOptionsSnapshot")]
    [InlineData("IOptionsMonitor")]
    public void EachOptionsWrapperKind_IsRecognised(string wrapper)
    {
        var run = GeneratorHarness.Run(Generator, [$$"""
            using Microsoft.Extensions.Options;
            using MintPlayer.SourceGenerators.Attributes;

            namespace Demo;

            public partial class Service
            {
                [Options("Database")] private readonly {{wrapper}}<DatabaseOptions> _options;
            }

            public class DatabaseOptions { public string Host { get; set; } = ""; }
            """]);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.Of("OPTIONS001").Should().BeEmpty($"{wrapper}<T> is a valid options wrapper");
    }

    #endregion
}
