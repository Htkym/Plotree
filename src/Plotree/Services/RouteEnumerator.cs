using Plotree.Models;

namespace Plotree.Services;

/// <summary>One step of a route: a node plus the choice label on the edge that led into it.</summary>
public sealed record RouteStep(PlotNode Node, string? ChoiceLabel);

/// <summary>Result of enumerating all story routes through the plot graph.</summary>
public sealed class RouteAnalysis
{
    /// <summary>All routes from every root node, each a sequence of steps.</summary>
    public List<List<RouteStep>> Routes { get; } = [];

    /// <summary>True when enumeration stopped at the route cap.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>True when the project has no Ending nodes and routes are maximal paths instead.</summary>
    public bool HasNoEndings { get; set; }

    /// <summary>Non-Ending nodes with no outgoing edges (likely unfinished plot lines).</summary>
    public List<PlotNode> DeadEnds { get; } = [];

    /// <summary>Nodes that cannot be reached from any root node.</summary>
    public List<PlotNode> Unreachable { get; } = [];

    /// <summary>Root nodes (indegree zero) the enumeration started from, in document order.</summary>
    public List<PlotNode> Roots { get; } = [];
}

/// <summary>
/// Enumerates all routes from every root node (indegree zero) to each Ending node via DFS,
/// so a project may hold any number of independent story openings. A graph where every node
/// lies on a cycle has no indegree-zero node; the first node in document order is used instead
/// so analysis still produces routes. Cycle-safe (a node is visited at most once per path) and
/// capped at <see cref="MaxRoutes"/> routes.
/// </summary>
public static class RouteEnumerator
{
    public const int MaxRoutes = 200;

    public static RouteAnalysis Analyze(PlotProject project)
    {
        var analysis = new RouteAnalysis();
        var nodesById = new Dictionary<string, PlotNode>();
        foreach (var node in project.Nodes)
        {
            nodesById.TryAdd(node.Id, node);
        }

        var outgoing = project.Nodes.ToDictionary(n => n.Id, _ => new List<PlotEdge>());
        foreach (var edge in project.Edges)
        {
            if (edge.FromId != edge.ToId
                && outgoing.TryGetValue(edge.FromId, out var list)
                && nodesById.ContainsKey(edge.ToId))
            {
                list.Add(edge);
            }
        }

        analysis.HasNoEndings = !project.Nodes.Any(n => n.Type == NodeType.Ending);

        var reachable = new HashSet<string>();
        foreach (var root in GraphRoots.Find(project.Nodes, project.Edges))
        {
            analysis.Roots.Add(root);
            if (!reachable.Add(root.Id))
            {
                continue; // already covered as a descendant of an earlier root
            }

            Walk(root, [new RouteStep(root, null)], [root.Id]);
        }

        foreach (var node in project.Nodes)
        {
            if (!reachable.Contains(node.Id))
            {
                analysis.Unreachable.Add(node);
            }

            if (node.Type != NodeType.Ending && outgoing[node.Id].Count == 0)
            {
                analysis.DeadEnds.Add(node);
            }
        }

        return analysis;

        void Walk(PlotNode current, List<RouteStep> path, HashSet<string> onPath)
        {
            if (analysis.Routes.Count >= MaxRoutes)
            {
                analysis.IsTruncated = true;
                return;
            }

            var isRouteEnd = current.Type == NodeType.Ending;
            var extended = false;

            if (!isRouteEnd)
            {
                foreach (var edge in outgoing[current.Id])
                {
                    var next = nodesById[edge.ToId];
                    reachable.Add(next.Id);
                    if (onPath.Contains(next.Id))
                    {
                        continue; // cycle — don't revisit within one path
                    }

                    extended = true;
                    path.Add(new RouteStep(next, edge.Label));
                    onPath.Add(next.Id);
                    Walk(next, path, onPath);
                    onPath.Remove(next.Id);
                    path.RemoveAt(path.Count - 1);
                }
            }

            // Record: reached an Ending, or (no-endings fallback) a maximal path.
            if ((isRouteEnd || (analysis.HasNoEndings && !extended)) && analysis.Routes.Count < MaxRoutes)
            {
                analysis.Routes.Add([.. path]);
            }
        }
    }
}
