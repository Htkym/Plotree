using Plotree.Models;
using Plotree.Services;
using Plotree.ViewModels;

namespace Plotree.Tests;

[TestClass]
public sealed class CharacterGraphTests
{
    [TestMethod]
    public void GroupingAndRecoloring_PreserveAllPositionsAndExistingRelationship()
    {
        var vm = new MainPageViewModel();
        vm.AddCharacter("A"); vm.AddCharacter("B"); vm.AddCharacter("Outside");
        var characters = vm.Project.Characters;
        vm.MoveCharacter(characters[0].Id, -140, 12);
        vm.MoveCharacter(characters[1].Id, 1700, 800);
        var before = characters.Select(c => (c.Id, c.GraphX, c.GraphY)).ToArray();
        var relation = vm.SaveRelationship(null, characters[0].Id, characters[1].Id, "Friends")!;
        var first = vm.CreateGraphGroup("First", "#336699", [characters[0].Id, characters[1].Id])!;
        var second = vm.CreateGraphGroup("Second", "#339966", [characters[0].Id])!;
        vm.AssignCharactersToGraphGroup([characters[1].Id], second.Id);
        vm.SetGraphGroupBackgroundColor(first.Id, "#123456");
        CollectionAssert.AreEqual(before, characters.Select(c => (c.Id, c.GraphX, c.GraphY)).ToArray());
        Assert.IsTrue(characters[0].GroupIds.Contains(first.Id) && characters[0].GroupIds.Contains(second.Id));
        Assert.AreEqual(relation.Id, vm.Project.Relationships.Single().Id);
        Assert.HasCount(2, CharacterGraphLayout.Calculate(vm.Project, null).Where(region => region.GroupId == first.Id).ToArray());
    }

    [TestMethod]
    public void TreeMembershipAndGraphMembership_UseSameGroupsAndAssignmentOptions()
    {
        var vm = new MainPageViewModel();
        vm.AddCharacter("A"); vm.AddCharacter("B"); vm.AddGroup("Tree group");
        var a = vm.Characters[0];
        vm.SelectedCharacter = a;
        vm.Groups.Single().IsChecked = true;
        var treeId = vm.Project.Groups.Single().Id;
        Assert.HasCount(1, CharacterGraphLayout.Calculate(vm.Project, null));
        Assert.AreEqual(treeId, CharacterGraphLayout.Calculate(vm.Project, null).Single().GroupId);
        var graphGroup = vm.CreateGraphGroup("Graph group", "#336699", [a.Id])!;
        Assert.IsTrue(vm.Groups.Single(group => group.Id == graphGroup.Id).IsChecked);
        Assert.AreSame(vm.Characters.Single(character => character.Id == a.Id),
            vm.CharacterAssignmentGroups.Single(group => group.Id == graphGroup.Id).Characters.Single());
        vm.Groups.Single(group => group.Id == treeId).IsChecked = false;
        Assert.IsFalse(CharacterGraphLayout.Calculate(vm.Project, null).Any(region => region.GroupId == treeId));
        CollectionAssert.Contains(vm.Project.Characters.Single(character => character.Id == a.Id).GroupIds, graphGroup.Id);
    }

    [TestMethod]
    public void OverlappingGroups_AreOffsetInCreationOrderAndSelectedGroupIsLast()
    {
        var project = SharedMemberProject();
        var originalPositions = project.Characters.Select(character => (character.GraphX, character.GraphY)).ToArray();
        var regions = CharacterGraphLayout.Calculate(project, null);
        CollectionAssert.AreEqual(new[] { "first", "second" }, regions.Select(region => region.GroupId).ToArray());
        Assert.AreNotEqual(regions[0].X, regions[1].X);
        Assert.AreNotEqual(regions[0].Y, regions[1].Y);
        Assert.IsTrue(regions[0].X < regions[1].X + regions[1].Width && regions[1].X < regions[0].X + regions[0].Width);
        // Each region exposes an edge while still containing its member.
        foreach (var region in regions)
        {
            Assert.IsTrue(region.X <= 0 && region.Y <= 0);
            Assert.IsTrue(region.X + region.Width >= 120 && region.Y + region.Height >= 110);
        }
        CollectionAssert.AreEqual(new[] { "second", "first" },
            CharacterGraphLayout.Calculate(project, "first").Select(region => region.GroupId).ToArray());
        CollectionAssert.AreEqual(originalPositions, project.Characters.Select(character => (character.GraphX, character.GraphY)).ToArray());
    }

    [TestMethod]
    public void NearbyMembersShareBackground_DistantMembersDoNotEncloseUnrelatedCharacters()
    {
        var project = SharedMemberProject();
        project.Groups.RemoveAt(1);
        project.Characters.Add(new Character { Id = "b", GraphX = 180, GraphY = 40, GroupIds = ["first"] });
        project.Characters.Add(new Character { Id = "c", GraphX = 1500, GraphY = 0, GroupIds = ["first"] });
        project.Characters.Add(new Character { Id = "outside", GraphX = 800, GraphY = 0 });
        var regions = CharacterGraphLayout.Calculate(project, null);
        Assert.HasCount(2, regions);
        Assert.IsTrue(regions.Any(region => region.X <= 0 && region.X + region.Width >= 300));
        Assert.IsFalse(regions.Any(region => region.X < 800 && region.X + region.Width > 800));
    }

    [TestMethod]
    public void SelectedGroup_HideUndoRedoAndDeletionKeepValidSelectionAndMembership()
    {
        var vm = new MainPageViewModel(); vm.AddCharacter("A");
        var group = vm.CreateGraphGroup("Team", "#336699", [vm.Characters.Single().Id])!;
        var before = ProjectSerializer.Serialize(vm.Project);
        Assert.IsTrue(vm.SelectGraphGroup(group.Id));
        Assert.AreEqual(before, ProjectSerializer.Serialize(vm.Project), "Selection must not change persisted state.");
        Assert.IsFalse(vm.SelectGraphGroup("missing"));
        Assert.AreEqual(group.Id, vm.SelectedGraphGroupId);
        Assert.IsTrue(vm.SetGraphGroupVisible(group.Id, false));
        Assert.IsNull(vm.SelectedGraphGroupId);
        Assert.IsFalse(vm.SelectGraphGroup(group.Id));
        Assert.IsEmpty(CharacterGraphLayout.Calculate(vm.Project, null));
        CollectionAssert.Contains(vm.Project.Characters.Single().GroupIds, group.Id);
        Assert.IsFalse(ProjectSerializer.Deserialize(ProjectSerializer.Serialize(vm.Project)).Groups.Single().IsVisible);
        vm.UndoCommand.Execute(null);
        Assert.IsTrue(vm.Project.Groups.Single().IsVisible);
        Assert.HasCount(1, CharacterGraphLayout.Calculate(vm.Project, null));
        vm.RedoCommand.Execute(null);
        Assert.IsFalse(vm.Project.Groups.Single().IsVisible);
        vm.SetGraphGroupVisible(group.Id, true);
        vm.SelectGraphGroup(group.Id);
        vm.DeleteGroup(vm.Groups.Single());
        Assert.IsNull(vm.SelectedGraphGroupId);
        Assert.IsEmpty(vm.Project.Characters.Single().GroupIds);
    }

    [TestMethod]
    public void VisibilityAndLegacyGraphMembership_RoundTripWithoutMovingCharacters()
    {
        var project = ProjectSerializer.Deserialize("""
            {"version":4,"groups":[{"id":"g","name":"Team"}],
             "characters":[{"id":"a","name":"A","graphGroupId":"g","graphX":42,"graphY":81}]}
            """);
        Assert.IsTrue(project.Groups.Single().IsVisible);
        CollectionAssert.AreEqual(new[] { "g" }, project.Characters.Single().GroupIds);
        project.Groups[0].IsVisible = false;
        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.IsFalse(restored.Groups.Single().IsVisible);
        Assert.AreEqual(42d, restored.Characters.Single().GraphX);
        Assert.AreEqual(81d, restored.Characters.Single().GraphY);
    }

    [TestMethod]
    public void TreeUncheckOfLegacyGraphGroup_DoesNotRestoreRemovedMembershipOnSave()
    {
        var vm = new MainPageViewModel(); vm.AddCharacter("A");
        var group = vm.CreateGraphGroup("Team", "#336699", [vm.Characters.Single().Id])!;
        vm.Groups.Single().IsChecked = false;
        Assert.IsEmpty(vm.Project.Characters.Single().GroupIds);
        Assert.IsNull(vm.Project.Characters.Single().GraphGroupId);
        Assert.IsEmpty(ProjectSerializer.Deserialize(ProjectSerializer.Serialize(vm.Project)).Characters.Single().GroupIds);
        vm.UndoCommand.Execute(null);
        CollectionAssert.Contains(vm.Project.Characters.Single().GroupIds, group.Id);
    }

    [TestMethod]
    public void Ungroup_RemovesSharedMembershipsPreservingPositions()
    {
        var vm = new MainPageViewModel(); vm.AddCharacter("A");
        var character = vm.Project.Characters.Single();
        vm.CreateGraphGroup("One", null, [character.Id]); vm.CreateGraphGroup("Two", null, [character.Id]);
        var before = (character.GraphX, character.GraphY);
        vm.AssignCharactersToGraphGroup([character.Id], null);
        Assert.IsEmpty(character.GroupIds); Assert.IsNull(character.GraphGroupId);
        Assert.AreEqual(before, (character.GraphX, character.GraphY));
        Assert.IsEmpty(CharacterGraphLayout.Calculate(vm.Project, null));
        vm.UndoCommand.Execute(null);
        Assert.HasCount(2, vm.Project.Characters.Single().GroupIds);
    }

    [TestMethod]
    public void RelationshipSelection_EmphasizesSelectedLineAndConnectionsOfSelectedCharacters()
    {
        var relationship = new CharacterRelationship { Id = "r", FirstCharacterId = "a", SecondCharacterId = "b" };
        Assert.IsTrue(CharacterGraphLayout.IsRelationshipEmphasized(relationship, "r", new HashSet<string>()));
        Assert.IsTrue(CharacterGraphLayout.IsRelationshipEmphasized(relationship, null, new HashSet<string> { "a" }));
        Assert.IsTrue(CharacterGraphLayout.IsRelationshipEmphasized(relationship, null, new HashSet<string> { "b" }));
        Assert.IsFalse(CharacterGraphLayout.IsRelationshipEmphasized(relationship, "other", new HashSet<string> { "c" }));
    }

    [TestMethod]
    public void NewRelationship_DefaultLabelRoundTripsAndEditingCanKeepLegacyEmptyLabel()
    {
        var vm = new MainPageViewModel(); vm.AddCharacter("A"); vm.AddCharacter("B");
        var relationship = vm.SaveRelationship(null, vm.Characters[0].Id, vm.Characters[1].Id, "   ")!;
        Assert.AreEqual(Loc.Get("Relationship_DefaultLabel"), relationship.Label);
        Assert.AreEqual(relationship.Label, ProjectSerializer.Deserialize(ProjectSerializer.Serialize(vm.Project)).Relationships.Single().Label);
        vm.SaveRelationship(relationship.Id, relationship.FirstCharacterId, relationship.SecondCharacterId, "  ");
        Assert.AreEqual(string.Empty, relationship.Label);
    }

    [TestMethod]
    public void DetailLines_UseOneLinePerCharacterWithAllGroupsAndNoDuplicateOrMissingNames()
    {
        var project = SharedMemberProject(); project.Characters[0].Name = "人物A";
        project.Groups[0].Name = "グループ1"; project.Groups[1].Name = "グループ2";
        project.Characters.Add(new Character { Id = "b", Name = "人物B" });
        Assert.AreEqual("・人物A（グループ1, グループ2）\n・人物B",
            CharacterSummaryFormatter.DetailLines(project, ["a", "missing", "a", "b"]));
        Assert.AreEqual("人物A, 人物B", CharacterSummaryFormatter.CardNames(project, ["a", "b", "a"]));
    }

    [TestMethod]
    public void CardCharacterVisibility_InheritsOverridesClonesAndSerializesIndependentlyOfDisplayMode()
    {
        var project = new PlotProject(); var node = new PlotNode(); project.Nodes.Add(node);
        Assert.IsFalse(AppearanceResolver.Resolve(project, node).ShowCharacters);
        project.Appearance.Scene.ShowCharacters = true;
        node.Appearance = new NodeAppearance { DisplayMode = NodeDisplayMode.TitleOnly };
        Assert.IsTrue(AppearanceResolver.Resolve(project, node).ShowCharacters);
        node.Appearance.ShowCharacters = false;
        Assert.IsFalse(AppearanceResolver.Resolve(project, node).ShowCharacters);
        Assert.IsFalse(node.Appearance.IsEmpty);
        Assert.IsFalse(node.Appearance.Clone().ShowCharacters!.Value);
        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.IsTrue(restored.Appearance.Scene.ShowCharacters!.Value);
        Assert.IsFalse(restored.Nodes.Single().Appearance!.ShowCharacters!.Value);
        Assert.AreEqual(NodeDisplayMode.TitleOnly, AppearanceResolver.Resolve(restored, restored.Nodes.Single()).DisplayMode);
    }

    [TestMethod]
    public void CardCharacterSettings_UndoRedoAndAssignmentNotifyExistingCard()
    {
        var vm = new MainPageViewModel(); var node = vm.AddNode(NodeType.Scene, 0, 0); vm.SelectNode(node);
        vm.AddCharacter("A");
        var notifications = new List<string?>();
        node.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        vm.Characters.Single().IsChecked = true;
        Assert.AreEqual("A", node.CharacterNames);
        CollectionAssert.Contains(notifications, nameof(NodeViewModel.CharacterNames));
        vm.TypeShowCharacters = true;
        Assert.IsTrue(node.EffectiveShowCharacters);
        vm.NodeCharactersDisplayIndex = 2;
        Assert.IsFalse(node.EffectiveShowCharacters);
        Assert.IsFalse(node.Model.Appearance!.IsEmpty);
        vm.UndoCommand.Execute(null);
        Assert.IsTrue(vm.SelectedNode!.EffectiveShowCharacters);
        vm.RedoCommand.Execute(null);
        Assert.IsFalse(vm.SelectedNode!.EffectiveShowCharacters);
        vm.NodeCharactersDisplayIndex = 0;
        Assert.IsTrue(vm.SelectedNode.EffectiveShowCharacters);
    }

    [TestMethod]
    public void CharacterUiResources_ArePresentInBothLanguagesWithExpectedJapaneseDefaultLabel()
    {
        var required = new[] { "Relationship_DefaultLabel", "Graph_GroupVisibility", "NodeCharactersDisplayCombo.Header",
            "NodeCharactersDefault.Content", "NodeCharactersShow.Content", "NodeCharactersHide.Content", "TypeShowCharacters.Content" };
        foreach (var language in new[] { "en-US", "ja-JP" })
        {
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", language + ".Resources.xml"));
            var resources = document.Root!.Elements("data").ToDictionary(element => element.Attribute("name")!.Value,
                element => element.Element("value")!.Value);
            foreach (var key in required)
            {
                Assert.IsTrue(resources.TryGetValue(key, out var value));
                Assert.IsFalse(string.IsNullOrWhiteSpace(value));
            }
            var expectedDefaultLabel = language == "ja-JP" ? "関係名" : "Relationship name";
            Assert.AreEqual(expectedDefaultLabel, resources["Relationship_DefaultLabel"]);
            foreach (var helpKey in new[] { "Relationship_ConnectHandle", "Help_CharacterGraphGestures" })
            {
                Assert.IsTrue(resources[helpKey].Contains(expectedDefaultLabel, StringComparison.Ordinal));
                Assert.IsFalse(resources[helpKey].Contains("unnamed", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(resources[helpKey].Contains("関係名なし", StringComparison.Ordinal));
            }
        }
    }

    private static PlotProject SharedMemberProject() => new()
    {
        Groups = [new CharacterGroup { Id = "first" }, new CharacterGroup { Id = "second" }],
        Characters = [new Character { Id = "a", GraphX = 0, GraphY = 0, GroupIds = ["first", "second"] }],
    };
}
