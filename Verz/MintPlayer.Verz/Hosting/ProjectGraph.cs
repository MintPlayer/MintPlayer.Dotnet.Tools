using MintPlayer.Verz.Abstractions;

namespace MintPlayer.Verz.Hosting;

public sealed class ProjectNode
{
    public required string PackageId { get; init; }
    public required string ProjectDir { get; init; }
    public required string ProjectFile { get; init; }
    public required string OwnerSdkId { get; init; }
    public int? FrameworkMajor { get; init; }

    /// <summary>PackageIds of in-repo dependencies. Outgoing edges.</summary>
    public required IReadOnlyList<string> Dependencies { get; init; }
}

public sealed class ProjectGraph
{
    public ProjectGraph(IReadOnlyDictionary<string, ProjectNode> nodes)
    {
        Nodes = nodes;
        ReverseAdjacency = BuildReverseAdjacency(nodes);
    }

    public IReadOnlyDictionary<string, ProjectNode> Nodes { get; }

    /// <summary>For each package, the set of its in-repo consumers (incoming edges).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReverseAdjacency { get; }

    /// <summary>
    /// Kahn's algorithm. Yields nodes such that for every emitted node, every
    /// in-repo dependency has already been emitted. Throws
    /// <see cref="CycleException"/> if the graph isn't a DAG.
    /// </summary>
    public IReadOnlyList<ProjectNode> TopologicalOrder()
    {
        var inDegree = Nodes.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Dependencies.Count(d => Nodes.ContainsKey(d)),
            StringComparer.Ordinal);

        var ready = new Queue<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));

        var order = new List<ProjectNode>(capacity: Nodes.Count);
        while (ready.TryDequeue(out var id))
        {
            order.Add(Nodes[id]);
            if (!ReverseAdjacency.TryGetValue(id, out var consumers)) continue;
            foreach (var consumer in consumers)
            {
                if (--inDegree[consumer] == 0) ready.Enqueue(consumer);
            }
        }

        if (order.Count != Nodes.Count)
        {
            var leftover = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            throw new CycleException(FindCyclePath(leftover));
        }

        return order;
    }

    /// <summary>
    /// Returns the transitive set of consumers of <paramref name="changed"/>,
    /// including <paramref name="changed"/> itself. BFS over reverse edges.
    /// </summary>
    public ISet<string> AffectedClosure(IEnumerable<string> changed)
    {
        var affected = new HashSet<string>(changed, StringComparer.Ordinal);
        var queue = new Queue<string>(affected);

        while (queue.TryDequeue(out var id))
        {
            if (!ReverseAdjacency.TryGetValue(id, out var consumers)) continue;
            foreach (var consumer in consumers)
            {
                if (affected.Add(consumer)) queue.Enqueue(consumer);
            }
        }

        return affected;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildReverseAdjacency(
        IReadOnlyDictionary<string, ProjectNode> nodes)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            foreach (var dep in node.Dependencies)
            {
                if (!nodes.ContainsKey(dep)) continue; // external dep
                if (!map.TryGetValue(dep, out var list))
                {
                    list = new List<string>();
                    map[dep] = list;
                }
                list.Add(node.PackageId);
            }
        }
        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.AsReadOnly(),
            StringComparer.Ordinal);
    }

    private IReadOnlyList<string> FindCyclePath(IReadOnlyList<string> candidates)
    {
        // DFS three-color from the first candidate to extract a concrete cycle.
        const int White = 0, Gray = 1, Black = 2;
        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in Nodes.Keys) color[id] = White;

        var stack = new Stack<(string Id, IEnumerator<string> DepIter)>();
        foreach (var start in candidates)
        {
            if (color[start] != White) continue;
            stack.Push((start, Nodes[start].Dependencies.Where(Nodes.ContainsKey).GetEnumerator()));
            color[start] = Gray;

            while (stack.Count > 0)
            {
                var (id, iter) = stack.Peek();
                if (iter.MoveNext())
                {
                    var dep = iter.Current;
                    if (color[dep] == Gray)
                    {
                        // Back-edge: build the cycle path.
                        var path = stack.Select(s => s.Id).Reverse().SkipWhile(x => x != dep).ToList();
                        path.Add(dep);
                        return path;
                    }
                    if (color[dep] == White)
                    {
                        color[dep] = Gray;
                        stack.Push((dep, Nodes[dep].Dependencies.Where(Nodes.ContainsKey).GetEnumerator()));
                    }
                }
                else
                {
                    color[id] = Black;
                    stack.Pop();
                }
            }
        }

        return candidates; // fallback: at least name the unreachable nodes
    }
}
