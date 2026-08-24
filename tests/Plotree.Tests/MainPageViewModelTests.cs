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

    [TestMethod]
    public void Copy_RequiresNodeSelectionAndDoesNotDirtyProject()
    {
        var viewModel = new MainPageViewModel();

        Assert.IsFalse(viewModel.CanCopy);
        Assert.IsFalse(viewModel.CopyCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CanPaste);
        Assert.IsFalse(viewModel.PasteCommand.CanExecute(null));

        var node = viewModel.AddNode(NodeType.Scene, 10, 20);
        viewModel.SelectNode(node);
        var dirtyBeforeCopy = viewModel.IsDirty;
        viewModel.CopyCommand.Execute(null);

        Assert.IsTrue(viewModel.CanCopy);
        Assert.IsTrue(viewModel.CanPaste);
        Assert.IsTrue(viewModel.PasteCommand.CanExecute(null));
        Assert.AreEqual(dirtyBeforeCopy, viewModel.IsDirty);

        viewModel.ClearSelection();
        Assert.IsFalse(viewModel.CanCopy);
        Assert.IsTrue(viewModel.CanPaste);
    }

    [TestMethod]
    public void CopyPaste_PreservesSelectedNodesAndOnlyInternalEdgesWithFreshIds()
    {
        var viewModel = new MainPageViewModel();
        var first = viewModel.AddNode(NodeType.Scene, 10, 20);
        var second = viewModel.AddNode(NodeType.Choice, 210, 220);
        var external = viewModel.AddNode(NodeType.Ending, 410, 420);
        first.Model.Body = "body";
        first.Model.Memo = "memo";
        first.Model.TagNames.Add("urgent");
        first.Model.CharacterIds.Add("character-id");
        first.Model.IsPinned = true;
        first.Model.Appearance = new NodeAppearance
        {
            HeaderColor = "#123456",
            Width = 321,
            Height = 123,
            DisplayMode = NodeDisplayMode.Compact,
        };
        second.Model.Title = "choice";

        var internalEdge = viewModel.AddEdge(first, second, EdgeSide.Right, EdgeSide.Left)!;
        var outgoingExternalEdge = viewModel.AddEdge(first, external, EdgeSide.Bottom, EdgeSide.Top)!;
        var incomingExternalEdge = viewModel.AddEdge(external, second, EdgeSide.Left, EdgeSide.Right)!;
        internalEdge.Model.Label = "accept";

        viewModel.SelectNodes([first, second], isAdditive: false);
        viewModel.CopyCommand.Execute(null);
        viewModel.PasteCommand.Execute(null);

        Assert.AreEqual(5, viewModel.Project.Nodes.Count);
        Assert.AreEqual(4, viewModel.Project.Edges.Count);
        Assert.AreNotEqual(first.Model.Id, viewModel.SelectedNodes[0].Model.Id);
        Assert.AreNotEqual(second.Model.Id, viewModel.SelectedNodes[1].Model.Id);
        Assert.IsTrue(viewModel.Project.Edges.Any(edge => edge.Id == internalEdge.Model.Id));
        Assert.IsTrue(viewModel.Project.Edges.Any(edge => edge.Id == outgoingExternalEdge.Model.Id));
        Assert.IsTrue(viewModel.Project.Edges.Any(edge => edge.Id == incomingExternalEdge.Model.Id));

        var pastedFirst = viewModel.SelectedNodes.Single(node => node.Model.Title != "choice").Model;
        var pastedSecond = viewModel.SelectedNodes.Single(node => node.Model.Title == "choice").Model;
        Assert.AreEqual(NodeType.Scene, pastedFirst.Type);
        Assert.AreEqual("body", pastedFirst.Body);
        Assert.AreEqual("memo", pastedFirst.Memo);
        CollectionAssert.AreEqual(new[] { "urgent" }, pastedFirst.TagNames);
        CollectionAssert.AreEqual(new[] { "character-id" }, pastedFirst.CharacterIds);
        Assert.IsTrue(pastedFirst.IsPinned);
        Assert.IsNotNull(pastedFirst.Appearance);
        Assert.AreEqual("#123456", pastedFirst.Appearance!.HeaderColor);
        Assert.AreEqual(321d, pastedFirst.Appearance.Width);
        Assert.AreEqual(123d, pastedFirst.Appearance.Height);
        Assert.AreEqual(NodeDisplayMode.Compact, pastedFirst.Appearance.DisplayMode);
        Assert.AreEqual(34d, pastedFirst.X, 1e-9);
        Assert.AreEqual(44d, pastedFirst.Y, 1e-9);

        var pastedEdge = viewModel.Project.Edges.Single(edge =>
            edge.FromId == pastedFirst.Id && edge.ToId == pastedSecond.Id);
        Assert.AreNotEqual(internalEdge.Model.Id, pastedEdge.Id);
        Assert.AreEqual("accept", pastedEdge.Label);
        Assert.AreEqual(EdgeSide.Right, pastedEdge.FromSide);
        Assert.AreEqual(EdgeSide.Left, pastedEdge.ToSide);
    }

    [TestMethod]
    public void CopyPaste_UsesIndependentDeepSnapshotAndIncreasingOffsets()
    {
        var viewModel = new MainPageViewModel();
        var source = viewModel.AddNode(NodeType.Scene, 100, 200);
        source.Model.Title = "before";
        source.Model.TagNames.Add("before-tag");
        source.Model.Appearance = new NodeAppearance { Width = 200 };
        viewModel.SelectNode(source);
        viewModel.CopyCommand.Execute(null);

        source.Model.Title = "after";
        source.Model.TagNames[0] = "after-tag";
        source.Model.Appearance!.Width = 500;

        viewModel.PasteCommand.Execute(null);
        var firstPaste = viewModel.SelectedNodes.Single().Model;
        Assert.AreEqual("before", firstPaste.Title);
        CollectionAssert.AreEqual(new[] { "before-tag" }, firstPaste.TagNames);
        Assert.AreEqual(200d, firstPaste.Appearance!.Width);
        Assert.AreEqual(124d, firstPaste.X, 1e-9);
        Assert.AreEqual(224d, firstPaste.Y, 1e-9);

        viewModel.PasteCommand.Execute(null);
        var secondPaste = viewModel.SelectedNodes.Single().Model;
        Assert.AreEqual("before", secondPaste.Title);
        Assert.AreEqual(148d, secondPaste.X, 1e-9);
        Assert.AreEqual(248d, secondPaste.Y, 1e-9);
        Assert.AreNotEqual(firstPaste.Id, secondPaste.Id);
    }

    [TestMethod]
    public void Paste_IsOneUndoableMutationAndSelectsAllPastedNodes()
    {
        var viewModel = new MainPageViewModel();
        var first = viewModel.AddNode(NodeType.Scene, 0, 0);
        var second = viewModel.AddNode(NodeType.Ending, 200, 0);
        viewModel.AddEdge(first, second, EdgeSide.Right, EdgeSide.Left);
        viewModel.SelectNodes([first, second], isAdditive: false);
        viewModel.CopyCommand.Execute(null);

        var dirtyBeforePaste = viewModel.IsDirty;
        viewModel.PasteCommand.Execute(null);

        Assert.IsTrue(viewModel.IsDirty);
        Assert.AreEqual(2, viewModel.SelectedNodeCount);
        Assert.IsTrue(viewModel.SelectedNodes.All(node => node.IsSelected));
        Assert.IsTrue(viewModel.SelectedNodes.All(node => !ReferenceEquals(node, first) && !ReferenceEquals(node, second)));

        viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(dirtyBeforePaste, viewModel.IsDirty);
        Assert.AreEqual(2, viewModel.Project.Nodes.Count);
        Assert.AreEqual(1, viewModel.Project.Edges.Count);

        viewModel.RedoCommand.Execute(null);
        Assert.IsTrue(viewModel.IsDirty);
        Assert.AreEqual(4, viewModel.Project.Nodes.Count);
        Assert.AreEqual(2, viewModel.Project.Edges.Count);
    }

    [TestMethod]
    public void Clipboard_SurvivesCreatingAnotherProject()
    {
        var viewModel = new MainPageViewModel();
        var source = viewModel.AddNode(NodeType.Scene, 0, 0);
        viewModel.SelectNode(source);
        viewModel.CopyCommand.Execute(null);

        viewModel.Project = new PlotProject();

        Assert.IsTrue(viewModel.CanPaste);
        viewModel.PasteCommand.Execute(null);
        Assert.AreEqual(1, viewModel.Project.Nodes.Count);
        Assert.AreEqual(source.Model.Title, viewModel.Project.Nodes.Single().Title);
    }

    [TestMethod]
    public void CopyPaste_ImportsReferencedMetadataIntoNewProjectWithRemappedIds()
    {
        var viewModel = new MainPageViewModel();
        var node = viewModel.AddNode(NodeType.Scene, 40, 60);
        var tag = new ColorTag { Name = "Urgent", Color = "#E5484D" };
        var group = new CharacterGroup { Name = "Leads" };
        var character = new Character
        {
            Name = "Ari",
            Color = "#00AAFF",
            Note = "detective",
            GroupIds = [group.Id],
        };
        viewModel.Project.Tags.Add(tag);
        viewModel.Project.Groups.Add(group);
        viewModel.Project.Characters.Add(character);
        node.Model.TagNames.Add(tag.Name);
        node.Model.CharacterIds.Add(character.Id);
        viewModel.SelectNode(node);
        viewModel.CopyCommand.Execute(null);

        viewModel.Project = new PlotProject();
        viewModel.PasteCommand.Execute(null);

        var pasted = viewModel.Project.Nodes.Single();
        var importedTag = viewModel.Project.Tags.Single();
        var importedGroup = viewModel.Project.Groups.Single();
        var importedCharacter = viewModel.Project.Characters.Single();

        Assert.AreEqual("Urgent", importedTag.Name);
        Assert.AreEqual("#E5484D", importedTag.Color);
        Assert.AreEqual("Leads", importedGroup.Name);
        Assert.AreEqual("Ari", importedCharacter.Name);
        Assert.AreEqual("#00AAFF", importedCharacter.Color);
        Assert.AreEqual("detective", importedCharacter.Note);
        Assert.AreNotEqual(character.Id, importedCharacter.Id);
        Assert.AreNotEqual(group.Id, importedGroup.Id);
        CollectionAssert.AreEqual(new[] { importedTag.Name }, pasted.TagNames);
        CollectionAssert.AreEqual(new[] { importedCharacter.Id }, pasted.CharacterIds);
        CollectionAssert.AreEqual(new[] { importedGroup.Id }, importedCharacter.GroupIds);

        viewModel.UndoCommand.Execute(null);
        Assert.IsEmpty(viewModel.Project.Nodes);
        Assert.IsEmpty(viewModel.Project.Tags);
        Assert.IsEmpty(viewModel.Project.Characters);
        Assert.IsEmpty(viewModel.Project.Groups);
    }

    [TestMethod]
    public void CopyPaste_ReconcilesMetadataCollisionsAndDoesNotDuplicateOnRepeat()
    {
        var viewModel = new MainPageViewModel();
        var sourceNode = viewModel.AddNode(NodeType.Scene, 0, 0);
        var sourceTag = new ColorTag { Name = "Urgent", Color = "#E5484D" };
        var sourceGroup = new CharacterGroup { Name = "Leads" };
        var sourceCharacter = new Character
        {
            Name = "Ari",
            Color = "#00AAFF",
            GroupIds = [sourceGroup.Id],
        };
        viewModel.Project.Tags.Add(sourceTag);
        viewModel.Project.Groups.Add(sourceGroup);
        viewModel.Project.Characters.Add(sourceCharacter);
        sourceNode.Model.TagNames.Add(sourceTag.Name);
        sourceNode.Model.CharacterIds.Add(sourceCharacter.Id);
        viewModel.SelectNode(sourceNode);
        viewModel.CopyCommand.Execute(null);

        var destinationTag = new ColorTag { Name = "urgent", Color = "#111111" };
        var destinationGroup = new CharacterGroup { Name = "leads" };
        var destinationCharacter = new Character { Name = "ari", Color = "#222222" };
        viewModel.Project = new PlotProject
        {
            Tags = [destinationTag],
            Groups = [destinationGroup],
            Characters = [destinationCharacter],
        };

        viewModel.PasteCommand.Execute(null);
        viewModel.PasteCommand.Execute(null);

        Assert.AreEqual(1, viewModel.Project.Tags.Count);
        Assert.AreEqual("#111111", viewModel.Project.Tags.Single().Color);
        Assert.AreEqual(1, viewModel.Project.Groups.Count);
        Assert.AreEqual(1, viewModel.Project.Characters.Count);
        Assert.AreEqual("#222222", viewModel.Project.Characters.Single().Color);
        Assert.IsTrue(viewModel.Project.Nodes.All(node =>
            node.TagNames.SequenceEqual(new[] { destinationTag.Name })
            && node.CharacterIds.SequenceEqual(new[] { destinationCharacter.Id })));
        Assert.IsEmpty(viewModel.Project.Characters.Single().GroupIds);
    }
}
