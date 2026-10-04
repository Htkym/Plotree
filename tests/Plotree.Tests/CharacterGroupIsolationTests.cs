using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class CharacterGroupIsolationTests
{
    [TestMethod]
    [DataRow(-180d, 0d)]
    [DataRow(180d, 0d)]
    [DataRow(0d, -160d)]
    [DataRow(0d, 160d)]
    public void SharedMemberDraggedNearAOnlyMember_DoesNotGrowB(double neighborX, double neighborY)
    {
        var project = Project();
        var shared = new Character { Id = "shared", GraphX = 1000, GraphY = 1000, GroupIds = ["A", "B"] };
        var onlyA = new Character { Id = "onlyA", GraphX = neighborX, GraphY = neighborY, GroupIds = ["A"] };
        project.Characters = [shared, onlyA];
        foreach (var selected in new string?[] { null, "A", "B" })
        {
            shared.GraphX = shared.GraphY = 1000;
            var before = CharacterGraphLayout.Calculate(project, selected).Single(region => region.GroupId == "B");
            shared.GraphX = shared.GraphY = 0;
            var after = CharacterGraphLayout.Calculate(project, selected).Single(region => region.GroupId == "B");
            Assert.AreEqual(before.Width, after.Width);
            Assert.AreEqual(before.Height, after.Height);
            Assert.AreEqual(before.X - 1000, after.X);
            Assert.AreEqual(before.Y - 1000, after.Y);
            Assert.IsFalse(Contains(after, new(neighborX, neighborY, 120, 110)),
                "B must not expand to include the neighboring A-only character.");
            Assert.IsTrue(Contains(after, CharacterGraphInteraction.CalculateCharacterBounds(0, 0)));
        }
    }

    [TestMethod]
    public void MovingOtherGroupsMembersAcrossAChain_DoesNotChangeUnrelatedBGeometry()
    {
        var project = Project();
        project.Groups.Add(new CharacterGroup { Id = "C" });
        project.Groups.Add(new CharacterGroup { Id = "D" });
        project.Characters = [new Character { Id = "AB", GraphX = 0, GraphY = 0, GroupIds = ["A", "B"] },
            new Character { Id = "AC", GraphX = 2000, GraphY = 0, GroupIds = ["A", "C"] },
            new Character { Id = "CD", GraphX = 4000, GraphY = 0, GroupIds = ["C", "D"] }];
        foreach (var selected in new string?[] { null, "A", "B", "C", "D" })
        {
            project.Characters[1].GraphX = 2000; project.Characters[2].GraphX = 4000;
            var before = CharacterGraphLayout.Calculate(project, selected).Single(region => region.GroupId == "B");
            project.Characters[1].GraphX = -180; project.Characters[2].GraphX = -360;
            var regions = CharacterGraphLayout.Calculate(project, selected);
            Assert.AreEqual(before, regions.Single(region => region.GroupId == "B"));
            Assert.IsFalse(Contains(before, new(-180, 0, 120, 110)));
            AssertSeparateNames(CharacterGraphLayout.CalculateLabels(regions));
        }
    }

    [TestMethod]
    public void TwoDistantBIslands_KeepTheirOwnBoundsWhenOtherGroupsJoinNearby()
    {
        var project = Project(); project.Groups.Add(new CharacterGroup { Id = "C" });
        project.Characters = [new Character { GraphX = 0, GraphY = 0, GroupIds = ["A", "B"] },
            new Character { GraphX = 2000, GraphY = 0, GroupIds = ["B", "C"] },
            new Character { GraphX = -1000, GraphY = 0, GroupIds = ["A"] },
            new Character { GraphX = 3000, GraphY = 0, GroupIds = ["C"] }];
        var before = CharacterGraphLayout.Calculate(project, null).Where(region => region.GroupId == "B").ToArray();
        project.Characters[2].GraphX = -180; project.Characters[3].GraphX = 2180;
        var after = CharacterGraphLayout.Calculate(project, null).Where(region => region.GroupId == "B").ToArray();
        Assert.HasCount(2, after);
        CollectionAssert.AreEqual(before, after);
        Assert.IsFalse(after.Any(region => Contains(region, new(900, 0, 120, 110))));
    }

    [TestMethod]
    public void CoincidentIndependentGroups_KeepUsableSeparateNameTargetsWithoutMovingBackgrounds()
    {
        var project = new PlotProject();
        for (var index = 0; index < 8; index++)
        {
            project.Groups.Add(new CharacterGroup { Id = $"g{index}" });
            project.Characters.Add(new Character { GraphX = 0, GraphY = 0, GroupIds = [$"g{index}"] });
        }
        var regions = CharacterGraphLayout.Calculate(project, null);
        var labels = CharacterGraphLayout.CalculateLabels(regions);
        AssertSeparateNames(labels);
        foreach (var label in labels)
        {
            var region = regions[label.RegionIndex];
            Assert.IsGreaterThanOrEqualTo(CharacterGraphLayout.MinimumGroupLabelWidth, label.Width);
            Assert.IsGreaterThanOrEqualTo(region.X + 12, label.X);
            Assert.IsLessThanOrEqualTo(region.X + region.Width - 12, label.X + label.Width);
            Assert.IsLessThanOrEqualTo(region.Y + 8 + CharacterGraphLayout.GroupLabelHeight, label.Y + label.Height);
            Assert.AreEqual(152d, region.Width, "Labels must not enlarge their independent backgrounds.");
            Assert.AreEqual(178d, region.Height);
        }
    }

    private static PlotProject Project() => new()
    {
        Groups = [new CharacterGroup { Id = "A" }, new CharacterGroup { Id = "B" }],
    };

    private static bool Contains(CharacterGroupRegion outer, CanvasNodeBounds inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static void AssertSeparateNames(IReadOnlyList<CharacterGroupLabel> labels)
    {
        for (var a = 0; a < labels.Count; a++)
            for (var b = a + 1; b < labels.Count; b++)
                Assert.IsFalse(labels[a].X < labels[b].X + labels[b].Width
                    && labels[a].X + labels[a].Width > labels[b].X && labels[a].Y < labels[b].Y + labels[b].Height
                    && labels[a].Y + labels[a].Height > labels[b].Y);
    }
}
