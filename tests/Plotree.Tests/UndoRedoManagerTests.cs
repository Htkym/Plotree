using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class UndoRedoManagerTests
{
    [TestMethod]
    public void UndoRedo_RestoresDiscreteProjectStates()
    {
        var manager = new UndoRedoManager();
        var before = TestGraph.Project([TestGraph.Node("n1", title: "Before")]);
        var after = TestGraph.Project([TestGraph.Node("n1", title: "After")]);

        Assert.IsTrue(manager.RecordChange(before, after));

        var undone = manager.Undo();
        Assert.IsNotNull(undone);
        Assert.AreEqual("Before", undone.Nodes[0].Title);
        Assert.IsTrue(manager.CanRedo);

        var redone = manager.Redo();
        Assert.IsNotNull(redone);
        Assert.AreEqual("After", redone.Nodes[0].Title);
        Assert.IsTrue(manager.CanUndo);
        Assert.IsFalse(manager.CanRedo);
    }

    [TestMethod]
    public void Undo_RestoresDeletedNodesAndEdges()
    {
        var manager = new UndoRedoManager();
        var before = TestGraph.Project(
            [TestGraph.Node("start"), TestGraph.Node("middle"), TestGraph.Node("end")],
            "start>middle",
            "middle>end");
        var after = TestGraph.Project([TestGraph.Node("start"), TestGraph.Node("end")]);

        manager.RecordChange(before, after);

        var restored = manager.Undo();

        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(new[] { "start", "middle", "end" }, restored.Nodes.Select(node => node.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "start->middle", "middle->end" },
            restored.Edges.Select(edge => edge.Id).ToArray());
    }

    [TestMethod]
    public void UndoRedo_PreservesVersion3Metadata()
    {
        var manager = new UndoRedoManager();
        var before = TestGraph.Project([TestGraph.Node("scene")]);
        before.Groups.Add(new CharacterGroup { Id = "group-1", Name = "Leads" });
        before.Characters.Add(new Character
        {
            Id = "character-1",
            Name = "Ari",
            GroupIds = ["group-1"],
        });
        before.Tags.Add(new ColorTag { Name = "Urgent", Color = "#E5484D" });
        before.Nodes[0].TagNames = ["Urgent"];
        before.Nodes[0].Appearance = new NodeAppearance
        {
            HeaderColor = "#112233",
            DisplayMode = NodeDisplayMode.TitleOnly,
        };
        var after = TestGraph.Project([TestGraph.Node("scene", title: "Edited")]);

        manager.RecordChange(before, after);

        var restored = manager.Undo() ?? throw new AssertFailedException("Expected undo to restore a project.");

        Assert.AreEqual(ProjectSerializer.CurrentVersion, restored.Version);
        CollectionAssert.AreEqual(new[] { "Urgent" }, restored.Nodes[0].TagNames);
        var appearance = restored.Nodes[0].Appearance
            ?? throw new AssertFailedException("Expected node appearance metadata to be restored.");
        Assert.AreEqual("#112233", appearance.HeaderColor);
        Assert.AreEqual(NodeDisplayMode.TitleOnly, appearance.DisplayMode);
        Assert.AreEqual("Leads", restored.Groups[0].Name);
        CollectionAssert.AreEqual(new[] { "group-1" }, restored.Characters[0].GroupIds);
    }

    [TestMethod]
    public void Checkpoint_TracksDirtyTransitionsWithoutDiscardingHistory()
    {
        var manager = new UndoRedoManager();
        var initial = TestGraph.Project([TestGraph.Node("n1", title: "Initial")]);
        var changed = TestGraph.Project([TestGraph.Node("n1", title: "Changed")]);

        manager.Reset(initial);
        Assert.IsFalse(manager.IsDirty(initial));

        manager.RecordChange(initial, changed);
        Assert.IsTrue(manager.IsDirty(changed));

        manager.SaveCheckpoint(changed);
        Assert.IsFalse(manager.IsDirty(changed));
        Assert.IsTrue(manager.CanUndo);

        var restored = manager.Undo();
        Assert.IsNotNull(restored);
        Assert.IsTrue(manager.IsDirty(restored));
    }

    [TestMethod]
    public void RecordChange_AfterUndo_ClearsRedoHistory()
    {
        var manager = new UndoRedoManager();
        var initial = TestGraph.Project([TestGraph.Node("n1", title: "Initial")]);
        var first = TestGraph.Project([TestGraph.Node("n1", title: "First")]);
        var replacement = TestGraph.Project([TestGraph.Node("n1", title: "Replacement")]);

        manager.RecordChange(initial, first);
        var undone = manager.Undo();
        Assert.IsNotNull(undone);
        Assert.IsTrue(manager.CanRedo);

        Assert.IsTrue(manager.RecordChange(undone, replacement));

        Assert.IsFalse(manager.CanRedo);
        Assert.AreEqual("Initial", manager.Undo()!.Nodes[0].Title);
        Assert.AreEqual("Replacement", manager.Redo()!.Nodes[0].Title);
    }

    [TestMethod]
    public void HistoryCapacity_DiscardsOldestChanges()
    {
        var manager = new UndoRedoManager(historyCapacity: 2);
        var first = TestGraph.Project([TestGraph.Node("n1", title: "1")]);
        var second = TestGraph.Project([TestGraph.Node("n1", title: "2")]);
        var third = TestGraph.Project([TestGraph.Node("n1", title: "3")]);
        var fourth = TestGraph.Project([TestGraph.Node("n1", title: "4")]);

        manager.RecordChange(first, second);
        manager.RecordChange(second, third);
        manager.RecordChange(third, fourth);

        Assert.AreEqual("3", manager.Undo()!.Nodes[0].Title);
        Assert.AreEqual("2", manager.Undo()!.Nodes[0].Title);
        Assert.IsNull(manager.Undo());
    }

    [TestMethod]
    public void RecordChange_NoOp_DoesNotAddHistoryOrClearRedo()
    {
        var manager = new UndoRedoManager();
        var initial = TestGraph.Project([TestGraph.Node("n1", title: "Initial")]);
        var changed = TestGraph.Project([TestGraph.Node("n1", title: "Changed")]);

        manager.RecordChange(initial, changed);
        var undone = manager.Undo();
        Assert.IsNotNull(undone);
        Assert.IsTrue(manager.CanRedo);

        Assert.IsFalse(manager.RecordChange(undone, undone));

        Assert.IsFalse(manager.CanUndo);
        Assert.IsTrue(manager.CanRedo);
    }
}
