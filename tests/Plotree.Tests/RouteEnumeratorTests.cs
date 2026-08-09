using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>
/// Regression coverage for multi-root, cycle-safe route enumeration.
/// </summary>
[TestClass]
public sealed class RouteEnumeratorTests
{
    [TestMethod]
    public void Analyze_EmptyProject_ReturnsNothing()
    {
        var analysis = RouteEnumerator.Analyze(new PlotProject());

        Assert.AreEqual(0, analysis.Roots.Count);
        Assert.AreEqual(0, analysis.Routes.Count);
        Assert.AreEqual(0, analysis.DeadEnds.Count);
        Assert.AreEqual(0, analysis.Unreachable.Count);
        Assert.IsFalse(analysis.IsTruncated);
        Assert.IsTrue(analysis.HasNoEndings);
    }

    [TestMethod]
    public void Analyze_MultipleRoots_EnumeratesEveryOpening()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r1"),
                TestGraph.Node("e1", NodeType.Ending),
                TestGraph.Node("r2"),
                TestGraph.Node("e2", NodeType.Ending),
            ],
            "r1>e1",
            "r2>e2");

        var analysis = RouteEnumerator.Analyze(project);

        CollectionAssert.AreEqual(new[] { "r1", "r2" }, analysis.Roots.Select(n => n.Id).ToList());
        CollectionAssert.AreEquivalent(new[] { "r1>e1", "r2>e2" }, TestGraph.Paths(analysis));
        Assert.AreEqual(0, analysis.Unreachable.Count);
        Assert.AreEqual(0, analysis.DeadEnds.Count);
    }

    [TestMethod]
    public void Analyze_ChoiceRoot_IsSupportedAsAnOpening()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("c", NodeType.Choice),
                TestGraph.Node("e", NodeType.Ending),
            ],
            "c>e");

        var analysis = RouteEnumerator.Analyze(project);

        Assert.AreEqual(1, analysis.Roots.Count);
        Assert.AreEqual(NodeType.Choice, analysis.Roots[0].Type);
        CollectionAssert.AreEqual(new[] { "c>e" }, TestGraph.Paths(analysis));
    }

    [TestMethod]
    public void Analyze_BranchingGraph_EnumeratesEveryRouteWithChoiceLabels()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("l", NodeType.Choice),
                TestGraph.Node("x"),
                TestGraph.Node("e1", NodeType.Ending),
                TestGraph.Node("e2", NodeType.Ending),
            ]);
        project.Edges.Add(TestGraph.Edge("r", "l"));
        project.Edges.Add(TestGraph.Edge("l", "x", "Left"));
        project.Edges.Add(TestGraph.Edge("l", "e2", "Right"));
        project.Edges.Add(TestGraph.Edge("x", "e1"));

        var analysis = RouteEnumerator.Analyze(project);

        CollectionAssert.AreEquivalent(new[] { "r>l>x>e1", "r>l>e2" }, TestGraph.Paths(analysis));

        var rightRoute = analysis.Routes.Single(route => TestGraph.Path(route) == "r>l>e2");
        Assert.IsNull(rightRoute[0].ChoiceLabel, "The root step has no incoming edge label.");
        Assert.AreEqual("Right", rightRoute[^1].ChoiceLabel);
    }

    [TestMethod]
    public void Analyze_CycleOffARoot_TerminatesAndVisitsEachNodeOncePerPath()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("a"),
                TestGraph.Node("b"),
                TestGraph.Node("c"),
                TestGraph.Node("end", NodeType.Ending),
            ],
            "r>a",
            "a>b",
            "b>c",
            "c>a",
            "c>end");

        var analysis = RouteEnumerator.Analyze(project);

        CollectionAssert.AreEqual(new[] { "r>a>b>c>end" }, TestGraph.Paths(analysis));
        Assert.IsFalse(analysis.IsTruncated);
        Assert.AreEqual(0, analysis.Unreachable.Count);
    }

    [TestMethod]
    public void Analyze_FullyCyclicGraph_FallsBackToTheFirstNodeAsRoot()
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

        var analysis = RouteEnumerator.Analyze(project);

        Assert.AreEqual(1, analysis.Roots.Count);
        Assert.AreEqual("a", analysis.Roots[0].Id, "With no indegree-zero node the first node in document order is used.");
        Assert.IsTrue(analysis.HasNoEndings);
        CollectionAssert.AreEqual(new[] { "a>b>c" }, TestGraph.Paths(analysis));
        Assert.AreEqual(0, analysis.Unreachable.Count);
    }

    [TestMethod]
    public void Analyze_SelfLoop_IsIgnored()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("a"),
                TestGraph.Node("e", NodeType.Ending),
            ],
            "a>a",
            "a>e");

        var analysis = RouteEnumerator.Analyze(project);

        Assert.AreEqual(1, analysis.Roots.Count);
        Assert.AreEqual("a", analysis.Roots[0].Id, "A self-loop must not give a node an indegree.");
        CollectionAssert.AreEqual(new[] { "a>e" }, TestGraph.Paths(analysis));
    }

    [TestMethod]
    public void Analyze_EdgeToMissingNode_IsIgnored()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("a"),
                TestGraph.Node("e", NodeType.Ending),
            ],
            "a>e",
            "a>ghost",
            "ghost>e");

        var analysis = RouteEnumerator.Analyze(project);

        CollectionAssert.AreEqual(new[] { "a>e" }, TestGraph.Paths(analysis));
    }

    [TestMethod]
    public void Analyze_NoEndings_ReportsMaximalPaths()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("a"),
                TestGraph.Node("b"),
            ],
            "r>a",
            "r>b");

        var analysis = RouteEnumerator.Analyze(project);

        Assert.IsTrue(analysis.HasNoEndings);
        CollectionAssert.AreEquivalent(new[] { "r>a", "r>b" }, TestGraph.Paths(analysis));
    }

    [TestMethod]
    public void Analyze_ReportsDeadEndsAndUnreachableNodes()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("stuck"),
                TestGraph.Node("e", NodeType.Ending),
                TestGraph.Node("island_from"),
                TestGraph.Node("island_to"),
            ],
            "r>stuck",
            "r>e",
            "island_from>island_to",
            "island_to>island_from");

        var analysis = RouteEnumerator.Analyze(project);

        CollectionAssert.AreEquivalent(
            new[] { "stuck" },
            analysis.DeadEnds.Select(n => n.Id).ToList());

        // island_from/island_to form a closed pair: neither has indegree zero, so
        // neither is a root and neither is reachable from one.
        CollectionAssert.AreEquivalent(
            new[] { "island_from", "island_to" },
            analysis.Unreachable.Select(n => n.Id).ToList());
    }

    [TestMethod]
    public void Analyze_EndingNodeOutgoingEdges_DoNotExtendTheRoute()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("r"),
                TestGraph.Node("e", NodeType.Ending),
                TestGraph.Node("after"),
            ],
            "r>e",
            "e>after");

        var analysis = RouteEnumerator.Analyze(project);

        CollectionAssert.AreEqual(new[] { "r>e" }, TestGraph.Paths(analysis));
    }

    [TestMethod]
    public void Analyze_TooManyRoutes_IsCappedAndFlaggedTruncated()
    {
        var nodes = new List<PlotNode> { TestGraph.Node("r") };
        var edges = new List<string>();
        var endingCount = RouteEnumerator.MaxRoutes + 25;
        for (var i = 0; i < endingCount; i++)
        {
            nodes.Add(TestGraph.Node($"e{i}", NodeType.Ending));
            edges.Add($"r>e{i}");
        }

        var analysis = RouteEnumerator.Analyze(TestGraph.Project(nodes, [.. edges]));

        Assert.AreEqual(RouteEnumerator.MaxRoutes, analysis.Routes.Count);
        Assert.IsTrue(analysis.IsTruncated);
    }

    [TestMethod]
    public void Analyze_DenseCyclicGraph_CompletesQuickly()
    {
        // Every node points at every other node, so a naive walk would never finish.
        var nodes = Enumerable.Range(0, 12).Select(i => TestGraph.Node($"n{i}")).ToList();
        var project = TestGraph.Project(nodes);
        foreach (var from in nodes)
        {
            foreach (var to in nodes)
            {
                project.Edges.Add(TestGraph.Edge(from.Id, to.Id));
            }
        }

        var analysis = RouteEnumerator.Analyze(project);

        Assert.IsTrue(analysis.Routes.Count <= RouteEnumerator.MaxRoutes);
        Assert.AreEqual(1, analysis.Roots.Count);
    }
}
