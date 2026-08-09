using Plotree.Models;
using Plotree.ViewModels;

namespace Plotree.Tests;

[TestClass]
public sealed class MainPageViewModelTests
{
    [TestMethod]
    public void WindowTitle_UsesFileNameInsteadOfProjectTitle()
    {
        var viewModel = new MainPageViewModel
        {
            CurrentFilePath = Path.Combine("C:", "stories", "chapter-one.plotree"),
        };

        viewModel.ProjectTitle = "A different project title";

        Assert.AreEqual("chapter-one.plotree * — Plotree", viewModel.WindowTitle);
    }

    [TestMethod]
    public void AutoLayout_LayoutsNewNodesWhilePreservingPinsUntilRelayoutAll()
    {
        var viewModel = new MainPageViewModel();
        var root = viewModel.AddNode(NodeType.Scene, 900, 700);
        var ending = viewModel.AddNode(NodeType.Ending, 1_500, 1_100);
        var rootId = root.Model.Id;
        var endingId = ending.Model.Id;
        viewModel.AddEdge(root, ending, EdgeSide.Right, EdgeSide.Left);

        Assert.IsFalse(root.Model.IsPinned);
        Assert.IsFalse(ending.Model.IsPinned);

        viewModel.AutoLayoutCommand.Execute(null);

        var laidOutRoot = viewModel.Project.Nodes.Single(node => node.Id == rootId);
        var laidOutEnding = viewModel.Project.Nodes.Single(node => node.Id == endingId);
        Assert.AreEqual(80d, laidOutRoot.X, 1e-9);
        Assert.AreEqual(80d, laidOutRoot.Y, 1e-9);
        Assert.IsFalse(laidOutRoot.IsPinned);
        Assert.IsFalse(laidOutEnding.IsPinned);

        laidOutRoot.IsPinned = true;
        laidOutRoot.X = 4_242;
        laidOutRoot.Y = 2_424;

        viewModel.AutoLayoutCommand.Execute(null);

        laidOutRoot = viewModel.Project.Nodes.Single(node => node.Id == rootId);
        Assert.IsTrue(laidOutRoot.IsPinned);
        Assert.AreEqual(4_242d, laidOutRoot.X, 1e-9);
        Assert.AreEqual(2_424d, laidOutRoot.Y, 1e-9);

        viewModel.RelayoutAllCommand.Execute(null);

        laidOutRoot = viewModel.Project.Nodes.Single(node => node.Id == rootId);
        Assert.IsFalse(laidOutRoot.IsPinned);
        Assert.AreEqual(80d, laidOutRoot.X, 1e-9);
        Assert.AreEqual(80d, laidOutRoot.Y, 1e-9);
    }

    [TestMethod]
    public void TagAndCharacterOptions_ApplyAcrossSelectionAndExposeMixedState()
    {
        var viewModel = new MainPageViewModel();
        var first = viewModel.AddNode(NodeType.Scene, 0, 0);
        var second = viewModel.AddNode(NodeType.Scene, 300, 0);
        viewModel.SelectNodes([first, second], isAdditive: false);

        viewModel.AddTag("Urgent", "#E5484D");
        viewModel.AddCharacter("Ari");

        var tag = viewModel.Tags.Single();
        var character = viewModel.Characters.Single();
        character.IsChecked = true;

        CollectionAssert.AreEqual(new[] { "Urgent" }, first.Model.TagNames);
        CollectionAssert.AreEqual(new[] { "Urgent" }, second.Model.TagNames);
        CollectionAssert.AreEqual(new[] { character.Id }, first.Model.CharacterIds);
        CollectionAssert.AreEqual(new[] { character.Id }, second.Model.CharacterIds);

        viewModel.SelectNode(first);
        tag.IsChecked = false;
        character.IsChecked = false;
        viewModel.SelectNodes([first, second], isAdditive: false);

        Assert.IsNull(tag.IsChecked);
        Assert.IsNull(character.IsChecked);
        Assert.IsFalse(first.HasTag("Urgent"));
        Assert.IsTrue(second.HasTag("Urgent"));
    }

    [TestMethod]
    public void CharacterGroups_SupportMultipleMemberships()
    {
        var viewModel = new MainPageViewModel();
        viewModel.AddCharacter("Ari");
        viewModel.AddGroup("Leads");
        viewModel.AddGroup("Investigators");

        foreach (var group in viewModel.Groups)
        {
            group.IsChecked = true;
        }

        CollectionAssert.AreEquivalent(
            viewModel.Groups.Select(group => group.Id).ToArray(),
            viewModel.SelectedCharacter!.Model.GroupIds);
        StringAssert.Contains(viewModel.SelectedCharacter!.GroupSummary, "Leads");
        StringAssert.Contains(viewModel.SelectedCharacter.GroupSummary, "Investigators");
    }

    [TestMethod]
    public void EditAndDeleteTag_UpdatesEveryNodeReference()
    {
        var viewModel = new MainPageViewModel();
        var first = viewModel.AddNode(NodeType.Scene, 0, 0);
        var second = viewModel.AddNode(NodeType.Scene, 300, 0);
        viewModel.SelectNodes([first, second], isAdditive: false);
        viewModel.AddTag("Urgent", "#E5484D");

        Assert.IsTrue(viewModel.EditTag(viewModel.SelectedTag, "Critical", "#FF0000"));
        CollectionAssert.AreEqual(new[] { "Critical" }, viewModel.Project.Nodes[0].TagNames);
        CollectionAssert.AreEqual(new[] { "Critical" }, viewModel.Project.Nodes[1].TagNames);

        viewModel.DeleteTag(viewModel.SelectedTag);
        Assert.IsEmpty(viewModel.Project.Tags);
        Assert.IsTrue(viewModel.Project.Nodes.All(node => node.TagNames.Count == 0));
        Assert.IsTrue(viewModel.IsDirty);
    }

    [TestMethod]
    public void DeleteCharacterAndGroup_RemovesAllReferences()
    {
        var viewModel = new MainPageViewModel();
        var node = viewModel.AddNode(NodeType.Scene, 0, 0);
        viewModel.SelectNode(node);
        viewModel.AddCharacter("Ari");
        var character = viewModel.SelectedCharacter!;
        character.IsChecked = true;
        viewModel.AddGroup("Leads");
        var group = viewModel.SelectedGroup!;
        viewModel.Groups.Single().IsChecked = true;

        viewModel.DeleteGroup(group);
        Assert.IsEmpty(viewModel.Project.Groups);
        Assert.IsEmpty(viewModel.Project.Characters.Single().GroupIds);

        viewModel.DeleteCharacter(viewModel.SelectedCharacter);
        Assert.IsEmpty(viewModel.Project.Characters);
        Assert.IsEmpty(viewModel.Project.Nodes.Single().CharacterIds);
    }

    [TestMethod]
    public void TextEdit_RecordsOneUndoableTransition()
    {
        var viewModel = new MainPageViewModel();
        var original = viewModel.ProjectTitle;

        viewModel.BeginProjectTitleTextEdit();
        viewModel.ProjectTitle = "First draft";
        viewModel.ProjectTitle = "Second draft";
        viewModel.CompleteTextEdit();

        viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(original, viewModel.ProjectTitle);
        Assert.IsFalse(viewModel.IsDirty);

        viewModel.RedoCommand.Execute(null);
        Assert.AreEqual("Second draft", viewModel.ProjectTitle);
        Assert.IsTrue(viewModel.IsDirty);
    }

    [TestMethod]
    public void TypeHeaderColor_ClearsOnlyMatchingNodeColorOverrides_AsOneUndoableChange()
    {
        var viewModel = new MainPageViewModel();
        var scene = viewModel.AddNode(NodeType.Scene, 0, 0);
        var choice = viewModel.AddNode(NodeType.Choice, 300, 0);
        scene.Model.Appearance = new NodeAppearance
        {
            HeaderColor = "#111111",
            Width = 250,
            Height = 140,
            DisplayMode = NodeDisplayMode.Compact,
        };
        choice.Model.Appearance = new NodeAppearance { HeaderColor = "#222222" };

        viewModel.AppearanceTypeIndex = 0;
        viewModel.TypeHeaderColor = "#ABCDEF";

        Assert.AreEqual("#ABCDEF", viewModel.Project.Appearance.Scene.HeaderColor);
        Assert.IsNull(scene.Model.Appearance!.HeaderColor);
        Assert.AreEqual(250d, scene.Model.Appearance.Width);
        Assert.AreEqual(140d, scene.Model.Appearance.Height);
        Assert.AreEqual(NodeDisplayMode.Compact, scene.Model.Appearance.DisplayMode);
        Assert.AreEqual("#222222", choice.Model.Appearance!.HeaderColor);
        Assert.AreEqual("#ABCDEF", scene.EffectiveHeaderColor);

        viewModel.UndoCommand.Execute(null);
        var restoredScene = viewModel.Project.Nodes.Single(node => node.Id == scene.Model.Id);
        Assert.AreEqual("#111111", restoredScene.Appearance!.HeaderColor);
        Assert.IsNull(viewModel.Project.Appearance.Scene.HeaderColor);

        viewModel.RedoCommand.Execute(null);
        restoredScene = viewModel.Project.Nodes.Single(node => node.Id == scene.Model.Id);
        Assert.IsNull(restoredScene.Appearance!.HeaderColor);
        Assert.AreEqual("#ABCDEF", viewModel.Project.Appearance.Scene.HeaderColor);
    }

    [TestMethod]
    public void TypeHeaderColor_NullsAnEmptyMatchingNodeAppearance()
    {
        var viewModel = new MainPageViewModel();
        var node = viewModel.AddNode(NodeType.Scene, 0, 0);
        node.Model.Appearance = new NodeAppearance { HeaderColor = "#111111" };

        viewModel.TypeHeaderColor = "#ABCDEF";

        Assert.IsNull(node.Model.Appearance);
    }

    [TestMethod]
    public void AssignmentOptions_AreTwoStateForOneNodeAndThreeStateForMultipleNodes()
    {
        var viewModel = new MainPageViewModel();
        var first = viewModel.AddNode(NodeType.Scene, 0, 0);
        var second = viewModel.AddNode(NodeType.Scene, 300, 0);
        viewModel.AddTag("Urgent", "#E5484D");
        viewModel.AddCharacter("Ari");

        viewModel.SelectNode(first);
        Assert.IsFalse(viewModel.Tags.Single().IsThreeState);
        Assert.IsFalse(viewModel.Characters.Single().IsThreeState);
        Assert.IsNotNull(viewModel.Tags.Single().IsChecked);
        Assert.IsNotNull(viewModel.Characters.Single().IsChecked);

        viewModel.SelectNodes([first, second], isAdditive: false);
        Assert.IsTrue(viewModel.Tags.Single().IsThreeState);
        Assert.IsTrue(viewModel.Characters.Single().IsThreeState);
    }

    [TestMethod]
    public void CharacterAssignmentGroups_RepeatMultiGroupCharactersAndIncludeUngrouped()
    {
        var viewModel = new MainPageViewModel();
        viewModel.AddCharacter("Ari");
        var ari = viewModel.SelectedCharacter!;
        viewModel.AddGroup("Leads");
        viewModel.Groups.Single().IsChecked = true;
        viewModel.AddGroup("Investigators");
        viewModel.Groups.Single(group => group.Name == "Investigators").IsChecked = true;
        viewModel.AddCharacter("Bo");

        var sections = viewModel.CharacterAssignmentGroups;
        Assert.AreEqual(3, sections.Count);
        CollectionAssert.AreEqual(
            new[] { "Leads", "Investigators" },
            sections.Take(2).Select(section => section.Name).ToArray());
        Assert.IsTrue(sections[0].Characters.Any(character => character.Id == ari.Id));
        Assert.IsTrue(sections[1].Characters.Any(character => character.Id == ari.Id));
        Assert.AreEqual("NoGroup", sections[2].Id);
        Assert.AreEqual("Bo", sections[2].Characters.Single().Name);
    }

    [TestMethod]
    public void CharacterAssignmentGroups_RefreshWhenCharacterMembershipChanges()
    {
        var viewModel = new MainPageViewModel();
        viewModel.AddCharacter("Ari");
        viewModel.AddGroup("Leads");

        viewModel.Groups.Single().IsChecked = true;

        var assignedGroup = viewModel.CharacterAssignmentGroups.Single();
        Assert.AreEqual("Leads", assignedGroup.Name);
        Assert.AreEqual("Ari", assignedGroup.Characters.Single().Name);

        viewModel.DeleteGroup(viewModel.Groups.Single());

        var ungrouped = viewModel.CharacterAssignmentGroups.Single();
        Assert.AreEqual("NoGroup", ungrouped.Id);
        Assert.AreEqual("Ari", ungrouped.Characters.Single().Name);
    }

    [TestMethod]
    public void UndoRedo_OneStepRestoresCompleteEdgeStateAndWrapperEndpoints()
    {
        var viewModel = new MainPageViewModel();
        var first = viewModel.AddNode(NodeType.Scene, 0, 0);
        var second = viewModel.AddNode(NodeType.Scene, 300, 0);
        var firstId = first.Model.Id;
        var secondId = second.Model.Id;
        var edge = viewModel.AddEdge(first, second, EdgeSide.Right, EdgeSide.Left);
        Assert.IsNotNull(edge);
        var edgeId = edge.Model.Id;

        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(2, viewModel.Nodes.Count);
        Assert.AreEqual(0, viewModel.Edges.Count);
        CollectionAssert.AreEquivalent(
            new[] { firstId, secondId },
            viewModel.Nodes.Select(node => node.Model.Id).ToArray());

        viewModel.RedoCommand.Execute(null);

        Assert.AreEqual(2, viewModel.Nodes.Count);
        Assert.AreEqual(1, viewModel.Edges.Count);
        var restored = viewModel.Edges.Single();
        Assert.AreEqual(edgeId, restored.Model.Id);
        Assert.AreEqual(firstId, restored.From.Model.Id);
        Assert.AreEqual(secondId, restored.To.Model.Id);
        Assert.IsTrue(viewModel.Nodes.Contains(restored.From));
        Assert.IsTrue(viewModel.Nodes.Contains(restored.To));
    }

    [TestMethod]
    public void TagRemovalUndoRedo_RebuildsAllResolvedTagColorsWithoutStaleEntries()
    {
        var viewModel = new MainPageViewModel();
        var node = viewModel.AddNode(NodeType.Scene, 0, 0);
        viewModel.AddTag("Red", "#FF0000");
        viewModel.AddTag("Blue", "#0000FF");
        CollectionAssert.AreEqual(new[] { "#FF0000", "#0000FF" }, node.TagColors.ToArray());

        viewModel.Tags.Single(tag => tag.Name == "Red").IsChecked = false;
        CollectionAssert.AreEqual(new[] { "#0000FF" }, node.TagColors.ToArray());

        viewModel.UndoCommand.Execute(null);
        var restored = viewModel.Nodes.Single();
        CollectionAssert.AreEqual(new[] { "#FF0000", "#0000FF" }, restored.TagColors.ToArray());

        viewModel.RedoCommand.Execute(null);
        restored = viewModel.Nodes.Single();
        CollectionAssert.AreEqual(new[] { "#0000FF" }, restored.TagColors.ToArray());

        viewModel.ClearSelectedTagsCommand.Execute(null);
        Assert.IsEmpty(restored.TagColors);
    }
}
