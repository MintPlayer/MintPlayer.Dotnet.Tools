using MintPlayer.Verz.Abstractions;
using MintPlayer.Verz.Hosting;
using Xunit;

namespace MintPlayer.Verz.Tests;

public class ProjectGraphTests
{
    private static ProjectNode Node(string id, params string[] deps) => new()
    {
        PackageId = id,
        ProjectDir = $"/repo/{id}",
        ProjectFile = $"/repo/{id}/{id}.csproj",
        OwnerSdkId = "dotnet",
        Dependencies = deps,
    };

    private static ProjectGraph Graph(params ProjectNode[] nodes) =>
        new(nodes.ToDictionary(n => n.PackageId, n => n, StringComparer.Ordinal));

    [Fact]
    public void Topological_order_emits_deepest_dependencies_first()
    {
        // A -> B -> C, plus standalone D
        var graph = Graph(
            Node("A", "B"),
            Node("B", "C"),
            Node("C"),
            Node("D"));

        var order = graph.TopologicalOrder().Select(n => n.PackageId).ToArray();

        Assert.Equal(4, order.Length);
        Assert.True(Array.IndexOf(order, "C") < Array.IndexOf(order, "B"));
        Assert.True(Array.IndexOf(order, "B") < Array.IndexOf(order, "A"));
        Assert.Contains("D", order);
    }

    [Fact]
    public void Cycle_is_detected_and_reported()
    {
        var graph = Graph(
            Node("A", "B"),
            Node("B", "C"),
            Node("C", "A"));

        var ex = Assert.Throws<CycleException>(() => graph.TopologicalOrder());
        Assert.Equal(9, ex.ExitCode);
        // The reported path should round-trip through the cycle.
        Assert.Equal(ex.CyclePath[0], ex.CyclePath[^1]);
    }

    [Fact]
    public void External_dependencies_are_ignored_for_cycle_detection()
    {
        // Only A is in-repo; "External" isn't a node, so the edge dangles.
        var graph = Graph(Node("A", "External"));
        var order = graph.TopologicalOrder();
        Assert.Single(order);
    }

    [Fact]
    public void Affected_closure_includes_changed_node_and_all_consumers()
    {
        // C is a leaf dep; A and B both consume C; D is unrelated.
        var graph = Graph(
            Node("A", "C"),
            Node("B", "C"),
            Node("C"),
            Node("D"));

        var affected = graph.AffectedClosure(new[] { "C" });

        Assert.Equal(new[] { "A", "B", "C" }, affected.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Affected_closure_walks_chains_transitively()
    {
        // App -> Mid -> Core; changing Core affects Mid and App.
        var graph = Graph(
            Node("App", "Mid"),
            Node("Mid", "Core"),
            Node("Core"));

        var affected = graph.AffectedClosure(new[] { "Core" });

        Assert.Equal(new[] { "App", "Core", "Mid" }, affected.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Affected_closure_does_not_include_independent_subgraph()
    {
        var graph = Graph(
            Node("A", "B"),
            Node("B"),
            Node("X", "Y"),
            Node("Y"));

        var affected = graph.AffectedClosure(new[] { "B" });

        Assert.Equal(new[] { "A", "B" }, affected.OrderBy(x => x).ToArray());
        Assert.DoesNotContain("X", affected);
        Assert.DoesNotContain("Y", affected);
    }

    [Fact]
    public void Reverse_adjacency_lists_consumers_per_dep()
    {
        var graph = Graph(
            Node("A", "Lib"),
            Node("B", "Lib"),
            Node("Lib"));

        Assert.True(graph.ReverseAdjacency.TryGetValue("Lib", out var consumers));
        Assert.Equal(new[] { "A", "B" }, consumers!.OrderBy(x => x).ToArray());
    }
}
