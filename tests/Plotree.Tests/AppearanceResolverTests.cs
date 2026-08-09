using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>
/// Regression coverage for the version 2 appearance chain:
/// per-node override → document per-type default → built-in fallback, with clamped dimensions.
/// </summary>
[TestClass]
public sealed class AppearanceResolverTests
{
    [TestMethod]
    public void Resolve_NoProjectAndNoOverride_UsesBuiltInFallback()
    {
        var resolved = AppearanceResolver.Resolve(null, TestGraph.Node("a"));

        Assert.IsNull(resolved.HeaderColor);
        Assert.AreEqual(NodeAppearanceFallback.Width, resolved.Width);
        Assert.AreEqual(NodeAppearanceFallback.Height, resolved.Height);
        Assert.AreEqual(NodeAppearanceFallback.DisplayMode, resolved.DisplayMode);
    }

    [TestMethod]
    public void Resolve_DocumentDefault_WinsOverBuiltInFallback()
    {
        var project = TestGraph.Project([TestGraph.Node("a", NodeType.Choice)]);
        project.Appearance.Choice = new NodeAppearance
        {
            HeaderColor = "#112233",
            Width = 200,
            Height = 100,
            DisplayMode = NodeDisplayMode.Compact,
        };

        var resolved = AppearanceResolver.Resolve(project, project.Nodes[0]);

        Assert.AreEqual("#112233", resolved.HeaderColor);
        Assert.AreEqual(200d, resolved.Width);
        Assert.AreEqual(100d, resolved.Height);
        Assert.AreEqual(NodeDisplayMode.Compact, resolved.DisplayMode);
    }

    [TestMethod]
    public void Resolve_DocumentDefaults_AreAppliedPerNodeType()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("s"),
                TestGraph.Node("c", NodeType.Choice),
                TestGraph.Node("e", NodeType.Ending),
            ]);
        project.Appearance.Scene = new NodeAppearance { Width = 200 };
        project.Appearance.Choice = new NodeAppearance { Width = 220 };
        project.Appearance.Ending = new NodeAppearance { Width = 240 };

        Assert.AreEqual(200d, AppearanceResolver.Resolve(project, project.Nodes[0]).Width);
        Assert.AreEqual(220d, AppearanceResolver.Resolve(project, project.Nodes[1]).Width);
        Assert.AreEqual(240d, AppearanceResolver.Resolve(project, project.Nodes[2]).Width);
    }

    [TestMethod]
    public void Resolve_NodeOverride_WinsOverDocumentDefault()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Appearance.Scene = new NodeAppearance
        {
            HeaderColor = "#112233",
            Width = 200,
            Height = 100,
            DisplayMode = NodeDisplayMode.Compact,
        };
        project.Nodes[0].Appearance = new NodeAppearance
        {
            HeaderColor = "#AABBCC",
            Width = 300,
            Height = 150,
            DisplayMode = NodeDisplayMode.TitleOnly,
        };

        var resolved = AppearanceResolver.Resolve(project, project.Nodes[0]);

        Assert.AreEqual("#AABBCC", resolved.HeaderColor);
        Assert.AreEqual(300d, resolved.Width);
        Assert.AreEqual(150d, resolved.Height);
        Assert.AreEqual(NodeDisplayMode.TitleOnly, resolved.DisplayMode);
    }

    [TestMethod]
    public void Resolve_PartialNodeOverride_FallsThroughMemberByMember()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Appearance.Scene = new NodeAppearance
        {
            HeaderColor = "#112233",
            Width = 200,
            Height = 100,
            DisplayMode = NodeDisplayMode.Compact,
        };
        project.Nodes[0].Appearance = new NodeAppearance { Width = 300 };

        var resolved = AppearanceResolver.Resolve(project, project.Nodes[0]);

        Assert.AreEqual(300d, resolved.Width, "Only the specified member is overridden.");
        Assert.AreEqual("#112233", resolved.HeaderColor);
        Assert.AreEqual(100d, resolved.Height);
        Assert.AreEqual(NodeDisplayMode.Compact, resolved.DisplayMode);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Resolve_BlankHeaderColor_FallsThroughToTheNextLevel(string blank)
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Appearance.Scene = new NodeAppearance { HeaderColor = "#112233" };
        project.Nodes[0].Appearance = new NodeAppearance { HeaderColor = blank };

        Assert.AreEqual("#112233", AppearanceResolver.Resolve(project, project.Nodes[0]).HeaderColor);
    }

    [TestMethod]
    public void Resolve_BlankHeaderColorEverywhere_ResolvesToNull()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Appearance.Scene = new NodeAppearance { HeaderColor = "  " };
        project.Nodes[0].Appearance = new NodeAppearance { HeaderColor = string.Empty };

        Assert.IsNull(AppearanceResolver.Resolve(project, project.Nodes[0]).HeaderColor);
    }

    [TestMethod]
    [DataRow(10_000d, NodeAppearanceFallback.MaxWidth)]
    [DataRow(1d, NodeAppearanceFallback.MinWidth)]
    [DataRow(0d, NodeAppearanceFallback.MinWidth)]
    [DataRow(-500d, NodeAppearanceFallback.MinWidth)]
    [DataRow(NodeAppearanceFallback.MinWidth, NodeAppearanceFallback.MinWidth)]
    [DataRow(NodeAppearanceFallback.MaxWidth, NodeAppearanceFallback.MaxWidth)]
    public void Resolve_Width_IsClampedToTheSupportedRange(double requested, double expected)
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Nodes[0].Appearance = new NodeAppearance { Width = requested };

        Assert.AreEqual(expected, AppearanceResolver.Resolve(project, project.Nodes[0]).Width);
    }

    [TestMethod]
    [DataRow(10_000d, NodeAppearanceFallback.MaxHeight)]
    [DataRow(1d, NodeAppearanceFallback.MinHeight)]
    [DataRow(-1d, NodeAppearanceFallback.MinHeight)]
    [DataRow(NodeAppearanceFallback.MaxHeight, NodeAppearanceFallback.MaxHeight)]
    public void Resolve_Height_IsClampedToTheSupportedRange(double requested, double expected)
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Nodes[0].Appearance = new NodeAppearance { Height = requested };

        Assert.AreEqual(expected, AppearanceResolver.Resolve(project, project.Nodes[0]).Height);
    }

    [TestMethod]
    public void Resolve_NonFiniteDimensions_CollapseToTheMinimum()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Nodes[0].Appearance = new NodeAppearance
        {
            Width = double.NaN,
            Height = double.PositiveInfinity,
        };

        var resolved = AppearanceResolver.Resolve(project, project.Nodes[0]);

        Assert.AreEqual(NodeAppearanceFallback.MinWidth, resolved.Width);
        Assert.AreEqual(NodeAppearanceFallback.MinHeight, resolved.Height);
    }

    [TestMethod]
    public void Resolve_ClampingAlsoAppliesToDocumentDefaults()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Appearance.Scene = new NodeAppearance { Width = 5_000, Height = 5_000 };

        var resolved = AppearanceResolver.Resolve(project, project.Nodes[0]);

        Assert.AreEqual(NodeAppearanceFallback.MaxWidth, resolved.Width);
        Assert.AreEqual(NodeAppearanceFallback.MaxHeight, resolved.Height);
    }

    [TestMethod]
    public void ResolveDefaults_IgnoresPerNodeOverrides()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);
        project.Appearance.Scene = new NodeAppearance { Width = 200 };
        project.Nodes[0].Appearance = new NodeAppearance { Width = 300 };

        Assert.AreEqual(200d, AppearanceResolver.ResolveDefaults(project, NodeType.Scene).Width);
    }

    [TestMethod]
    public void Resolve_NullNode_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AppearanceResolver.Resolve(null, null!));
    }
}
