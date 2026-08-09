using System.Text.Json;
using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

/// <summary>
/// Regression coverage for the .plotree file format: version 1 documents (including the
/// retired "start" node type and the "branch" spelling) must migrate without data loss,
/// and current-version documents must round-trip unchanged.
/// </summary>
[TestClass]
public sealed class ProjectSerializerTests
{
    private const string V1Json = """
    {
      "version": 1,
      "title": "Legacy story",
      "layoutDirection": "topToBottom",
      "nodes": [
        {
          "id": "n1",
          "type": "start",
          "title": "Opening",
          "body": "It begins.",
          "memo": "Author note",
          "colorTag": "Red",
          "characterIds": [ "c1" ],
          "x": 12.5,
          "y": -30,
          "isPinned": true
        },
        { "id": "n2", "type": "branch", "title": "Fork", "body": "", "memo": "" },
        { "id": "n3", "type": "scene", "title": "Middle" },
        { "id": "n4", "type": "ending", "title": "The End" },
        { "id": "n5", "type": "somethingUnknown", "title": "Odd" }
      ],
      "edges": [
        { "id": "e1", "fromId": "n1", "toId": "n2", "label": "Go on" },
        { "id": "e2", "fromId": "n2", "toId": "n3" },
        { "id": "e3", "fromId": "n3", "toId": "n4" }
      ],
      "characters": [ { "id": "c1", "name": "Alice", "color": "#FF8800", "note": "Lead" } ],
      "tags": [ { "name": "Red", "color": "#FF0000" } ]
    }
    """;

    [TestMethod]
    public void Deserialize_V1_UpgradesVersionAndCreatesAppearanceDefaults()
    {
        var project = ProjectSerializer.Deserialize(V1Json);

        Assert.AreEqual(ProjectSerializer.CurrentVersion, project.Version);
        Assert.IsNotNull(project.Appearance);
        Assert.IsNotNull(project.Appearance.Scene);
        Assert.IsNotNull(project.Appearance.Choice);
        Assert.IsNotNull(project.Appearance.Ending);
        Assert.IsTrue(project.Appearance.Scene.IsEmpty);
        Assert.IsTrue(project.Appearance.Choice.IsEmpty);
        Assert.IsTrue(project.Appearance.Ending.IsEmpty);
    }

    [TestMethod]
    public void Deserialize_V1_MapsStartToSceneAndBranchToChoice()
    {
        var project = ProjectSerializer.Deserialize(V1Json);

        Assert.AreEqual(NodeType.Scene, project.Nodes[0].Type, "v1 'start' must become a Scene.");
        Assert.AreEqual(NodeType.Choice, project.Nodes[1].Type, "v1 'branch' must become a Choice.");
        Assert.AreEqual(NodeType.Scene, project.Nodes[2].Type);
        Assert.AreEqual(NodeType.Ending, project.Nodes[3].Type);
        Assert.AreEqual(NodeType.Scene, project.Nodes[4].Type, "Unknown types must degrade to Scene, not fail the load.");
    }

    [TestMethod]
    public void Deserialize_V1_KeepsNodeDataEdgesCharactersAndTags()
    {
        var project = ProjectSerializer.Deserialize(V1Json);

        Assert.AreEqual("Legacy story", project.Title);
        Assert.AreEqual(LayoutDirection.TopToBottom, project.LayoutDirection);

        var first = project.Nodes[0];
        Assert.AreEqual("Opening", first.Title);
        Assert.AreEqual("It begins.", first.Body);
        Assert.AreEqual("Author note", first.Memo);
        Assert.AreEqual("Red", first.ColorTag);
        CollectionAssert.AreEqual(new[] { "Red" }, first.TagNames);
        Assert.IsNull(first.LegacyColorTag, "The legacy scalar member must be consumed by migration.");
        Assert.AreEqual(1, first.CharacterIds.Count);
        Assert.AreEqual("c1", first.CharacterIds[0]);
        Assert.AreEqual(12.5, first.X);
        Assert.AreEqual(-30d, first.Y);
        Assert.IsTrue(first.IsPinned);
        Assert.IsNull(first.Appearance, "v1 nodes carry no appearance; it must stay null and fall back.");

        Assert.AreEqual(3, project.Edges.Count);
        Assert.AreEqual("n1", project.Edges[0].FromId);
        Assert.AreEqual("n2", project.Edges[0].ToId);
        Assert.AreEqual("Go on", project.Edges[0].Label);
        Assert.IsNull(project.Edges[0].FromSide, "v1 edges carry no port side; it must stay null.");
        Assert.IsNull(project.Edges[0].ToSide);

        Assert.AreEqual(1, project.Characters.Count);
        Assert.AreEqual("Alice", project.Characters[0].Name);
        Assert.AreEqual(1, project.Tags.Count);
        Assert.AreEqual("Red", project.Tags[0].Name);
        Assert.AreEqual("#FF0000", project.Tags[0].Color);
    }

    [TestMethod]
    public void Deserialize_V1_AcceptsLegacyNumericNodeTypes()
    {
        // v1 enum ordinals were Start = 0, Scene = 1, Branch = 2, Ending = 3.
        const string json = """
        {
          "version": 1,
          "nodes": [
            { "id": "a", "type": 0 },
            { "id": "b", "type": 1 },
            { "id": "c", "type": 2 },
            { "id": "d", "type": 3 }
          ]
        }
        """;

        var project = ProjectSerializer.Deserialize(json);

        Assert.AreEqual(NodeType.Scene, project.Nodes[0].Type);
        Assert.AreEqual(NodeType.Scene, project.Nodes[1].Type);
        Assert.AreEqual(NodeType.Choice, project.Nodes[2].Type);
        Assert.AreEqual(NodeType.Ending, project.Nodes[3].Type);
    }

    [TestMethod]
    public void Serialize_WritesCurrentVersionAndV1CompatibleNodeTypeNames()
    {
        var project = TestGraph.Project(
            [
                TestGraph.Node("a"),
                TestGraph.Node("b", NodeType.Choice),
                TestGraph.Node("c", NodeType.Ending),
            ],
            "a>b",
            "b>c");
        project.Version = 1;

        var json = ProjectSerializer.Serialize(project);

        Assert.AreEqual(ProjectSerializer.CurrentVersion, project.Version, "Serialize must stamp the current version.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.AreEqual(ProjectSerializer.CurrentVersion, root.GetProperty("version").GetInt32());

        var types = root.GetProperty("nodes").EnumerateArray()
            .Select(node => node.GetProperty("type").GetString())
            .ToList();
        CollectionAssert.AreEqual(new[] { "scene", "branch", "ending" }, types, "Choice must persist as the v1 'branch' wire name.");
    }

    [TestMethod]
    public void Serialize_OmitsUnsetAppearanceMembers()
    {
        var project = TestGraph.Project([TestGraph.Node("a")]);

        var json = ProjectSerializer.Serialize(project);

        using var document = JsonDocument.Parse(json);
        var node = document.RootElement.GetProperty("nodes")[0];
        Assert.IsFalse(node.TryGetProperty("appearance", out _), "Null node appearance must not be written.");

        var scene = document.RootElement.GetProperty("appearance").GetProperty("scene");
        Assert.IsFalse(scene.TryGetProperty("headerColor", out _));
        Assert.IsFalse(scene.TryGetProperty("width", out _));
    }

    [TestMethod]
    public void RoundTrip_V3_PreservesAppearanceDisplayModePortSidesGeometryAndMetadata()
    {
        var project = new PlotProject
        {
            Title = "Round trip",
            LayoutDirection = LayoutDirection.TopToBottom,
            Appearance = new AppearanceDefaults
            {
                Scene = new NodeAppearance { HeaderColor = "#FF112233", Width = 240, Height = 120 },
                Choice = new NodeAppearance { DisplayMode = NodeDisplayMode.Compact },
                Ending = new NodeAppearance { HeaderColor = "#445566" },
            },
        };
        project.Nodes.Add(new PlotNode
        {
            Id = "a",
            Type = NodeType.Scene,
            Title = "Alpha",
            Body = "Body text",
            Memo = "Memo text",
            TagNames = ["Red", "Urgent"],
            CharacterIds = ["c1"],
            X = 11.25,
            Y = -22.5,
            IsPinned = true,
            Appearance = new NodeAppearance
            {
                HeaderColor = "#AABBCCDD",
                Width = 321,
                Height = 123,
                DisplayMode = NodeDisplayMode.TitleOnly,
            },
        });
        project.Nodes.Add(TestGraph.Node("b", NodeType.Choice));
        project.Nodes.Add(TestGraph.Node("c", NodeType.Ending));
        project.Edges.Add(new PlotEdge
        {
            Id = "e1",
            FromId = "a",
            ToId = "b",
            Label = "Choose",
            FromSide = EdgeSide.Bottom,
            ToSide = EdgeSide.Top,
        });
        project.Edges.Add(TestGraph.Edge("b", "c"));
        project.Groups.Add(new CharacterGroup { Id = "g1", Name = "Investigators" });
        project.Groups.Add(new CharacterGroup { Id = "g2", Name = "Family" });
        project.Characters.Add(new Character
        {
            Id = "c1",
            Name = "Alice",
            Color = "#FF8800",
            Note = "Lead",
            GroupIds = ["g1", "g2"],
        });
        project.Tags.Add(new ColorTag { Name = "Red", Color = "#FF0000" });
        project.Tags.Add(new ColorTag { Name = "Urgent", Color = "#E5484D" });

        var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));

        Assert.AreEqual(ProjectSerializer.CurrentVersion, restored.Version);
        Assert.AreEqual(project.Title, restored.Title);
        Assert.AreEqual(LayoutDirection.TopToBottom, restored.LayoutDirection);

        var a = restored.Nodes[0];
        Assert.AreEqual(NodeType.Scene, a.Type);
        Assert.AreEqual("Body text", a.Body);
        Assert.AreEqual("Memo text", a.Memo);
        CollectionAssert.AreEqual(new[] { "Red", "Urgent" }, a.TagNames);
        Assert.AreEqual("Red", a.ColorTag, "The compatibility property exposes the first assigned tag.");
        Assert.IsNull(a.LegacyColorTag);
        Assert.AreEqual(11.25, a.X);
        Assert.AreEqual(-22.5, a.Y);
        Assert.IsTrue(a.IsPinned);
        Assert.IsNotNull(a.Appearance);
        Assert.AreEqual("#AABBCCDD", a.Appearance.HeaderColor);
        Assert.AreEqual(321d, a.Appearance.Width);
        Assert.AreEqual(123d, a.Appearance.Height);
        Assert.AreEqual(NodeDisplayMode.TitleOnly, a.Appearance.DisplayMode);
        Assert.AreEqual(NodeType.Choice, restored.Nodes[1].Type);
        Assert.AreEqual(NodeType.Ending, restored.Nodes[2].Type);

        Assert.AreEqual(EdgeSide.Bottom, restored.Edges[0].FromSide);
        Assert.AreEqual(EdgeSide.Top, restored.Edges[0].ToSide);
        Assert.AreEqual("Choose", restored.Edges[0].Label);
        Assert.IsNull(restored.Edges[1].FromSide);

        Assert.IsNotNull(restored.Appearance);
        Assert.AreEqual("#FF112233", restored.Appearance.Scene.HeaderColor);
        Assert.AreEqual(240d, restored.Appearance.Scene.Width);
        Assert.AreEqual(NodeDisplayMode.Compact, restored.Appearance.Choice.DisplayMode);
        Assert.AreEqual("#445566", restored.Appearance.Ending.HeaderColor);

        Assert.AreEqual("Alice", restored.Characters[0].Name);
        CollectionAssert.AreEqual(new[] { "g1", "g2" }, restored.Characters[0].GroupIds);
        Assert.AreEqual(2, restored.Groups.Count);
        Assert.AreEqual("Investigators", restored.Groups[0].Name);
        Assert.AreEqual("Family", restored.Groups[1].Name);
        Assert.AreEqual("#FF0000", restored.Tags[0].Color);

        using var document = JsonDocument.Parse(ProjectSerializer.Serialize(restored));
        var serializedNode = document.RootElement.GetProperty("nodes")[0];
        Assert.IsFalse(serializedNode.TryGetProperty("colorTag", out _), "Version 3 must not write the retired scalar tag.");
        CollectionAssert.AreEqual(
            new[] { "Red", "Urgent" },
            serializedNode.GetProperty("tagNames").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [TestMethod]
    public void RoundTrip_V2_IsStableAcrossASecondPass()
    {
        var project = ProjectSerializer.Deserialize(V1Json);

        var first = ProjectSerializer.Serialize(project);
        var second = ProjectSerializer.Serialize(ProjectSerializer.Deserialize(first));

        Assert.AreEqual(first, second, "Re-saving a migrated document must be byte-stable.");
    }

    [TestMethod]
    public void Deserialize_NewerVersion_Throws()
    {
        var json = $$"""{ "version": {{ProjectSerializer.CurrentVersion + 1}}, "nodes": [] }""";

        Assert.ThrowsExactly<InvalidDataException>(() => ProjectSerializer.Deserialize(json));
    }

    [TestMethod]
    public void Deserialize_JsonNull_Throws()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ProjectSerializer.Deserialize("null"));
    }

    [TestMethod]
    public void Deserialize_MigratedV1_LayoutAndRoutesStillWork()
    {
        var project = ProjectSerializer.Deserialize(V1Json);

        AutoLayoutService.Apply(project);
        var analysis = RouteEnumerator.Analyze(project);

        Assert.AreEqual(2, analysis.Roots.Count, "n1 and the disconnected n5 both have indegree zero.");
        Assert.AreEqual("n1", analysis.Roots[0].Id);
        Assert.AreEqual("n5", analysis.Roots[1].Id);
        CollectionAssert.AreEqual(new[] { "n1>n2>n3>n4" }, TestGraph.Paths(analysis));
        Assert.IsTrue(project.Nodes.All(n => double.IsFinite(n.X) && double.IsFinite(n.Y)));
    }

    [TestMethod]
    public void Deserialize_V2_MigratesScalarColorTagToTagNamesAndDoesNotWriteItBack()
    {
        const string json = """
        {
          "version": 2,
          "nodes": [
            { "id": "n1", "colorTag": "Red", "characterIds": [] }
          ],
          "characters": [],
          "tags": [{ "name": "Red", "color": "#FF0000" }]
        }
        """;

        var project = ProjectSerializer.Deserialize(json);

        Assert.AreEqual(ProjectSerializer.CurrentVersion, project.Version);
        CollectionAssert.AreEqual(new[] { "Red" }, project.Nodes[0].TagNames);
        Assert.AreEqual("Red", project.Nodes[0].ColorTag);
        Assert.IsNull(project.Nodes[0].LegacyColorTag);
        Assert.AreEqual(0, project.Groups.Count, "Version 2 files have no groups.");

        using var document = JsonDocument.Parse(ProjectSerializer.Serialize(project));
        var node = document.RootElement.GetProperty("nodes")[0];
        Assert.IsFalse(node.TryGetProperty("colorTag", out _));
        CollectionAssert.AreEqual(
            new[] { "Red" },
            node.GetProperty("tagNames").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [TestMethod]
    public void Deserialize_V3_NormalizesNullCollectionsAndConsumesUnexpectedLegacyColorTag()
    {
        const string json = """
        {
          "version": 3,
          "nodes": [
            {
              "id": "n1",
              "colorTag": "Legacy",
              "tagNames": null,
              "characterIds": null
            }
          ],
          "edges": null,
          "characters": [
            { "id": "c1", "name": "Alice", "groupIds": null }
          ],
          "groups": null,
          "tags": null
        }
        """;

        var project = ProjectSerializer.Deserialize(json);

        Assert.AreEqual(ProjectSerializer.CurrentVersion, project.Version);
        CollectionAssert.AreEqual(new[] { "Legacy" }, project.Nodes[0].TagNames);
        Assert.AreEqual(0, project.Nodes[0].CharacterIds.Count);
        Assert.AreEqual(0, project.Edges.Count);
        Assert.AreEqual(0, project.Groups.Count);
        Assert.AreEqual(0, project.Tags.Count);
        Assert.AreEqual(0, project.Characters[0].GroupIds.Count);

        using var document = JsonDocument.Parse(ProjectSerializer.Serialize(project));
        var node = document.RootElement.GetProperty("nodes")[0];
        Assert.IsFalse(node.TryGetProperty("colorTag", out _));
        Assert.IsTrue(node.TryGetProperty("tagNames", out _));
    }

    [TestMethod]
    public void Serialize_ConsumesLegacyColorTagFromInMemoryProject()
    {
        var project = TestGraph.Project([TestGraph.Node("n1")]);
        project.Nodes[0].LegacyColorTag = "Red";

        using var document = JsonDocument.Parse(ProjectSerializer.Serialize(project));
        var node = document.RootElement.GetProperty("nodes")[0];

        Assert.IsFalse(node.TryGetProperty("colorTag", out _));
        CollectionAssert.AreEqual(
            new[] { "Red" },
            node.GetProperty("tagNames").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.IsNull(project.Nodes[0].LegacyColorTag);
        CollectionAssert.AreEqual(new[] { "Red" }, project.Nodes[0].TagNames);
    }
}
