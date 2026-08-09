using Plotree.Models;

namespace Plotree.Services;

/// <summary>
/// Shared root detection for the plot graph. A root is a node with indegree zero
/// (self-loops don't count), which lets a project hold any number of independent
/// story openings. When every node lies on a cycle there is no indegree-zero node,
/// so the first node in document order is used to keep layout and route analysis
/// deterministic instead of empty.
/// </summary>
internal static class GraphRoots
{
    /// <summary>Returns the root nodes in document order.</summary>
    public static List<PlotNode> Find(IReadOnlyList<PlotNode> nodes, IEnumerable<PlotEdge> edges)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        var ids = new HashSet<string>(nodes.Select(n => n.Id));
        var hasIncoming = new HashSet<string>();
        foreach (var edge in edges)
        {
            if (edge.FromId != edge.ToId && ids.Contains(edge.FromId) && ids.Contains(edge.ToId))
            {
                hasIncoming.Add(edge.ToId);
            }
        }

        var roots = nodes.Where(n => !hasIncoming.Contains(n.Id)).ToList();

        // Fully cyclic graph: fall back to the first node so callers still have somewhere to start.
        if (roots.Count == 0)
        {
            roots.Add(nodes[0]);
        }

        return roots;
    }
}
