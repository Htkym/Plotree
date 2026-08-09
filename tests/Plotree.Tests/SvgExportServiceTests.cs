using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>
/// Regression coverage for the standalone SVG graph export: well-formed XML, escaped and
/// sanitized text, and bounds that cover the whole graph and stay within the configured limit.
/// </summary>
[TestClass]
public sealed class SvgExportServiceTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static PlotProject SimpleProject()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("a", NodeType.Scene, "Alpha"),
                TestGraph.Node("b", NodeType.Ending, "Omega"),
            ]);
        project.Nodes[0].Body = "Some body text";
        project.Nodes[1].X = 400;
        project.Nodes[1].Y = 200;
        project.Edges.Add(TestGraph.Edge("a", "b", "Continue"));
        return project;
    }

    private static XDocument ParseSvg(string svg)
    {
        var document = XDocument.Parse(svg);
        Assert.IsNotNull(document.Root);
        Assert.AreEqual(Svg + "svg", document.Root.Name, "The root element must be an SVG element.");
        return document;
    }

    [TestMethod]
    public void Export_ProducesWellFormedSvgWithADeclaration()
    {
        var svg = SvgExportService.Export(SimpleProject());

        Assert.IsTrue(svg.StartsWith("<?xml", StringComparison.Ordinal));
        var document = ParseSvg(svg);
        Assert.IsNotNull(document.Root!.Attribute("viewBox"));
        Assert.IsNotNull(document.Root.Attribute("width"));
        Assert.IsNotNull(document.Root.Attribute("height"));
        Assert.AreEqual("img", document.Root.Attribute("role")?.Value);
        Assert.AreEqual("plotree-title", document.Root.Attribute("aria-labelledby")?.Value);
        Assert.AreEqual("Test project", document.Root.Element(Svg + "title")?.Value);
    }

    [TestMethod]
    public void Export_EmptyProject_StillProducesValidSvg()
    {
        var svg = SvgExportService.Export(new PlotProject());

        var document = ParseSvg(svg);
        var viewBox = ParseViewBox(document);
        Assert.IsTrue(viewBox.Width > 0);
        Assert.IsTrue(viewBox.Height > 0);
    }

    [TestMethod]
    public void Export_EscapesMarkupInTitlesBodiesAndLabels()
    {
        var project = TestGraph.Project([TestGraph.Node("a", NodeType.Scene, "<b>&\"'x")]);
        project.Title = "<script>alert(\"pwned\")</script> & 'more'";
        project.Nodes[0].Body = "body<img/>&amp;";
        project.Nodes.Add(TestGraph.Node("b", NodeType.Ending, "end"));
        project.Nodes[1].X = 400;
        project.Edges.Add(TestGraph.Edge("a", "b", "<label>&x"));

        var svg = SvgExportService.Export(project);

        Assert.IsFalse(svg.Contains("<script>", StringComparison.Ordinal), "Raw markup must never reach the output.");
        Assert.IsFalse(svg.Contains("<img/>", StringComparison.Ordinal));
        Assert.IsTrue(svg.Contains("&lt;script&gt;", StringComparison.Ordinal), "Markup must be entity-escaped.");

        var document = ParseSvg(svg);
        Assert.AreEqual(project.Title, document.Root!.Element(Svg + "title")?.Value, "Escaping must be lossless.");

        var text = string.Concat(document.Descendants(Svg + "tspan").Select(e => e.Value));
        Assert.IsTrue(text.Contains("<b>&\"'x", StringComparison.Ordinal), $"Node title text was lost: '{text}'");
        Assert.IsTrue(text.Contains("<label>&x", StringComparison.Ordinal), $"Edge label text was lost: '{text}'");
    }

    [TestMethod]
    public void Export_ReplacesCharactersThatAreIllegalInXml()
    {
        var project = TestGraph.Project([TestGraph.Node("a", NodeType.Scene, "ok")]);
        project.Title = "bad\u0000\u0001\u001Fend";

        var svg = SvgExportService.Export(project);

        var document = ParseSvg(svg);
        var title = document.Root!.Element(Svg + "title")!.Value;
        Assert.AreEqual("bad\uFFFD\uFFFD\uFFFDend", title);
        Assert.IsTrue(title.All(XmlConvert.IsXmlChar));
    }

    [TestMethod]
    public void Export_SurrogatePairsSurviveIntact()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Title = "emoji \U0001F600 ok";

        var document = ParseSvg(SvgExportService.Export(project));

        Assert.AreEqual("emoji \U0001F600 ok", document.Root!.Element(Svg + "title")!.Value);
    }

    [TestMethod]
    public void Export_ViewBoxCoversTheGraphPlusPaddingAndStroke()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);

        var document = ParseSvg(SvgExportService.Export(project, new SvgExportOptions { Padding = 32 }));

        // Default card is 180 x 76 at (0, 0); padding 32 plus the 2-unit edge stroke allowance.
        var viewBox = ParseViewBox(document);
        Assert.AreEqual(-34d, viewBox.MinX, 1e-6);
        Assert.AreEqual(-34d, viewBox.MinY, 1e-6);
        Assert.AreEqual(180d + 68, viewBox.Width, 1e-6);
        Assert.AreEqual(76d + 68, viewBox.Height, 1e-6);
        Assert.AreEqual(viewBox.Width, double.Parse(document.Root!.Attribute("width")!.Value, CultureInfo.InvariantCulture), 1e-6);
        Assert.AreEqual(viewBox.Height, double.Parse(document.Root.Attribute("height")!.Value, CultureInfo.InvariantCulture), 1e-6);
    }

    [TestMethod]
    public void Export_ViewBoxEnclosesEveryNodeRectangle()
    {
        var project = SimpleProject();
        project.Nodes[0].X = -250;
        project.Nodes[0].Y = -125;

        var document = ParseSvg(SvgExportService.Export(project));
        var viewBox = ParseViewBox(document);

        foreach (var node in project.Nodes)
        {
            var rect = TestGraph.Rect(project, node);
            Assert.IsTrue(rect.X >= viewBox.MinX, $"Node '{node.Id}' starts left of the view box.");
            Assert.IsTrue(rect.Y >= viewBox.MinY, $"Node '{node.Id}' starts above the view box.");
            Assert.IsTrue(rect.X + rect.Width <= viewBox.MinX + viewBox.Width, $"Node '{node.Id}' extends past the right edge.");
            Assert.IsTrue(rect.Y + rect.Height <= viewBox.MinY + viewBox.Height, $"Node '{node.Id}' extends past the bottom edge.");
        }
    }

    [TestMethod]
    public void Export_UsesInvariantNumberFormatting()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var project = TestGraph.Project([TestGraph.Node("a")]);
            project.Nodes[0].X = 10.5;

            var document = ParseSvg(SvgExportService.Export(project));
            var viewBox = document.Root!.Attribute("viewBox")!.Value;

            Assert.IsFalse(viewBox.Contains(',', StringComparison.Ordinal), $"Decimal comma leaked into '{viewBox}'.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void Export_RendersOneGroupPerNodeWithItsIdentifier()
    {
        var document = ParseSvg(SvgExportService.Export(SimpleProject()));

        var ids = document.Descendants(Svg + "g")
            .Select(g => g.Attribute("data-node-id")?.Value)
            .Where(id => id is not null)
            .ToList();

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, ids);
    }

    [TestMethod]
    public void Export_BoundsExceedingTheMaximumDimension_Throws()
    {
        var project = SimpleProject();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => SvgExportService.Export(project, new SvgExportOptions { MaxDimension = 10 }));
    }

    [TestMethod]
    public void Export_HugeButValidGraph_IsAcceptedWithinTheDefaultLimit()
    {
        var project = TestGraph.Project([TestGraph.Node("a"), TestGraph.Node("b")]);
        project.Nodes[1].X = 500_000;
        project.Nodes[1].Y = 500_000;

        var document = ParseSvg(SvgExportService.Export(project));

        Assert.IsTrue(ParseViewBox(document).Width > 500_000);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(-1d)]
    public void Export_InvalidPadding_Throws(double padding)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => SvgExportService.Export(SimpleProject(), new SvgExportOptions { Padding = padding }));
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(-1d)]
    [DataRow(double.NaN)]
    public void Export_InvalidMaxDimension_Throws(double maxDimension)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => SvgExportService.Export(SimpleProject(), new SvgExportOptions { MaxDimension = maxDimension }));
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public void Export_NonFiniteNodeCoordinates_Throw(double x)
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Nodes[0].X = x;

        Assert.ThrowsExactly<ArgumentException>(() => SvgExportService.Export(project));
    }

    [TestMethod]
    public void Export_DuplicateNodeIdentifiers_Throw()
    {
        var project = TestGraph.Project([TestGraph.Node("a"), TestGraph.Node("a")]);

        Assert.ThrowsExactly<ArgumentException>(() => SvgExportService.Export(project));
    }

    [TestMethod]
    public void Export_BlankNodeIdentifier_Throws()
    {
        var project = TestGraph.Project([TestGraph.Node("   ")]);

        Assert.ThrowsExactly<ArgumentException>(() => SvgExportService.Export(project));
    }

    [TestMethod]
    public void Export_EdgeReferencingAMissingNode_Throws()
    {
        var project = TestGraph.Project([TestGraph.Node("a")], "a>ghost");

        Assert.ThrowsExactly<ArgumentException>(() => SvgExportService.Export(project));
    }

    [TestMethod]
    public void Export_NullProject_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SvgExportService.Export(null!));
    }

    [TestMethod]
    public void Export_HonorsPersistedPortSides()
    {
        var project = SimpleProject();
        project.Edges[0].FromSide = EdgeSide.Top;
        project.Edges[0].ToSide = EdgeSide.Bottom;

        var document = ParseSvg(SvgExportService.Export(project));

        var path = document.Descendants(Svg + "path").First().Attribute("d")!.Value;
        // Top port of the default 180 x 76 card at (0, 0) is its top-centre point.
        Assert.IsTrue(path.StartsWith("M 90 0 ", StringComparison.Ordinal), $"Unexpected path start: '{path}'.");
    }

    [TestMethod]
    public void Export_NodeTextIsClippedAndVerticallyBudgetedToTheResolvedCard()
    {
        var project = TestGraph.Project([TestGraph.Node("a", NodeType.Scene, "A title that is deliberately much too long for a small card")]);
        project.Nodes[0].Body = "A body that would previously continue below the card boundary.";
        project.Nodes[0].Appearance = new NodeAppearance
        {
            Width = NodeAppearanceFallback.MinWidth,
            Height = NodeAppearanceFallback.MinHeight,
            DisplayMode = NodeDisplayMode.Full,
        };

        var document = ParseSvg(SvgExportService.Export(project));
        var node = document.Descendants(Svg + "g")
            .Single(group => group.Attribute("data-node-id")?.Value == "a");
        var clipPath = node.Element(Svg + "clipPath");
        Assert.IsNotNull(clipPath, "Every node's title/body region must be clipped.");
        var clip = clipPath.Element(Svg + "rect")!;
        Assert.AreEqual(8d + 4d, double.Parse(clip.Attribute("x")!.Value, CultureInfo.InvariantCulture), 1e-6);
        Assert.AreEqual(28d, double.Parse(clip.Attribute("y")!.Value, CultureInfo.InvariantCulture), 1e-6);
        Assert.AreEqual(16d, double.Parse(clip.Attribute("height")!.Value, CultureInfo.InvariantCulture), 1e-6);

        var textGroup = node.Elements(Svg + "g")
            .Single(group => group.Attribute("clip-path") is not null);
        Assert.IsTrue(textGroup.Elements(Svg + "text").Any(), "The title should remain visible in a minimum-height card.");
        Assert.IsFalse(textGroup.Value.Contains("body that", StringComparison.Ordinal), "No body line fits after the title budget.");
    }

    [TestMethod]
    public void Export_PreservesNewlinesAndValidGraphemesInNodeText()
    {
        var project = TestGraph.Project([TestGraph.Node("a", NodeType.Scene, "😀 e\u0301\nSecond title line")]);
        project.Nodes[0].Appearance = new NodeAppearance
        {
            Height = 120,
            DisplayMode = NodeDisplayMode.TitleOnly,
        };

        var document = ParseSvg(SvgExportService.Export(project));
        var node = document.Descendants(Svg + "g")
            .Single(group => group.Attribute("data-node-id")?.Value == "a");
        var text = node.Descendants(Svg + "tspan").Select(span => span.Value).ToArray();

        CollectionAssert.AreEqual(new[] { "😀 e\u0301", "Second title line" }, text);
    }

    [TestMethod]
    public void Export_RendersEveryTagAsAnEqualOrderedAccentSegment()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Nodes[0].TagNames.AddRange(["Red", "Blue"]);
        project.Tags.Add(new ColorTag { Name = "Red", Color = "#FF0000" });
        project.Tags.Add(new ColorTag { Name = "Blue", Color = "#0000FF" });

        var document = ParseSvg(SvgExportService.Export(project));
        var node = document.Descendants(Svg + "g")
            .Single(group => group.Attribute("data-node-id")?.Value == "a");
        var accents = node.Elements(Svg + "rect")
            .Where(rect => rect.Attribute("width")?.Value == "4")
            .ToArray();

        Assert.AreEqual(2, accents.Length);
        CollectionAssert.AreEqual(
            new[] { "#FF0000", "#0000FF" },
            accents.Select(rect => rect.Attribute("fill")!.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { "0", "38" },
            accents.Select(rect => rect.Attribute("y")!.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { "38", "38" },
            accents.Select(rect => rect.Attribute("height")!.Value).ToArray());
    }

    [TestMethod]
    public void Export_FullModeBodyLineBudgetGrowsWithCardHeight()
    {
        var project = TestGraph.Project([TestGraph.Node("a", title: "Title")]);
        project.Nodes[0].Body = string.Join(' ', Enumerable.Repeat("body", 80));
        var appearance = new NodeAppearance { Width = 120, Height = 76 };
        project.Nodes[0].Appearance = appearance;
        var shortDocument = ParseSvg(SvgExportService.Export(project));
        var shortLines = BodyLines(shortDocument, "a");

        appearance.Height = 200;
        var tallDocument = ParseSvg(SvgExportService.Export(project));
        var tallLines = BodyLines(tallDocument, "a");

        Assert.IsTrue(tallLines > shortLines, $"Expected a taller card to show more body lines ({shortLines} vs {tallLines}).");
        Assert.IsTrue(tallLines > 3, "Full mode must no longer be capped at three body lines.");
    }

    private static int BodyLines(XDocument document, string nodeId)
    {
        var node = document.Descendants(Svg + "g")
            .Single(group => group.Attribute("data-node-id")?.Value == nodeId);
        return node.Descendants(Svg + "text")
            .Where(text => text.Attribute("font-size")?.Value == "12")
            .SelectMany(text => text.Elements(Svg + "tspan"))
            .Count();
    }

    private static (double MinX, double MinY, double Width, double Height) ParseViewBox(XDocument document)
    {
        var parts = document.Root!.Attribute("viewBox")!.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.AreEqual(4, parts.Length);
        return (parts[0], parts[1], parts[2], parts[3]);
    }
}
