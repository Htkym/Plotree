using System.Globalization;
using System.Text;
using System.Xml;
using Plotree.Helpers;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>Exports the complete persisted plot graph to a standalone SVG document.</summary>
public static class SvgExportService
{
    private const double HeaderHeight = 24;
    private const double AccentWidth = 4;
    private const double CardCornerRadius = 8;
    private const double EdgeStrokeWidth = 2;
    private const double ArrowLength = 10;
    private const double ArrowHalfWidth = 5;
    private const double LabelPaddingHorizontal = 6;
    private const double LabelHeight = 20;
    private const double LabelMaxWidth = 240;
    private const double TextTopMargin = 4;
    private const double TextBottomMargin = 4;

    /// <summary>
    /// Produces a transparent-background SVG containing every node, edge, port
    /// attachment, and edge label in <paramref name="project"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the persisted graph contains invalid node coordinates, duplicate
    /// node identifiers, or an edge whose endpoint is not in the project.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when requested export bounds are invalid or too large.
    /// </exception>
    public static string Export(PlotProject project, SvgExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new SvgExportOptions();
        ValidateOptions(options);

        var nodes = project.Nodes
            ?? throw new ArgumentException("The project node collection cannot be null.", nameof(project));
        var edges = project.Edges
            ?? throw new ArgumentException("The project edge collection cannot be null.", nameof(project));

        var nodeById = new Dictionary<string, NodeInfo>(StringComparer.Ordinal);
        var graphBounds = new Bounds();
        foreach (var node in nodes)
        {
            if (node is null)
            {
                throw new ArgumentException("The project contains a null node.", nameof(project));
            }

            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new ArgumentException("Every node must have a non-empty identifier.", nameof(project));
            }

            if (!double.IsFinite(node.X) || !double.IsFinite(node.Y))
            {
                throw new ArgumentException(
                    $"Node '{node.Id}' has non-finite coordinates.",
                    nameof(project));
            }

            var appearance = AppearanceResolver.Resolve(project, node);
            var info = new NodeInfo(
                node,
                appearance,
                node.X,
                node.Y,
                ResolveTagColors(project, node.TagNames));

            if (!nodeById.TryAdd(node.Id, info))
            {
                throw new ArgumentException(
                    $"More than one node uses the identifier '{node.Id}'.",
                    nameof(project));
            }

            graphBounds.IncludeRectangle(info.X, info.Y, appearance.Width, appearance.Height);
        }

        var edgeInfos = new List<EdgeInfo>(edges.Count);
        foreach (var edge in edges)
        {
            if (edge is null)
            {
                throw new ArgumentException("The project contains a null edge.", nameof(project));
            }

            if (!nodeById.TryGetValue(edge.FromId, out var from)
                || !nodeById.TryGetValue(edge.ToId, out var to))
            {
                throw new ArgumentException(
                    $"Edge '{edge.Id}' references a node that is not in the project.",
                    nameof(project));
            }

            var edgeInfo = CreateEdgeInfo(edge, from, to, project.LayoutDirection);
            edgeInfos.Add(edgeInfo);
            graphBounds.IncludeBezier(edgeInfo.Start, edgeInfo.Control1, edgeInfo.Control2, edgeInfo.End);
            graphBounds.IncludePoints(edgeInfo.Arrow);
            if (edgeInfo.Label is not null)
            {
                graphBounds.IncludeRectangle(
                    edgeInfo.Label.X,
                    edgeInfo.Label.Y,
                    edgeInfo.Label.Width,
                    edgeInfo.Label.Height);
            }
        }

        if (!graphBounds.HasValue)
        {
            graphBounds.IncludePoint(0, 0);
        }

        graphBounds.Expand(options.Padding + EdgeStrokeWidth);
        ValidateBounds(graphBounds, options.MaxDimension);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.Entitize,
        };

        var output = new StringBuilder();
        using (var textWriter = new Utf8StringWriter(output))
        using (var writer = XmlWriter.Create(textWriter, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            writer.WriteAttributeString("version", "1.1");
            writer.WriteAttributeString("viewBox", JoinNumbers(
                graphBounds.MinX,
                graphBounds.MinY,
                graphBounds.Width,
                graphBounds.Height));
            writer.WriteAttributeString("width", Number(graphBounds.Width));
            writer.WriteAttributeString("height", Number(graphBounds.Height));
            writer.WriteAttributeString("role", "img");
            writer.WriteAttributeString("aria-labelledby", "plotree-title");

            writer.WriteStartElement("title");
            writer.WriteAttributeString("id", "plotree-title");
            writer.WriteString(XmlSafe(project.Title));
            writer.WriteEndElement();

            writer.WriteStartElement("g");
            writer.WriteAttributeString("fill", "none");
            writer.WriteAttributeString("stroke", "#6B6B6B");
            writer.WriteAttributeString("stroke-width", Number(EdgeStrokeWidth));
            writer.WriteAttributeString("stroke-linecap", "round");
            foreach (var edge in edgeInfos)
            {
                WriteEdge(writer, edge);
            }

            writer.WriteEndElement();

            writer.WriteStartElement("g");
            var nodeIndex = 0;
            foreach (var node in nodeById.Values)
            {
                WriteNode(writer, node, nodeIndex);
                nodeIndex++;
            }

            writer.WriteEndElement();

            writer.WriteStartElement("g");
            foreach (var edge in edgeInfos)
            {
                if (edge.Label is not null)
                {
                    WriteLabel(writer, edge.Label);
                }
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToString();
    }

    private static EdgeInfo CreateEdgeInfo(
        PlotEdge edge,
        NodeInfo from,
        NodeInfo to,
        LayoutDirection direction)
    {
        var fromSide = edge.FromSide ?? DefaultFromSide(direction);
        var toSide = edge.ToSide ?? DefaultToSide(direction);
        var start = GetAnchor(from, fromSide);
        var end = GetAnchor(to, toSide);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var handle = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) * 0.5, 40, 200);
        var fromNormal = GetSideNormal(fromSide);
        var toNormal = GetSideNormal(toSide);
        var control1 = new Point(start.X + fromNormal.X * handle, start.Y + fromNormal.Y * handle);
        var control2 = new Point(end.X + toNormal.X * handle, end.Y + toNormal.Y * handle);
        var arrow = CreateArrow(control2, end);
        var label = CreateLabel(edge.Label, start, control1, control2, end);
        return new EdgeInfo(start, control1, control2, end, arrow, label);
    }

    private static void WriteEdge(XmlWriter writer, EdgeInfo edge)
    {
        writer.WriteStartElement("path");
        writer.WriteAttributeString("d", $"M {Pair(edge.Start)} C {Pair(edge.Control1)} {Pair(edge.Control2)} {Pair(edge.End)}");
        writer.WriteEndElement();

        writer.WriteStartElement("polygon");
        writer.WriteAttributeString("fill", "#6B6B6B");
        writer.WriteAttributeString("stroke", "none");
        writer.WriteAttributeString("points", string.Join(" ", edge.Arrow.Select(Pair)));
        writer.WriteEndElement();
    }

    private static void WriteNode(XmlWriter writer, NodeInfo node, int nodeIndex)
    {
        var appearance = node.Appearance;
        var cardFill = "#FFFFFF";
        var headerFill = ToSvgColor(appearance.HeaderColor, DefaultHeaderColor(node.Model.Type));

        writer.WriteStartElement("g");
        writer.WriteAttributeString("data-node-id", XmlSafe(node.Model.Id));

        writer.WriteStartElement("rect");
        writer.WriteAttributeString("x", Number(node.X));
        writer.WriteAttributeString("y", Number(node.Y));
        writer.WriteAttributeString("width", Number(appearance.Width));
        writer.WriteAttributeString("height", Number(appearance.Height));
        writer.WriteAttributeString("rx", Number(CardCornerRadius));
        writer.WriteAttributeString("fill", cardFill);
        writer.WriteAttributeString("stroke", "#C8C8C8");
        writer.WriteEndElement();

        for (var index = 0; index < node.AccentColors.Count; index++)
        {
            var segmentHeight = appearance.Height / node.AccentColors.Count;
            WriteRectangle(
                writer,
                node.X,
                node.Y + segmentHeight * index,
                AccentWidth,
                index == node.AccentColors.Count - 1
                    ? appearance.Height - segmentHeight * index
                    : segmentHeight,
                ToSvgColor(node.AccentColors[index], "#000000"),
                stroke: "none");
        }

        WriteRectangle(
            writer,
            node.X + AccentWidth,
            node.Y,
            appearance.Width - AccentWidth,
            HeaderHeight,
            headerFill,
            stroke: "none");

        var contentX = node.X + AccentWidth + 8;
        var contentWidth = appearance.Width - AccentWidth - 16;
        var contentY = node.Y + HeaderHeight + TextTopMargin;
        var contentHeight = Math.Max(0, appearance.Height - HeaderHeight - TextTopMargin - TextBottomMargin);
        var textLayout = CreateNodeTextLayout(
            DisplayTitle(node.Model),
            node.Model.Body,
            appearance.DisplayMode,
            contentWidth,
            contentY,
            contentHeight);
        var clipId = $"node-text-clip-{nodeIndex}";

        writer.WriteStartElement("clipPath");
        writer.WriteAttributeString("id", clipId);
        WriteRectangle(writer, contentX, contentY, contentWidth, contentHeight, "none", "none");
        writer.WriteEndElement();

        writer.WriteStartElement("g");
        writer.WriteAttributeString("clip-path", $"url(#{clipId})");
        WriteTextLines(
            writer,
            textLayout.TitleLines,
            contentX,
            textLayout.TitleFirstBaseline,
            "#1A1A1A",
            NodeTextLayoutCalculator.TitleFontSize,
            NodeTextLayoutCalculator.TitleLineHeight,
            bold: true);
        WriteTextLines(
            writer,
            textLayout.BodyLines,
            contentX,
            textLayout.BodyFirstBaseline,
            "#616161",
            NodeTextLayoutCalculator.BodyFontSize,
            NodeTextLayoutCalculator.BodyLineHeight,
            bold: false);
        writer.WriteEndElement();

        writer.WriteStartElement("text");
        writer.WriteAttributeString("x", Number(node.X + AccentWidth + 8));
        writer.WriteAttributeString("y", Number(node.Y + 16));
        writer.WriteAttributeString("fill", "#FFFFFF");
        writer.WriteAttributeString("font-family", "Segoe UI, sans-serif");
        writer.WriteAttributeString("font-size", "11");
        writer.WriteAttributeString("font-weight", "600");
        writer.WriteString(XmlSafe(NodeTypeName(node.Model.Type)));
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static void WriteLabel(XmlWriter writer, LabelInfo label)
    {
        WriteRectangle(writer, label.X, label.Y, label.Width, label.Height, "#FFFFFF", "#C8C8C8", 4);
        WriteTextLines(
            writer,
            label.Lines,
            label.X + LabelPaddingHorizontal,
            label.Y + 14,
            "#303030",
            NodeTextLayoutCalculator.BodyFontSize,
            NodeTextLayoutCalculator.BodyLineHeight,
            bold: false);
    }

    private static void WriteRectangle(
        XmlWriter writer,
        double x,
        double y,
        double width,
        double height,
        string fill,
        string stroke,
        double cornerRadius = 0)
    {
        writer.WriteStartElement("rect");
        writer.WriteAttributeString("x", Number(x));
        writer.WriteAttributeString("y", Number(y));
        writer.WriteAttributeString("width", Number(width));
        writer.WriteAttributeString("height", Number(height));
        writer.WriteAttributeString("fill", fill);
        writer.WriteAttributeString("stroke", stroke);
        if (cornerRadius > 0)
        {
            writer.WriteAttributeString("rx", Number(cornerRadius));
        }

        writer.WriteEndElement();
    }

    private static void WriteTextLines(
        XmlWriter writer,
        IReadOnlyList<string> lines,
        double x,
        double firstBaseline,
        string color,
        double fontSize,
        double lineHeight,
        bool bold)
    {
        if (lines.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("text");
        writer.WriteAttributeString("x", Number(x));
        writer.WriteAttributeString("y", Number(firstBaseline));
        writer.WriteAttributeString("fill", color);
        writer.WriteAttributeString("font-family", "Segoe UI, sans-serif");
        writer.WriteAttributeString("font-size", Number(fontSize));
        if (bold)
        {
            writer.WriteAttributeString("font-weight", "600");
        }

        for (var index = 0; index < lines.Count; index++)
        {
            writer.WriteStartElement("tspan");
            writer.WriteAttributeString("x", Number(x));
            if (index > 0)
            {
                writer.WriteAttributeString("dy", Number(lineHeight));
            }

            writer.WriteString(SvgTextWrapper.SanitizeXmlText(lines[index]));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static LabelInfo? CreateLabel(string? label, Point p0, Point p1, Point p2, Point p3)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var maxTextWidth = LabelMaxWidth - LabelPaddingHorizontal * 2;
        var lines = SvgTextWrapper.Wrap(label, maxTextWidth, 2, NodeTextLayoutCalculator.BodyFontSize);
        var width = Math.Min(
            LabelMaxWidth,
            Math.Max(
                36,
                (lines.Count == 0 ? 0 : lines.Max(line => SvgTextWrapper.EstimateWidth(
                    line,
                    NodeTextLayoutCalculator.BodyFontSize)))
                + LabelPaddingHorizontal * 2));
        var height = LabelHeight + (lines.Count - 1) * NodeTextLayoutCalculator.BodyLineHeight;
        var center = CubicPoint(p0, p1, p2, p3, 0.5);
        return new LabelInfo(
            center.X - width / 2,
            center.Y - height / 2,
            width,
            height,
            lines);
    }

    private static NodeTextLayout CreateNodeTextLayout(
        string title,
        string? body,
        NodeDisplayMode displayMode,
        double contentWidth,
        double contentY,
        double contentHeight)
    {
        var hasBody = displayMode != NodeDisplayMode.TitleOnly && !string.IsNullOrWhiteSpace(body);
        var budget = NodeTextLayoutCalculator.Calculate(
            title,
            displayMode,
            contentWidth,
            contentHeight,
            hasBody);
        var titleLines = SvgTextWrapper.Wrap(
            title,
            contentWidth,
            budget.TitleLineCapacity,
            NodeTextLayoutCalculator.TitleFontSize);
        var bodyLines = Array.Empty<string>();
        var bodyFirstBaseline = 0d;
        if (budget.BodyLineCapacity > 0)
        {
            var usedByTitle = titleLines.Count * NodeTextLayoutCalculator.TitleLineHeight;
            bodyLines = SvgTextWrapper.Wrap(
                body,
                contentWidth,
                budget.BodyLineCapacity,
                NodeTextLayoutCalculator.BodyFontSize).ToArray();
            bodyFirstBaseline = contentY
                + usedByTitle
                + NodeTextLayoutCalculator.SectionSpacing
                + NodeTextLayoutCalculator.BodyFontSize;
        }

        return new NodeTextLayout(
            titleLines,
            bodyLines,
            contentY + NodeTextLayoutCalculator.TitleFontSize,
            bodyFirstBaseline);
    }

    private static Point GetAnchor(NodeInfo node, EdgeSide side) => side switch
    {
        EdgeSide.Left => new Point(node.X, node.Y + node.Appearance.Height / 2),
        EdgeSide.Top => new Point(node.X + node.Appearance.Width / 2, node.Y),
        EdgeSide.Bottom => new Point(node.X + node.Appearance.Width / 2, node.Y + node.Appearance.Height),
        _ => new Point(node.X + node.Appearance.Width, node.Y + node.Appearance.Height / 2),
    };

    private static EdgeSide DefaultFromSide(LayoutDirection direction) =>
        direction == LayoutDirection.TopToBottom ? EdgeSide.Bottom : EdgeSide.Right;

    private static EdgeSide DefaultToSide(LayoutDirection direction) =>
        direction == LayoutDirection.TopToBottom ? EdgeSide.Top : EdgeSide.Left;

    private static Point GetSideNormal(EdgeSide side) => side switch
    {
        EdgeSide.Left => new Point(-1, 0),
        EdgeSide.Top => new Point(0, -1),
        EdgeSide.Bottom => new Point(0, 1),
        _ => new Point(1, 0),
    };

    private static IReadOnlyList<Point> CreateArrow(Point control, Point endpoint)
    {
        var x = endpoint.X - control.X;
        var y = endpoint.Y - control.Y;
        var length = Math.Sqrt(x * x + y * y);
        if (length < 0.001)
        {
            x = 1;
            length = 1;
        }

        x /= length;
        y /= length;
        var baseX = endpoint.X - x * ArrowLength;
        var baseY = endpoint.Y - y * ArrowLength;
        var normalX = -y;
        var normalY = x;
        return
        [
            endpoint,
            new Point(baseX + normalX * ArrowHalfWidth, baseY + normalY * ArrowHalfWidth),
            new Point(baseX - normalX * ArrowHalfWidth, baseY - normalY * ArrowHalfWidth),
        ];
    }

    private static Point CubicPoint(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var inverse = 1 - t;
        return new Point(
            inverse * inverse * inverse * p0.X
            + 3 * inverse * inverse * t * p1.X
            + 3 * inverse * t * t * p2.X
            + t * t * t * p3.X,
            inverse * inverse * inverse * p0.Y
            + 3 * inverse * inverse * t * p1.Y
            + 3 * inverse * t * t * p2.Y
            + t * t * t * p3.Y);
    }

    private static IReadOnlyList<string> ResolveTagColors(PlotProject project, IEnumerable<string> tagNames) =>
        tagNames
            .Select(tagName => project.Tags?.FirstOrDefault(tag =>
                tag is not null && string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase))?.Color)
            .OfType<string>()
            .ToArray();

    private static string ToSvgColor(string? color, string fallback)
    {
        var parsed = ColorHex.Parse(color);
        if (parsed is null)
        {
            return fallback;
        }

        var value = parsed.Value;
        return value.A == byte.MaxValue
            ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
            : FormattableString.Invariant(
                $"rgba({value.R}, {value.G}, {value.B}, {value.A / 255d:0.###})");
    }

    private static string DefaultHeaderColor(NodeType type) => type switch
    {
        NodeType.Choice => "#C77E1E",
        NodeType.Ending => "#C74E4E",
        _ => "#4F6BED",
    };

    private static string DisplayTitle(PlotNode node) =>
        string.IsNullOrWhiteSpace(node.Title) ? Loc.Get("Default_UntitledNode") : node.Title;

    private static string NodeTypeName(NodeType type) => Loc.Get($"NodeType_{type}");

    private static void ValidateOptions(SvgExportOptions options)
    {
        if (!double.IsFinite(options.Padding) || options.Padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SVG padding must be finite and non-negative.");
        }

        if (!double.IsFinite(options.MaxDimension) || options.MaxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The SVG maximum dimension must be finite and greater than zero.");
        }
    }

    private static void ValidateBounds(Bounds bounds, double maxDimension)
    {
        if (!bounds.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "The graph export bounds must be finite.");
        }

        if (bounds.Width > maxDimension || bounds.Height > maxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                $"The graph bounds ({Number(bounds.Width)} by {Number(bounds.Height)}) exceed the configured maximum dimension.");
        }
    }

    private static string XmlSafe(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (char.IsHighSurrogate(character))
            {
                if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], character))
                {
                    sanitized.Append(character);
                    sanitized.Append(value[++i]);
                }
                else
                {
                    sanitized.Append('\uFFFD');
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                // Lone low surrogate (unpaired) — replace.
                sanitized.Append('\uFFFD');
            }
            else
            {
                sanitized.Append(XmlConvert.IsXmlChar(character) ? character : '\uFFFD');
            }
        }

        return sanitized.ToString();
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pair(Point point) => $"{Number(point.X)} {Number(point.Y)}";

    private static string JoinNumbers(params double[] values) =>
        string.Join(" ", values.Select(Number));

    private readonly record struct NodeInfo(
        PlotNode Model,
        ResolvedAppearance Appearance,
        double X,
        double Y,
        IReadOnlyList<string> AccentColors);

    private readonly record struct EdgeInfo(
        Point Start,
        Point Control1,
        Point Control2,
        Point End,
        IReadOnlyList<Point> Arrow,
        LabelInfo? Label);

    private sealed record NodeTextLayout(
        IReadOnlyList<string> TitleLines,
        IReadOnlyList<string> BodyLines,
        double TitleFirstBaseline,
        double BodyFirstBaseline);

    private sealed record LabelInfo(
        double X,
        double Y,
        double Width,
        double Height,
        IReadOnlyList<string> Lines);

    private readonly record struct Point(double X, double Y);

    private sealed class Bounds
    {
        public double MinX { get; private set; } = double.PositiveInfinity;
        public double MinY { get; private set; } = double.PositiveInfinity;
        public double MaxX { get; private set; } = double.NegativeInfinity;
        public double MaxY { get; private set; } = double.NegativeInfinity;

        public bool HasValue => MinX <= MaxX && MinY <= MaxY;

        public bool IsFinite =>
            double.IsFinite(MinX)
            && double.IsFinite(MinY)
            && double.IsFinite(MaxX)
            && double.IsFinite(MaxY);

        public double Width => MaxX - MinX;

        public double Height => MaxY - MinY;

        public void IncludePoint(double x, double y)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }

        public void IncludePoints(IEnumerable<Point> points)
        {
            foreach (var point in points)
            {
                IncludePoint(point.X, point.Y);
            }
        }

        public void IncludeRectangle(double x, double y, double width, double height)
        {
            IncludePoint(x, y);
            IncludePoint(x + width, y + height);
        }

        public void IncludeBezier(Point p0, Point p1, Point p2, Point p3)
        {
            // A cubic Bezier lies within the convex hull of its four control points.
            // Including the complete hull is robust and also leaves room for the stroke.
            IncludePoints([p0, p1, p2, p3]);
        }

        public void Expand(double amount)
        {
            MinX -= amount;
            MinY -= amount;
            MaxX += amount;
            MaxY += amount;
        }
    }

    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
