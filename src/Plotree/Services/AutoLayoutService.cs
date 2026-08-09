using Plotree.Models;

namespace Plotree.Services;

/// <summary>
/// Layered (hierarchical) auto-layout for the plot graph.
/// Layers are assigned by longest path from every root node (indegree zero); nodes not
/// reachable from any root go in trailing layers. Graphs where every node sits on a cycle
/// (no root at all) fall back to the first node in document order so a layout is still
/// produced. Within-layer order is refined with a few barycenter sweeps to reduce edge
/// crossings.
/// Card sizes are variable: every node is measured through <see cref="AppearanceResolver"/>
/// (per-node override → document per-type default → built-in fallback), so layer bands and
/// in-layer slots are sized from the actual cards instead of a fixed constant.
/// Hybrid rule: nodes with <see cref="PlotNode.IsPinned"/> keep their manual
/// position but still occupy their slot in the layout grid.
/// </summary>
public static class AutoLayoutService
{
    private const double Margin = 80;

    // Clear space between cards; the card's own resolved size is added on top of these.
    private const double LayerGapLR = 120; // between columns in LeftToRight
    private const double RowGapLR = 48;    // between cards within a column
    private const double LayerGapTB = 100; // between rows in TopToBottom
    private const double ColumnGapTB = 60; // between cards within a row

    /// <summary>Assigns X/Y to every unpinned node according to the project's layout direction.</summary>
    public static void Apply(PlotProject project)
    {
        if (project.Nodes.Count == 0)
        {
            return;
        }

        var layers = AssignLayers(project);
        var ordered = OrderWithinLayers(project, layers);
        AssignPositions(project, ordered);
    }

    /// <summary>Longest-path layering from every root node, cycle-safe via relaxation with a cap.</summary>
    private static Dictionary<string, int> AssignLayers(PlotProject project)
    {
        var layer = project.Nodes.ToDictionary(n => n.Id, _ => -1);
        var edges = project.Edges
            .Where(e => e.FromId != e.ToId && layer.ContainsKey(e.FromId) && layer.ContainsKey(e.ToId))
            .ToList();
        var cap = project.Nodes.Count; // guards against runaway growth in cyclic graphs

        // Multiple roots are supported: every node with indegree zero starts a layer-0 column.
        var roots = GraphRoots.Find(project.Nodes, edges);
        foreach (var root in roots)
        {
            layer[root.Id] = 0;
        }

        if (roots.Count > 0)
        {
            Relax(edges, layer, cap);
        }

        // Nodes with no path from a root (unreachable, or every node on a cycle):
        // trailing layers, layered among themselves.
        var maxReachable = layer.Values.Max();
        var trailingBase = maxReachable + 1;
        var hasUnreachable = false;
        foreach (var node in project.Nodes)
        {
            if (layer[node.Id] < 0)
            {
                layer[node.Id] = trailingBase;
                hasUnreachable = true;
            }
        }

        if (hasUnreachable)
        {
            Relax(edges, layer, trailingBase + cap);
        }

        return layer;
    }

    private static void Relax(List<PlotEdge> edges, Dictionary<string, int> layer, int cap)
    {
        for (var pass = 0; pass < layer.Count; pass++)
        {
            var changed = false;
            foreach (var edge in edges)
            {
                var from = layer[edge.FromId];
                if (from >= 0 && from < cap && layer[edge.ToId] < from + 1)
                {
                    layer[edge.ToId] = from + 1;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Groups nodes by layer and reduces crossings with barycenter sweeps
    /// (two downward passes over predecessors, one upward over successors).
    /// </summary>
    private static List<List<PlotNode>> OrderWithinLayers(PlotProject project, Dictionary<string, int> layers)
    {
        var byLayer = project.Nodes
            .GroupBy(n => layers[n.Id])
            .OrderBy(g => g.Key)
            .Select(g => g
                .OrderBy(n => project.LayoutDirection == LayoutDirection.TopToBottom ? n.X : n.Y)
                .ToList())
            .ToList();

        var predecessors = new Dictionary<string, List<string>>();
        var successors = new Dictionary<string, List<string>>();
        foreach (var edge in project.Edges)
        {
            if (edge.FromId == edge.ToId || !layers.ContainsKey(edge.FromId) || !layers.ContainsKey(edge.ToId))
            {
                continue;
            }

            (predecessors.TryGetValue(edge.ToId, out var p) ? p : predecessors[edge.ToId] = []).Add(edge.FromId);
            (successors.TryGetValue(edge.FromId, out var s) ? s : successors[edge.FromId] = []).Add(edge.ToId);
        }

        for (var sweep = 0; sweep < 3; sweep++)
        {
            var upward = sweep == 2;
            var neighbors = upward ? successors : predecessors;
            var indices = (upward
                    ? Enumerable.Range(0, byLayer.Count - 1).Reverse()
                    : Enumerable.Range(1, Math.Max(0, byLayer.Count - 1)))
                .ToList();

            foreach (var i in indices)
            {
                var reference = byLayer[upward ? i + 1 : i - 1];
                var slot = reference.Select((n, idx) => (n.Id, idx)).ToDictionary(t => t.Id, t => t.idx);

                byLayer[i] = byLayer[i]
                    .Select((node, currentIndex) =>
                    {
                        var linked = neighbors.TryGetValue(node.Id, out var ids)
                            ? ids.Where(slot.ContainsKey).Select(id => (double)slot[id]).ToList()
                            : [];
                        var barycenter = linked.Count > 0 ? linked.Average() : currentIndex;
                        return (node, barycenter, currentIndex);
                    })
                    .OrderBy(t => t.barycenter)
                    .ThenBy(t => t.currentIndex)
                    .Select(t => t.node)
                    .ToList();
            }
        }

        return byLayer;
    }

    /// <summary>
    /// Places each layer in a band whose thickness is the largest card across the layer,
    /// and stacks the layer's cards along the band leaving a fixed clear gap between them.
    /// Cards are centred across the band, so mixed sizes stay tidy and never overlap —
    /// neither within a layer nor across layers.
    /// </summary>
    private static void AssignPositions(PlotProject project, List<List<PlotNode>> byLayer)
    {
        var topToBottom = project.LayoutDirection == LayoutDirection.TopToBottom;
        var layerGap = topToBottom ? LayerGapTB : LayerGapLR;
        var slotGap = topToBottom ? ColumnGapTB : RowGapLR;
        var sizes = MeasureNodes(project);

        var bandStart = Margin;

        foreach (var layer in byLayer)
        {
            // Band thickness runs across the flow: card width in LeftToRight, height in TopToBottom.
            var thickness = 0.0;
            foreach (var node in layer)
            {
                var size = sizes[node.Id];
                thickness = Math.Max(thickness, topToBottom ? size.Height : size.Width);
            }

            var offset = Margin;
            foreach (var node in layer)
            {
                var size = sizes[node.Id];
                var across = topToBottom ? size.Height : size.Width;
                var along = topToBottom ? size.Width : size.Height;

                // Pinned nodes keep their manual position but still hold their slot,
                // reserving space sized from their own card.
                if (!node.IsPinned)
                {
                    var centred = bandStart + ((thickness - across) / 2);
                    if (topToBottom)
                    {
                        node.X = offset;
                        node.Y = centred;
                    }
                    else
                    {
                        node.X = centred;
                        node.Y = offset;
                    }
                }

                offset += along + slotGap;
            }

            bandStart += thickness + layerGap;
        }
    }

    /// <summary>Resolves every node's effective card size through the version 2 appearance chain.</summary>
    private static Dictionary<string, ResolvedAppearance> MeasureNodes(PlotProject project)
    {
        var sizes = new Dictionary<string, ResolvedAppearance>(project.Nodes.Count, StringComparer.Ordinal);
        foreach (var node in project.Nodes)
        {
            sizes[node.Id] = AppearanceResolver.Resolve(project, node);
        }

        return sizes;
    }
}
