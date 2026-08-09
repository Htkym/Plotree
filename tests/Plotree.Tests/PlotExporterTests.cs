using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>
/// Regression coverage for the Markdown and plain-text route exports, in particular the
/// labelled Body and Memo sections and their localized empty marker.
/// </summary>
/// <remarks>
/// Outside a packaged/bootstrapped app MRT Core cannot resolve resources, so
/// <see cref="Loc.Get"/> deliberately returns the resource key. These tests therefore compare
/// against <c>Loc.Get(...)</c> rather than hard-coded English, which keeps them valid in both
/// situations; <see cref="ExportResourceTests"/> covers the resource values themselves.
/// </remarks>
[TestClass]
public sealed class PlotExporterTests
{
    private static readonly string BodyLabel = Loc.Get("Export_Body");
    private static readonly string MemoLabel = Loc.Get("Export_Memo");
    private static readonly string EmptyMarker = Loc.Get("Export_Empty");

    private static PlotProject TwoStepProject(string body = "Alpha body", string memo = "Alpha memo")
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("a", NodeType.Scene, "Alpha"),
                TestGraph.Node("z", NodeType.Ending, "Omega"),
            ]);
        project.Nodes[0].Body = body;
        project.Nodes[0].Memo = memo;
        project.Edges.Add(TestGraph.Edge("a", "z", "Continue"));
        return project;
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static List<int> IndicesOf(string[] lines, string value) =>
        lines.Select((line, index) => (line, index))
            .Where(entry => entry.line == value)
            .Select(entry => entry.index)
            .ToList();

    [TestMethod]
    public void ToMarkdown_LabelsBodyAndMemoForEveryNodeOnTheRoute()
    {
        var lines = Lines(PlotExporter.ToMarkdown(TwoStepProject()));

        var bodyIndices = IndicesOf(lines, $"**{BodyLabel}**");
        var memoIndices = IndicesOf(lines, $"**{MemoLabel}**");

        Assert.AreEqual(2, bodyIndices.Count, "Both nodes on the route need a labelled Body section.");
        Assert.AreEqual(2, memoIndices.Count, "Both nodes on the route need a labelled Memo section.");
        for (var i = 0; i < 2; i++)
        {
            Assert.IsTrue(bodyIndices[i] < memoIndices[i], "Body must be written before Memo.");
        }

        Assert.AreEqual("Alpha body", lines[bodyIndices[0] + 1]);
        Assert.AreEqual("Alpha memo", lines[memoIndices[0] + 1]);
    }

    [TestMethod]
    public void ToPlainText_LabelsBodyAndMemoForEveryNodeOnTheRoute()
    {
        var lines = Lines(PlotExporter.ToPlainText(TwoStepProject()));

        var bodyIndices = IndicesOf(lines, $"{BodyLabel}:");
        var memoIndices = IndicesOf(lines, $"{MemoLabel}:");

        Assert.AreEqual(2, bodyIndices.Count);
        Assert.AreEqual(2, memoIndices.Count);
        Assert.AreEqual("Alpha body", lines[bodyIndices[0] + 1]);
        Assert.AreEqual("Alpha memo", lines[memoIndices[0] + 1]);
        Assert.IsFalse(
            lines.Any(line => line.Contains("**", StringComparison.Ordinal)),
            "Plain text must not contain Markdown emphasis.");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\r\n ")]
    public void ToMarkdown_EmptyBodyAndMemo_UseTheLocalizedEmptyMarker(string blank)
    {
        var lines = Lines(PlotExporter.ToMarkdown(TwoStepProject(blank, blank)));

        var bodyIndex = IndicesOf(lines, $"**{BodyLabel}**")[0];
        var memoIndex = IndicesOf(lines, $"**{MemoLabel}**")[0];

        Assert.AreEqual(EmptyMarker, lines[bodyIndex + 1]);
        Assert.AreEqual(EmptyMarker, lines[memoIndex + 1]);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ToPlainText_EmptyBodyAndMemo_UseTheLocalizedEmptyMarker(string blank)
    {
        var lines = Lines(PlotExporter.ToPlainText(TwoStepProject(blank, blank)));

        Assert.AreEqual(EmptyMarker, lines[IndicesOf(lines, $"{BodyLabel}:")[0] + 1]);
        Assert.AreEqual(EmptyMarker, lines[IndicesOf(lines, $"{MemoLabel}:")[0] + 1]);
    }

    [TestMethod]
    public void ToMarkdown_TrailingNodeAlwaysGetsBothLabelsEvenWhenEmpty()
    {
        // The Ending node has neither Body nor Memo.
        var lines = Lines(PlotExporter.ToMarkdown(TwoStepProject()));

        var bodyIndex = IndicesOf(lines, $"**{BodyLabel}**")[1];
        var memoIndex = IndicesOf(lines, $"**{MemoLabel}**")[1];

        Assert.AreEqual(EmptyMarker, lines[bodyIndex + 1]);
        Assert.AreEqual(EmptyMarker, lines[memoIndex + 1]);
    }

    [TestMethod]
    public void ToMarkdown_PreservesMultiLineBodyAndTrimsTrailingWhitespace()
    {
        var project = TwoStepProject("first line\nsecond line   \n\n", "memo   ");

        var lines = Lines(PlotExporter.ToMarkdown(project));
        var bodyIndex = IndicesOf(lines, $"**{BodyLabel}**")[0];

        Assert.AreEqual("first line", lines[bodyIndex + 1]);
        Assert.AreEqual("second line", lines[bodyIndex + 2]);
        Assert.AreEqual(string.Empty, lines[bodyIndex + 3], "Trailing blank lines are trimmed, leaving the section separator.");
        Assert.AreEqual("memo", lines[IndicesOf(lines, $"**{MemoLabel}**")[0] + 1]);
    }

    [TestMethod]
    public void ToMarkdown_StartsWithTheProjectTitleHeading()
    {
        var lines = Lines(PlotExporter.ToMarkdown(TwoStepProject()));

        Assert.AreEqual("# Test project", lines[0]);
    }

    [TestMethod]
    public void ToPlainText_UnderlinesTheProjectTitle()
    {
        var lines = Lines(PlotExporter.ToPlainText(TwoStepProject()));

        Assert.AreEqual("Test project", lines[0]);
        Assert.AreEqual(new string('=', "Test project".Length), lines[1]);
    }

    [TestMethod]
    public void Export_UsesNodeTitlesAndTheUntitledFallback()
    {
        var project = TwoStepProject();
        project.Nodes[0].Title = "   ";

        var markdown = PlotExporter.ToMarkdown(project);

        Assert.IsTrue(markdown.Contains($"### {Loc.Get("Default_UntitledNode")}", StringComparison.Ordinal));
        Assert.IsTrue(markdown.Contains("### Omega", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Export_WritesTheChoiceMarkerBetweenSteps()
    {
        var markdown = PlotExporter.ToMarkdown(TwoStepProject());
        var plain = PlotExporter.ToPlainText(TwoStepProject());

        Assert.IsTrue(
            markdown.Contains($"> {Loc.Get("Export_Choice")}", StringComparison.Ordinal)
            || markdown.Contains("> " + Loc.Format("Export_Choice", "Continue"), StringComparison.Ordinal),
            "Markdown must quote the incoming choice.");
        Assert.IsTrue(
            plain.Contains("  -> ", StringComparison.Ordinal),
            "Plain text must indent the incoming choice.");
    }

    [TestMethod]
    public void Export_NoRoutesAndNoEndings_WriteTheirNotes()
    {
        var project = TestGraph.Project([TestGraph.Node("lonely", NodeType.Ending)]);

        var markdown = PlotExporter.ToMarkdown(project);

        Assert.IsFalse(markdown.Contains(Loc.Get("Export_NoEndings"), StringComparison.Ordinal));

        var noEndings = PlotExporter.ToMarkdown(TestGraph.Project([TestGraph.Node("a")]));
        Assert.IsTrue(noEndings.Contains(Loc.Get("Export_NoEndings"), StringComparison.Ordinal));
    }

    [TestMethod]
    public void Export_EmptyProject_ProducesTheNoRoutesNote()
    {
        var markdown = PlotExporter.ToMarkdown(new PlotProject());

        Assert.IsTrue(markdown.Contains(Loc.Get("Export_NoRoutes"), StringComparison.Ordinal));
    }

    [TestMethod]
    public void Export_ListsDeadEndAndUnreachableWarnings()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r", NodeType.Scene, "Root"),
                TestGraph.Node("stuck", NodeType.Scene, "Stuck"),
                TestGraph.Node("e", NodeType.Ending, "End"),
                TestGraph.Node("i1", NodeType.Scene, "Island one"),
                TestGraph.Node("i2", NodeType.Scene, "Island two"),
            ],
            "r>stuck",
            "r>e",
            "i1>i2",
            "i2>i1");

        var markdown = PlotExporter.ToMarkdown(project);

        Assert.IsTrue(markdown.Contains($"## {Loc.Get("Export_Warnings")}", StringComparison.Ordinal));
        var warningLines = Lines(markdown).Count(line => line.StartsWith("- ", StringComparison.Ordinal));
        Assert.AreEqual(3, warningLines, "One dead end plus two unreachable nodes.");
    }

    [TestMethod]
    public void Export_MultiRootProject_WritesEveryOpening()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r1", NodeType.Scene, "First opening"),
                TestGraph.Node("e1", NodeType.Ending, "First end"),
                TestGraph.Node("r2", NodeType.Scene, "Second opening"),
                TestGraph.Node("e2", NodeType.Ending, "Second end"),
            ],
            "r1>e1",
            "r2>e2");

        var markdown = PlotExporter.ToMarkdown(project);

        Assert.IsTrue(markdown.Contains("### First opening", StringComparison.Ordinal));
        Assert.IsTrue(markdown.Contains("### Second opening", StringComparison.Ordinal));
        Assert.AreEqual(4, IndicesOf(Lines(markdown), $"**{BodyLabel}**").Count);
    }

    [TestMethod]
    public void Export_EndsWithASingleTrailingNewLine()
    {
        var markdown = PlotExporter.ToMarkdown(TwoStepProject());

        Assert.IsTrue(markdown.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.IsFalse(markdown.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal));
    }
}
