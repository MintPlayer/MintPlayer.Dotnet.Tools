using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Pipeline;

/// <summary>
/// The synthetic B2 compilations. <see cref="S1"/> is exactly the corpus of spike S1 (500 files, the first 50
/// of them <c>[Register]</c> classes with two <c>[Inject]</c> fields), so the 5-generator numbers are directly
/// comparable to the PRD's baseline. <see cref="Full"/> keeps the 500 files and the 50 services, and turns 100
/// of the plain files into 50 <c>[GenerateMapper]</c> pairs and 50 <c>[GenerateEquality]</c> models, so every
/// generator in the repo has inputs.
/// </summary>
public sealed class Corpus
{
    public const int Files = 500;
    public const int Services = 50;
    public const int MapperFiles = 50;
    public const int ModelFiles = 50;

    /// <summary>A plain file; editing its method body is the "unrelated" edit.</summary>
    public const int UnrelatedFile = 250;

    /// <summary>A <c>[Register]</c> file; toggling its lifetime is the "relevant" edit.</summary>
    public const int RelevantFile = 10;

    private Corpus(string name, Func<int, string> file)
    {
        Name = name;
        File = file;
    }

    public string Name { get; }
    private Func<int, string> File { get; }

    public static Corpus S1 { get; } = new("S1", i => i < Services ? ServiceFile(i, "Scoped") : PlainFile(i, 1));

    public static Corpus Full { get; } = new("Full", i =>
        i < Services ? ServiceFile(i, "Scoped")
        : i < Services + MapperFiles ? MapperFile(i)
        : i < Services + MapperFiles + ModelFiles ? ModelFile(i)
        : PlainFile(i, 1));

    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    public CSharpCompilation Create()
    {
        var trees = Enumerable.Range(0, Files)
            .Select(i => CSharpSyntaxTree.ParseText(File(i), ParseOptions, path: $"F{i}.cs"))
            .ToList();
        return CSharpCompilation.Create("Corpus", trees, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>The text of edit number <paramref name="version"/> of <paramref name="file"/>.</summary>
    public static string Edit(int file, int version) => file == RelevantFile
        ? ServiceFile(file, version % 2 == 0 ? "Scoped" : "Singleton")
        : PlainFile(file, version);

    // ---- file templates (the first two are spike S1's, unchanged) ----

    public static string ServiceFile(int i, string lifetime) => $$"""
        using MintPlayer.SourceGenerators.Attributes;
        using Microsoft.Extensions.DependencyInjection;
        namespace Corpus.N{{i}};
        public interface IService{{i}} { string Get(); }
        [Register(typeof(IService{{i}}), ServiceLifetime.{{lifetime}})]
        public partial class Service{{i}} : IService{{i}}
        {
            [Inject] private readonly {{(i == 0 ? "System.IServiceProvider" : $"Corpus.N{i - 1}.IService{i - 1}")}} dep;
            [Inject] private readonly System.IServiceProvider provider;
            public string Get() => "v{{i}}";
        }
        """;

    public static string PlainFile(int i, int body) => $$"""
        using System;
        using System.Collections.Generic;
        namespace Corpus.P{{i}};
        public class Plain{{i}}
        {
            private readonly List<int> _items = new();
            public string Name { get; set; } = "p{{i}}";
            public int Count => _items.Count;
            public void Add(int x) { if (x > 0) _items.Add(x); }
            public int Compute(int a, int b)
            {
                var sum = 0;
                for (var k = a; k < b; k++) sum += k * {{body}};
                return sum;
            }
            public override string ToString() => $"{Name}:{Count}";
        }
        """;

    public static string MapperFile(int i) => $$"""
        using MintPlayer.Mapper.Attributes;
        namespace Corpus.M{{i}};
        public class PersonDto{{i}}
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
        }
        [GenerateMapper(typeof(PersonDto{{i}}))]
        public class Person{{i}}
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
        }
        """;

    public static string ModelFile(int i) => $$"""
        using MintPlayer.ValueComparerGenerator.Attributes;
        using System.Collections.Generic;
        namespace Corpus.V{{i}};
        [GenerateEquality]
        public sealed partial class Model{{i}}
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public IReadOnlyList<string> Tags { get; set; } = new List<string>();
        }
        """;

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return tpa
            .Where(p => Path.GetFileName(p) is var n && (n.StartsWith("System.", StringComparison.Ordinal) || n is "System.dll" or "netstandard.dll" or "mscorlib.dll"))
            // ServiceRegistrationsGenerator produces nothing unless the DI abstractions are referenced.
            .Append(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location)
            .Append(typeof(MintPlayer.SourceGenerators.Attributes.RegisterAttribute).Assembly.Location)
            .Append(typeof(MintPlayer.Mapper.Attributes.GenerateMapperAttribute).Assembly.Location)
            .Append(typeof(MintPlayer.ValueComparerGenerator.Attributes.GenerateEqualityAttribute).Assembly.Location)
            .Append(typeof(MintPlayer.SourceGenerators.Tools.ValueEquality).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    });
}
