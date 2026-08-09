using System.Text;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>Exports a plot project as a readable Markdown or plain-text document, route by route.</summary>
public static class PlotExporter
{
    public static string ToMarkdown(PlotProject project) => Build(project, markdown: true);

    public static string ToPlainText(PlotProject project) => Build(project, markdown: false);

    private static string Build(PlotProject project, bool markdown)
    {
        var analysis = RouteEnumerator.Analyze(project);
        var sb = new StringBuilder();

        // Document title
        if (markdown)
        {
            sb.AppendLine($"# {project.Title}");
        }
        else
        {
            sb.AppendLine(project.Title);
            sb.AppendLine(new string('=', Math.Max(4, project.Title.Length)));
        }

        sb.AppendLine();

        if (analysis.HasNoEndings)
        {
            AppendNote(sb, markdown, Loc.Get("Export_NoEndings"));
            sb.AppendLine();
        }

        if (analysis.Routes.Count == 0)
        {
            AppendNote(sb, markdown, Loc.Get("Export_NoRoutes"));
            sb.AppendLine();
        }

        // Routes
        for (var i = 0; i < analysis.Routes.Count; i++)
        {
            var route = analysis.Routes[i];
            var endingTitle = DisplayTitle(route[^1].Node);
            AppendHeading(sb, markdown, 2, Loc.Format("Export_Route", i + 1, endingTitle));
            sb.AppendLine();

            foreach (var step in route)
            {
                if (!string.IsNullOrWhiteSpace(step.ChoiceLabel))
                {
                    var choice = Loc.Format("Export_Choice", step.ChoiceLabel);
                    sb.AppendLine(markdown ? $"> {choice}" : $"  -> {choice}");
                    sb.AppendLine();
                }

                AppendHeading(sb, markdown, 3, DisplayTitle(step.Node));
                sb.AppendLine();

                AppendSection(sb, markdown, Loc.Get("Export_Body"), step.Node.Body);
                AppendSection(sb, markdown, Loc.Get("Export_Memo"), step.Node.Memo);
            }
        }

        // Warnings
        var warnings = new List<string>();
        if (analysis.IsTruncated)
        {
            warnings.Add(Loc.Format("Export_Truncated", RouteEnumerator.MaxRoutes));
        }

        foreach (var node in analysis.DeadEnds)
        {
            warnings.Add(Loc.Format("Export_DeadEnd", DisplayTitle(node), Loc.Get($"NodeType_{node.Type}")));
        }

        foreach (var node in analysis.Unreachable)
        {
            warnings.Add(Loc.Format("Export_Unreachable", DisplayTitle(node), Loc.Get($"NodeType_{node.Type}")));
        }

        if (warnings.Count > 0)
        {
            if (markdown)
            {
                sb.AppendLine("---");
                sb.AppendLine();
            }

            AppendHeading(sb, markdown, 2, Loc.Get("Export_Warnings"));
            sb.AppendLine();
            foreach (var warning in warnings)
            {
                sb.AppendLine($"- {warning}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendHeading(StringBuilder sb, bool markdown, int level, string text)
    {
        if (markdown)
        {
            sb.AppendLine($"{new string('#', level)} {text}");
        }
        else
        {
            sb.AppendLine(text);
            sb.AppendLine(new string(level == 2 ? '-' : '~', Math.Max(4, text.Length)));
        }
    }

    private static void AppendNote(StringBuilder sb, bool markdown, string text) =>
        sb.AppendLine(markdown ? $"> **Note:** {text}" : $"[Note] {text}");

    private static void AppendSection(StringBuilder sb, bool markdown, string label, string text)
    {
        sb.AppendLine(markdown ? $"**{label}**" : $"{label}:");
        sb.AppendLine(string.IsNullOrWhiteSpace(text) ? Loc.Get("Export_Empty") : text.TrimEnd());
        sb.AppendLine();
    }

    private static string DisplayTitle(PlotNode node) =>
        string.IsNullOrWhiteSpace(node.Title) ? Loc.Get("Default_UntitledNode") : node.Title;
}
