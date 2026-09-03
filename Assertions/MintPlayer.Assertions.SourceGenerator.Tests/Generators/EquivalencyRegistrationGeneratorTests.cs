namespace MintPlayer.Assertions.SourceGenerator.Tests.Generators;

/// <summary>
/// Generates reflection-free member accessors for the types that <c>BeEquivalentTo</c> compares,
/// so equivalency works under AOT and trimming.
/// </summary>
/// <remarks>
/// It finds its work two ways — from call sites and from the <c>[AssertEquivalency]</c> attribute —
/// and both paths matter: the call-site scan is what makes the library work without any annotation,
/// and the attribute is the escape hatch for types only reached dynamically.
/// </remarks>
public class EquivalencyRegistrationGeneratorTests
{
    private const string Generator = "EquivalencyRegistrationGenerator";

    [Fact]
    public void ItRegistersTheExpectationTypeAtACallSite()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }

            public class Test
            {
                public void Run(Person actual, Person expected)
                    => actual.Should().BeEquivalentTo(expected);
            }
            """);

        run.GeneratedSources.Should().NotBeEmpty();
        run.AllSources.Should().Contain("Person");
        run.AllSources.Should().Contain("Name");
        run.AllSources.Should().Contain("Age");
    }

    [Fact]
    public void ItRegistersATypeMarkedWithTheAttribute()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            [AssertEquivalency]
            public class Order
            {
                public string Reference { get; set; } = "";
            }
            """);

        run.GeneratedSources.Should().NotBeEmpty();
        run.AllSources.Should().Contain("Order");
        run.AllSources.Should().Contain("Reference");
    }

    /// <summary>
    /// Nested types have to be walked transitively, otherwise equivalency falls back to reflection
    /// exactly where AOT cannot follow it.
    /// </summary>
    [Fact]
    public void ItWalksNestedTypes()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public class Address
            {
                public string City { get; set; } = "";
            }

            public class Customer
            {
                public Address Address { get; set; } = new();
            }

            public class Test
            {
                public void Run(Customer actual, Customer expected)
                    => actual.Should().BeEquivalentTo(expected);
            }
            """);

        run.AllSources.Should().Contain("Customer");
        run.AllSources.Should().Contain("Address");
        run.AllSources.Should().Contain("City");
    }

    [Fact]
    public void ItEmitsCompilableCode()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public class Person
            {
                public string Name { get; set; } = "";
            }

            public class Test
            {
                public void Run(Person a, Person b) => a.Should().BeEquivalentTo(b);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
    }

    /// <summary>
    /// A file with no equivalency in it must not drag the whole compilation into the registry —
    /// that would be a compile-time cost on every consumer for nothing.
    /// </summary>
    [Fact]
    public void ItRegistersNothingWhenEquivalencyIsNotUsed()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public class Person
            {
                public string Name { get; set; } = "";
            }

            public class Test
            {
                public void Run(Person a) => a.Name.Should().Be("x");
            }
            """);

        run.AllSources.Should().NotContain("nameof(Demo.Person.Name)");
    }

    /// <summary>
    /// A self-referencing graph must terminate. The scanner tracks visited types for this reason;
    /// without it this fixture hangs rather than fails, which is why it is worth a test of its own.
    /// </summary>
    [Fact]
    public void ItTerminatesOnARecursiveGraph()
    {
        var run = Harness.Instance.RunGenerator(Generator, """
            using MintPlayer.Assertions;

            namespace Demo;

            public class Node
            {
                public string Name { get; set; } = "";
                public Node? Next { get; set; }
            }

            public class Test
            {
                public void Run(Node a, Node b) => a.Should().BeEquivalentTo(b);
            }
            """);

        run.Errors.Should().BeEmpty(run.ErrorText);
        run.AllSources.Should().Contain("Node");
    }
}
