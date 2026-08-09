using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>
/// Regression coverage for the variable-size, multi-root, cycle-safe automatic layout.
/// </summary>
[TestClass]
public sealed class AutoLayoutServiceTests
{
    /// <summary>Graph with deliberately mixed card sizes across and within layers.</summary>
    private static PlotProject MixedSizeGraph(LayoutDirection direction)
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("root", NodeType.Scene, appearance: new NodeAppearance { Width = 420, Height = 300 }),
                TestGraph.Node("b", NodeType.Choice),
                TestGraph.Node("c", NodeType.Choice, appearance: new NodeAppearance { Width = 96, Height = 460 }),
                TestGraph.Node("e", NodeType.Scene, appearance: new NodeAppearance { Width = 10_000, Height = 10_000 }),
                TestGraph.Node("d", NodeType.Ending),
                TestGraph.Node("second_root"),
                TestGraph.Node("f", NodeType.Ending, appearance: new NodeAppearance { Width = 100 }),
            ],
            "root>b",
            "root>c",
            "root>e",
            "b>d",
            "c>d",
            "second_root>f");
        project.LayoutDirection = direction;
        project.Appearance.Choice = new NodeAppearance { Width = 260, Height = 90 };
        project.Appearance.Ending = new NodeAppearance { Height = 200 };
        return project;
    }

    private static void AssertNoOverlaps(PlotProject project)
    {
        var rects = project.Nodes
            .Where(n => !n.IsPinned)
            .Select(n => (n.Id, Rect: TestGraph.Rect(project, n)))
            .ToList();

        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.IsFalse(
                    TestGraph.Overlaps(rects[i].Rect, rects[j].Rect),
                    $"'{rects[i].Id}' {rects[i].Rect} overlaps '{rects[j].Id}' {rects[j].Rect}.");
            }
        }
    }

    [TestMethod]
    public void Apply_EmptyProject_DoesNotThrow()
    {
        AutoLayoutService.Apply(new PlotProject());
    }

    [TestMethod]
    [DataRow(LayoutDirection.LeftToRight)]
    [DataRow(LayoutDirection.TopToBottom)]
    public void Apply_VariableCardSizes_NeverOverlap(LayoutDirection direction)
    {
        var project = MixedSizeGraph(direction);

        AutoLayoutService.Apply(project);

        AssertNoOverlaps(project);
        Assert.IsTrue(project.Nodes.All(n => double.IsFinite(n.X) && double.IsFinite(n.Y)));
    }

    [TestMethod]
    [DataRow(LayoutDirection.LeftToRight)]
    [DataRow(LayoutDirection.TopToBottom)]
    public void Apply_IsIdempotent(LayoutDirection direction)
    {
        var project = MixedSizeGraph(direction);

        AutoLayoutService.Apply(project);
        var first = project.Nodes.Select(n => (n.Id, n.X, n.Y)).ToList();
        AutoLayoutService.Apply(project);
        var second = project.Nodes.Select(n => (n.Id, n.X, n.Y)).ToList();

        CollectionAssert.AreEqual(first, second, "Re-running layout on an already laid out graph must be stable.");
        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_MultipleRoots_ShareTheFirstLayerBand()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r1"),
                TestGraph.Node("r2"),
                TestGraph.Node("a"),
                TestGraph.Node("b"),
            ],
            "r1>a",
            "r2>b");
        project.LayoutDirection = LayoutDirection.LeftToRight;

        AutoLayoutService.Apply(project);

        var r1 = project.Nodes.Single(n => n.Id == "r1");
        var r2 = project.Nodes.Single(n => n.Id == "r2");
        var a = project.Nodes.Single(n => n.Id == "a");

        Assert.AreEqual(r1.X, r2.X, 1e-9, "Both roots are layer 0, so both sit in the same column.");
        Assert.AreNotEqual(r1.Y, r2.Y, "Roots in the same layer must occupy separate slots.");
        Assert.IsTrue(a.X > r1.X, "Successors must be placed in a later layer band.");
        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_MultipleRoots_TopToBottom_ShareTheFirstLayerBand()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r1"),
                TestGraph.Node("r2"),
                TestGraph.Node("a"),
            ],
            "r1>a",
            "r2>a");
        project.LayoutDirection = LayoutDirection.TopToBottom;

        AutoLayoutService.Apply(project);

        var r1 = project.Nodes.Single(n => n.Id == "r1");
        var r2 = project.Nodes.Single(n => n.Id == "r2");
        var a = project.Nodes.Single(n => n.Id == "a");

        Assert.AreEqual(r1.Y, r2.Y, 1e-9);
        Assert.IsTrue(a.Y > r1.Y);
        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_CyclicGraph_TerminatesAndProducesFinitePositions()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("a"),
                TestGraph.Node("b"),
                TestGraph.Node("c"),
            ],
            "a>b",
            "b>c",
            "c>a");

        AutoLayoutService.Apply(project);

        Assert.IsTrue(project.Nodes.All(n => double.IsFinite(n.X) && double.IsFinite(n.Y)));
        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_CycleReachableFromARoot_TerminatesAndDoesNotOverlap()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("a"),
                TestGraph.Node("b"),
                TestGraph.Node("c"),
            ],
            "r>a",
            "a>b",
            "b>c",
            "c>a");

        AutoLayoutService.Apply(project);

        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_UnreachableNodes_GetTrailingLayersWithoutOverlap()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("a"),
                TestGraph.Node("island_from"),
                TestGraph.Node("island_to"),
            ],
            "r>a",
            "island_from>island_to",
            "island_to>island_from");

        AutoLayoutService.Apply(project);

        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_PinnedNode_KeepsItsPositionButStillReservesItsSlot()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("free1"),
                TestGraph.Node("pinned", isPinned: true),
                TestGraph.Node("free2"),
            ]);
        project.LayoutDirection = LayoutDirection.LeftToRight;

        // Within-layer order in LeftToRight comes from the current Y, so seed it explicitly.
        var pinnedNode = project.Nodes.Single(n => n.Id == "pinned");
        project.Nodes.Single(n => n.Id == "free1").Y = 0;
        pinnedNode.X = 4_242;
        pinnedNode.Y = 100;
        project.Nodes.Single(n => n.Id == "free2").Y = 200;

        AutoLayoutService.Apply(project);

        Assert.AreEqual(4_242d, pinnedNode.X, "A pinned node keeps its manual X.");
        Assert.AreEqual(100d, pinnedNode.Y, "A pinned node keeps its manual Y.");

        var free1 = project.Nodes.Single(n => n.Id == "free1");
        var free2 = project.Nodes.Single(n => n.Id == "free2");
        Assert.IsFalse(TestGraph.Overlaps(TestGraph.Rect(project, free1), TestGraph.Rect(project, free2)));

        // free1 and free2 are separated by two slots because the pinned card still holds one.
        var gap = Math.Abs(free1.Y - free2.Y);
        Assert.IsTrue(
            gap > 2 * NodeAppearanceFallback.Height,
            $"The pinned card must still consume a slot between the two free cards, but the gap was {gap}.");
    }

    [TestMethod]
    public void Apply_ClampedOversizeCard_UsesTheClampedSizeForSpacing()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("huge", appearance: new NodeAppearance { Width = 10_000, Height = 10_000 }),
                TestGraph.Node("small"),
            ]);
        project.LayoutDirection = LayoutDirection.LeftToRight;

        AutoLayoutService.Apply(project);

        var huge = project.Nodes.Single(n => n.Id == "huge");
        var small = project.Nodes.Single(n => n.Id == "small");

        // Both are roots, so they stack along Y within layer 0 separated by the clamped height.
        var gap = Math.Abs(huge.Y - small.Y);
        Assert.IsTrue(
            gap >= NodeAppearanceFallback.MaxHeight,
            $"Spacing must use the clamped card height, but the gap was {gap}.");
        Assert.IsTrue(gap < NodeAppearanceFallback.MaxHeight + 1_000, "Spacing must not use the unclamped 10000 height.");
        AssertNoOverlaps(project);
    }

    [TestMethod]
    public void Apply_SingleNode_IsPlacedAtTheMargin()
    {
        var project = TestGraph.Project([TestGraph.Node("only")]);

        AutoLayoutService.Apply(project);

        Assert.AreEqual(80d, project.Nodes[0].X, 1e-9);
        Assert.AreEqual(80d, project.Nodes[0].Y, 1e-9);
    }
}
