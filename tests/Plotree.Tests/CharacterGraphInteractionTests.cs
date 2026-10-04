using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class CharacterGraphInteractionTests
{
    [TestMethod]
    [DataRow(0.25)]
    [DataRow(1d)]
    [DataRow(4d)]
    public void LineHitTolerance_IsSixteenScreenPixelsAtEveryZoom(double zoom)
    {
        var geometry = CharacterGraphInteraction.Calculate(0, 0, 1000, 0, zoom);
        Assert.AreEqual(16d, geometry.HitThickness * zoom);
        Assert.IsTrue(CharacterGraphInteraction.ContainsHit(geometry, 500, 7 / zoom));
        Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 500, 9 / zoom));
        Assert.AreEqual(CharacterGraphInteraction.VisibleLineInset, geometry.Visible.X1);
        Assert.AreEqual(1000 - CharacterGraphInteraction.VisibleLineInset, geometry.Visible.X2);
    }

    [TestMethod]
    public void LineHitTolerance_FollowsDiagonalSegmentRatherThanItsBoundingBox()
    {
        var geometry = CharacterGraphInteraction.Calculate(0, 0, 600, 600, 1);
        Assert.IsTrue(CharacterGraphInteraction.ContainsHit(geometry, 304, 296));
        Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 400, 200));
    }

    [TestMethod]
    [DataRow(0.25)]
    [DataRow(1d)]
    [DataRow(4d)]
    public void TopmostHitTargets_LeaveAvatarsAndConnectorButtonsSelectable(double zoom)
    {
        var geometry = CharacterGraphInteraction.Calculate(0, 0, 1000, 0, zoom);
        Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 0, 0));
        Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 34, 0));
        Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 52, 12));
        Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 1000, 0));
        Assert.IsTrue(CharacterGraphInteraction.ContainsHit(geometry, 500, 0));
    }

    [TestMethod]
    public void CoincidentAndVeryCloseCharacters_DoNotCreateHitTargetsOnTopOfTheirControls()
    {
        foreach (var distance in new[] { 0d, 60d, 120d })
        {
            var geometry = CharacterGraphInteraction.Calculate(10, 20, 10 + distance, 20, 1);
            Assert.IsFalse(geometry.CanHit);
            Assert.IsFalse(CharacterGraphInteraction.ContainsHit(geometry, 10, 20));
            Assert.IsTrue(double.IsFinite(geometry.Visible.X1));
            Assert.IsTrue(double.IsFinite(geometry.HitTarget.X2));
        }
    }

    [TestMethod]
    public void RenderingLayers_KeepVisibleLinesAndTheirHitTargetsAboveEveryGroup()
    {
        var layers = new[] { CharacterGraphInteraction.BackgroundLayer, CharacterGraphInteraction.GroupNameLayer,
            CharacterGraphInteraction.CharacterLayer, CharacterGraphInteraction.RelationshipLayer,
            CharacterGraphInteraction.RelationshipHitLayer, CharacterGraphInteraction.RelationshipLabelLayer };
        CollectionAssert.AreEqual(layers.Order().ToArray(), layers);
        Assert.HasCount(layers.Length, layers.Distinct().ToArray());
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(6)]
    public void EveryOverlappingGroup_HasSeparateNameAndSelectedFrontmostNameIsLowest(int count)
    {
        var project = new PlotProject
        {
            Groups = Enumerable.Range(0, count).Select(index => new CharacterGroup { Id = $"group{index}" }).ToList(),
        };
        project.Characters.Add(new Character { Id = "shared", GraphX = 30, GraphY = 50,
            GroupIds = project.Groups.Select(group => group.Id).ToList() });
        foreach (var selected in project.Groups.Select(group => group.Id))
        {
            var regions = CharacterGraphLayout.Calculate(project, selected);
            var labels = CharacterGraphLayout.CalculateLabels(regions);
            Assert.HasCount(count, labels);
            Assert.HasCount(count, labels.Select(label => label.GroupId).Distinct().ToArray());
            Assert.AreEqual(selected, labels.Last().GroupId);
            Assert.AreEqual(labels.Max(label => label.Y), labels.Last().Y);
            for (var a = 0; a < labels.Count; a++)
            {
                var label = labels[a];
                Assert.IsLessThanOrEqualTo(50d, label.Y + label.Height, "Names must stay above the character.");
                for (var b = a + 1; b < labels.Count; b++)
                    Assert.IsFalse(Overlap(label, labels[b]), "All group-name buttons must remain separately clickable.");
            }
        }
        Assert.AreEqual(30d, project.Characters.Single().GraphX);
        Assert.AreEqual(50d, project.Characters.Single().GraphY);
    }

    [TestMethod]
    public void HiddenGroupsHaveNoNameTarget_AndDistantMembersRetainSeparateTargets()
    {
        var project = new PlotProject
        {
            Groups = [new CharacterGroup { Id = "shown" }, new CharacterGroup { Id = "hidden", IsVisible = false }],
            Characters = [new Character { Id = "a", GraphX = 0, GraphY = 0, GroupIds = ["shown", "hidden"] },
                new Character { Id = "b", GraphX = 2000, GraphY = 0, GroupIds = ["shown", "hidden"] }],
        };
        var regions = CharacterGraphLayout.Calculate(project, null);
        var labels = CharacterGraphLayout.CalculateLabels(regions);
        Assert.HasCount(2, labels);
        Assert.IsTrue(labels.All(label => label.GroupId == "shown"));
        Assert.IsFalse(Overlap(labels[0], labels[1]));
    }

    [TestMethod]
    public void GroupHeadersAreIncludedInCanvasExtent_WhileViewportAnchorRemainsFixed()
    {
        var project = new PlotProject
        {
            Groups = Enumerable.Range(0, 6).Select(index => new CharacterGroup { Id = $"g{index}" }).ToList(),
        };
        project.Characters.Add(new Character { GraphX = 0, GraphY = 0, GroupIds = project.Groups.Select(group => group.Id).ToList() });
        var regions = CharacterGraphLayout.Calculate(project, "g0");
        var extent = CanvasExtentCalculator.Calculate(regions.Select(region => new CanvasNodeBounds(region.X,
            region.Y, region.Width, region.Height)), 640, 480, 1, 120, 60, 55, 320, 240);
        foreach (var label in CharacterGraphLayout.CalculateLabels(regions))
        {
            Assert.IsGreaterThanOrEqualTo(0d, label.X + extent.OriginX);
            Assert.IsGreaterThanOrEqualTo(0d, label.Y + extent.OriginY);
            Assert.IsLessThanOrEqualTo(extent.Height, label.Y + label.Height + extent.OriginY);
        }
        var offsetY = 55 + extent.OriginY - 240;
        Assert.IsGreaterThanOrEqualTo(0d, offsetY);
        Assert.IsLessThanOrEqualTo(extent.Height - 480, offsetY, "The viewport anchor must be restorable without clamping.");
    }

    [TestMethod]
    public void ManyDistantGroups_DoNotReserveHeaderSpaceForAllOtherGroups()
    {
        var project = new PlotProject();
        for (var index = 0; index < 12; index++)
        {
            var group = new CharacterGroup { Id = $"g{index}" };
            project.Groups.Add(group);
            project.Characters.Add(new Character { Id = $"c{index}", GraphX = index * 1000, GraphY = 0, GroupIds = [group.Id] });
        }
        var regions = CharacterGraphLayout.Calculate(project, null);
        Assert.HasCount(12, regions);
        foreach (var region in regions) Assert.IsLessThanOrEqualTo(300d, region.Height);
        var labels = CharacterGraphLayout.CalculateLabels(regions);
        for (var a = 0; a < labels.Count; a++)
            for (var b = a + 1; b < labels.Count; b++) Assert.IsFalse(Overlap(labels[a], labels[b]));
    }

    [TestMethod]
    [DataRow(0.25)]
    [DataRow(1d)]
    [DataRow(4d)]
    public void CompleteSelectionRing_FitsItsOwnContainerAndCanvasAtEveryZoom(double zoom)
    {
        foreach (var (x, y) in new[] { (-600d, -400d), (0d, 0d), (900d, 700d) })
        {
            var node = CharacterGraphInteraction.CalculateCharacterBounds(x, y);
            var ring = CharacterGraphInteraction.CalculateSelectionRingBounds(x, y);
            var extent = CanvasExtentCalculator.Calculate([node], 640, 480, zoom, 120, x + 60, y + 34, 320, 240);
            Assert.IsTrue(Contains(node, ring), "No part of the thick ring may overflow its layout container.");
            Assert.AreEqual(x + 60, ring.X + ring.Width / 2);
            Assert.AreEqual(y + 34, ring.Y + ring.Height / 2, "Avatar and relationship centers must stay fixed.");
            Assert.AreEqual(y, node.Y + CharacterGraphInteraction.SelectionRingInset,
                "The original inner node must retain its Y coordinate.");
            for (var angle = 0; angle < 360; angle += 15)
            {
                var radians = angle * Math.PI / 180;
                var px = ring.X + ring.Width / 2 + ring.Width / 2 * Math.Cos(radians);
                var py = ring.Y + ring.Height / 2 + ring.Height / 2 * Math.Sin(radians);
                Assert.IsGreaterThanOrEqualTo(node.X * zoom, px * zoom);
                Assert.IsLessThanOrEqualTo((node.X + node.Width) * zoom, px * zoom);
                Assert.IsGreaterThanOrEqualTo(node.Y * zoom, py * zoom);
                Assert.IsLessThanOrEqualTo((node.Y + node.Height) * zoom, py * zoom);
                Assert.IsGreaterThanOrEqualTo(0d, (px + extent.OriginX) * zoom);
                Assert.IsGreaterThanOrEqualTo(0d, (py + extent.OriginY) * zoom);
                Assert.IsLessThanOrEqualTo(extent.Width * zoom, (px + extent.OriginX) * zoom);
                Assert.IsLessThanOrEqualTo(extent.Height * zoom, (py + extent.OriginY) * zoom);
            }
        }
    }

    [TestMethod]
    public void ForegroundRelationshipStrokes_DoNotPaintOverTheSelectedRing()
    {
        for (var angle = 0; angle < 360; angle += 15)
        {
            var radians = angle * Math.PI / 180;
            var geometry = CharacterGraphInteraction.Calculate(0, 0, 1000 * Math.Cos(radians), 1000 * Math.Sin(radians), 1);
            var startDistance = Math.Sqrt(geometry.Visible.X1 * geometry.Visible.X1 + geometry.Visible.Y1 * geometry.Visible.Y1);
            Assert.IsGreaterThan(CharacterGraphInteraction.SelectionRingDiameter / 2,
                startDistance - 5d / 2, "Even the widest selected line must leave the white ring intact.");
        }
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(6)]
    public void OverlappingBackgroundsAndNames_MoveTogetherInAStaircaseWhenSelectionChanges(int count)
    {
        var project = new PlotProject
        {
            Groups = Enumerable.Range(0, count).Select(index => new CharacterGroup { Id = $"g{index}" }).ToList(),
        };
        project.Characters.Add(new Character { Id = "shared", GraphX = 70, GraphY = -90,
            GroupIds = project.Groups.Select(group => group.Id).ToList() });
        var originalPositions = project.Characters.Select(character => (character.GraphX, character.GraphY)).ToArray();
        var bounds = CharacterGraphLayout.CalculateCanvasBounds(project);
        foreach (var selected in project.Groups.Select(group => (string?)group.Id).Prepend(null))
        {
            var regions = CharacterGraphLayout.Calculate(project, selected);
            var labels = CharacterGraphLayout.CalculateLabels(regions);
            if (selected is not null) Assert.AreEqual(selected, regions.Last().GroupId);
            for (var rank = 0; rank < regions.Count; rank++)
            {
                var region = regions[rank];
                var label = labels[rank];
                Assert.AreEqual(region.GroupId, label.GroupId);
                Assert.AreEqual(region.X + 12, label.X);
                Assert.AreEqual(region.Y + 8, label.Y);
                Assert.IsTrue(Contains(new(region.X, region.Y, region.Width, region.Height),
                    CharacterGraphInteraction.CalculateCharacterBounds(70, -90)));
                Assert.IsTrue(bounds.Any(bound => Contains(bound, new(region.X, region.Y, region.Width, region.Height))));
                if (rank == 0) continue;
                Assert.AreEqual(CharacterGraphLayout.StackStepX, regions[rank - 1].X - region.X);
                Assert.AreEqual(CharacterGraphLayout.StackStepY, region.Y - regions[rank - 1].Y);
                Assert.AreEqual(CharacterGraphLayout.StackStepX, labels[rank - 1].X - label.X);
                Assert.AreEqual(CharacterGraphLayout.StackStepY, label.Y - labels[rank - 1].Y);
                Assert.IsFalse(Overlap(labels[rank - 1], label));
            }
            CollectionAssert.AreEqual(originalPositions,
                project.Characters.Select(character => (character.GraphX, character.GraphY)).ToArray());
        }
        CollectionAssert.AreEqual(bounds.ToArray(), CharacterGraphLayout.CalculateCanvasBounds(project).ToArray());
    }

    [TestMethod]
    public void UnequalAndAdjacentStackRegions_KeepNamesAttachedAndSeparatelySelectable()
    {
        var project = new PlotProject
        {
            Groups = Enumerable.Range(0, 4).Select(index => new CharacterGroup { Id = $"g{index}" }).ToList(),
            Characters = [new Character { GraphX = 0, GraphY = 0, GroupIds = ["g0", "g1", "g2"] },
                new Character { GraphX = 180, GraphY = -20, GroupIds = ["g0"] },
                new Character { GraphX = -100, GraphY = -80, GroupIds = ["g3"] },
                new Character { GraphX = 2000, GraphY = 900, GroupIds = ["g3"] }],
        };
        var bounds = CharacterGraphLayout.CalculateCanvasBounds(project);
        foreach (var selected in project.Groups.Select(group => group.Id))
        {
            var regions = CharacterGraphLayout.Calculate(project, selected);
            var labels = CharacterGraphLayout.CalculateLabels(regions);
            Assert.HasCount(2, regions.Where(region => region.GroupId == "g3").ToArray(),
                "Distant members must retain their separate backgrounds.");
            for (var a = 0; a < labels.Count; a++)
            {
                Assert.AreEqual(regions[a].X + 12, labels[a].X);
                Assert.AreEqual(regions[a].Y + 8, labels[a].Y);
                Assert.IsTrue(bounds.Any(bound => Contains(bound,
                    new(regions[a].X, regions[a].Y, regions[a].Width, regions[a].Height))));
                for (var b = a + 1; b < labels.Count; b++) Assert.IsFalse(Overlap(labels[a], labels[b]));
            }
        }
    }

    private static bool Contains(CanvasNodeBounds outer, CanvasNodeBounds inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static bool Overlap(CharacterGroupLabel a, CharacterGroupLabel b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
}
