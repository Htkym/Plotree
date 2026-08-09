using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>Shared graph builders and geometry helpers for the model/service tests.</summary>
internal static class TestGraph
{
    public static PlotNode Node(
        string id,
        NodeType type = NodeType.Scene,
        string? title = null,
        NodeAppearance? appearance = null,
        bool isPinned = false) => new()
        {
            Id = id,
            Type = type,
            Title = title ?? id,
            Appearance = appearance,
            IsPinned = isPinned,
        };

    public static PlotEdge Edge(string from, string to, string? label = null) => new()
    {
        Id = $"{from}->{to}",
        FromId = from,
        ToId = to,
        Label = label,
    };

    /// <summary>Builds a project from nodes plus "from>to" edge shorthand.</summary>
    public static PlotProject Project(IEnumerable<PlotNode> nodes, params string[] edges)
    {
        var project = new PlotProject { Title = "Test project" };
        project.Nodes.AddRange(nodes);
        foreach (var spec in edges)
        {
            var parts = spec.Split('>', 2);
            project.Edges.Add(Edge(parts[0], parts[1]));
        }

        return project;
    }

    /// <summary>Resolved card rectangle (x, y, width, height) of a node.</summary>
    public static (double X, double Y, double Width, double Height) Rect(PlotProject project, PlotNode node)
    {
        var appearance = AppearanceResolver.Resolve(project, node);
        return (node.X, node.Y, appearance.Width, appearance.Height);
    }

    /// <summary>True when two axis-aligned rectangles share any interior area.</summary>
    public static bool Overlaps(
        (double X, double Y, double Width, double Height) a,
        (double X, double Y, double Width, double Height) b)
    {
        const double Tolerance = 1e-9;
        return a.X + a.Width - Tolerance > b.X
            && b.X + b.Width - Tolerance > a.X
            && a.Y + a.Height - Tolerance > b.Y
            && b.Y + b.Height - Tolerance > a.Y;
    }

    /// <summary>Titles of every node on a route, in order.</summary>
    public static string Path(List<RouteStep> route) =>
        string.Join(">", route.Select(step => step.Node.Id));

    /// <summary>All routes of an analysis rendered as "a>b>c" strings.</summary>
    public static List<string> Paths(RouteAnalysis analysis) =>
        analysis.Routes.Select(Path).ToList();
}
