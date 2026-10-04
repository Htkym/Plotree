using Plotree.Models;

namespace Plotree.Services;

public readonly record struct CharacterGroupRegion(string GroupId, double X, double Y, double Width, double Height);
public readonly record struct CharacterGroupLabel(string GroupId, int RegionIndex, double X, double Y, double Width, double Height);

/// <summary>Backgrounds and attached names form a stack without moving their member characters.</summary>
public static class CharacterGraphLayout
{
    public const double GroupLabelHeight = 24;
    public const double GroupLabelGap = 4;
    public const double StackStepX = 12;
    public const double StackStepY = GroupLabelHeight + GroupLabelGap;

    public static IReadOnlyList<CharacterGroup> DrawOrder(PlotProject project, string? selectedGroupId) =>
        project.Groups.Where(group => group.IsVisible)
            .OrderBy(group => group.Id == selectedGroupId ? 1 : 0).ToArray();

    public static IReadOnlyList<CharacterGroupRegion> Calculate(PlotProject project, string? selectedGroupId,
        double nodeWidth = 120, double nodeHeight = 110) =>
        CalculateStack(project, selectedGroupId, nodeWidth, nodeHeight).Regions;

    /// <summary>Selection-independent envelopes contain every possible ordering of each stack.</summary>
    public static IReadOnlyList<CanvasNodeBounds> CalculateCanvasBounds(PlotProject project,
        double nodeWidth = 120, double nodeHeight = 110) =>
        CalculateStack(project, null, nodeWidth, nodeHeight).Bounds;

    private static (IReadOnlyList<CharacterGroupRegion> Regions, IReadOnlyList<CanvasNodeBounds> Bounds)
        CalculateStack(PlotProject project, string? selectedGroupId, double nodeWidth, double nodeHeight)
    {
        var regions = new List<CharacterGroupRegion>();
        var bounds = new List<CanvasNodeBounds>();
        var order = DrawOrder(project, selectedGroupId);
        const double padding = 16;
        var headerHeight = GroupLabelHeight + GroupLabelGap + 8;
        foreach (var group in order)
        {
            var remaining = project.Characters.Where(character => character.GroupIds.Contains(group.Id)).ToList();
            while (remaining.Count > 0)
            {
                var members = new List<Character> { remaining[0] };
                remaining.RemoveAt(0);
                for (var i = 0; i < members.Count; i++)
                {
                    var current = members[i];
                    var neighbors = remaining.Where(character =>
                        Math.Abs((character.GraphX ?? 0) - (current.GraphX ?? 0)) <= nodeWidth + 100
                        && Math.Abs((character.GraphY ?? 0) - (current.GraphY ?? 0)) <= nodeHeight + 80).ToArray();
                    foreach (var neighbor in neighbors)
                    {
                        remaining.Remove(neighbor);
                        members.Add(neighbor);
                    }
                }
                var minX = members.Min(character => character.GraphX ?? 0);
                var minY = members.Min(character => character.GraphY ?? 0);
                var maxX = members.Max(character => (character.GraphX ?? 0) + nodeWidth);
                var maxY = members.Max(character => (character.GraphY ?? 0) + nodeHeight);
                var raw = new CharacterGroupRegion(group.Id, minX - padding, minY - padding - headerHeight,
                    maxX - minX + padding * 2, maxY - minY + padding * 2 + headerHeight);
                // Only this region's own members decide its stack reserve. A neighboring
                // group's wider bounds or other members cannot enlarge this background.
                var memberships = members.SelectMany(character => character.GroupIds).ToHashSet(StringComparer.Ordinal);
                var localOrder = order.Where(candidate => memberships.Contains(candidate.Id)).ToArray();
                var rank = Array.FindIndex(localOrder, candidate => candidate.Id == group.Id);
                var horizontalReserve = (localOrder.Length - 1) * StackStepX;
                var verticalReserve = (localOrder.Length - 1) * StackStepY;
                regions.Add(raw with { X = raw.X - rank * StackStepX,
                    Y = raw.Y - verticalReserve + rank * StackStepY,
                    Width = raw.Width + horizontalReserve, Height = raw.Height + verticalReserve });
                bounds.Add(new(raw.X - horizontalReserve, raw.Y - verticalReserve,
                    raw.Width + horizontalReserve * 2, raw.Height + verticalReserve * 2));
            }
        }
        return (regions, bounds.OrderBy(bound => bound.X).ThenBy(bound => bound.Y).ToArray());
    }

    /// <summary>Names follow their own backgrounds. Ellipsized widths leave adjacent names separately selectable.</summary>
    public static IReadOnlyList<CharacterGroupLabel> CalculateLabels(IReadOnlyList<CharacterGroupRegion> regions)
    {
        var labels = new List<CharacterGroupLabel>();
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            var label = new CharacterGroupLabel(region.GroupId, index, region.X + 12, region.Y + 8,
                Math.Min(220, Math.Max(1, region.Width - 24)), GroupLabelHeight);
            for (var previousIndex = 0; previousIndex < labels.Count; previousIndex++)
            {
                var previous = labels[previousIndex];
                if (!Overlaps(label.X, label.Y, label.Width, label.Height,
                    previous.X, previous.Y, previous.Width, previous.Height)) continue;
                // Prefer keeping both top-left anchors. Only identical or very close
                // anchors need the right-hand name moved within its own header.
                if (label.X + GroupLabelGap + 1 <= previous.X)
                    label = label with { Width = previous.X - label.X - GroupLabelGap };
                else if (previous.X + GroupLabelGap + 1 <= label.X)
                    labels[previousIndex] = previous with { Width = label.X - previous.X - GroupLabelGap };
                else
                {
                    var right = label.X + label.Width;
                    var overlapLeft = Math.Max(previous.X, label.X);
                    var overlapRight = Math.Min(previous.X + previous.Width, label.X + label.Width);
                    var gap = Math.Min(GroupLabelGap, (overlapRight - overlapLeft) / 3);
                    var split = (overlapLeft + overlapRight) / 2;
                    labels[previousIndex] = previous with { Width = split - gap / 2 - previous.X };
                    var newX = split + gap / 2;
                    label = label with { X = newX, Width = right - newX };
                }
            }
            labels.Add(label);
        }
        return labels;
    }

    private static bool Overlaps(double x1, double y1, double width1, double height1,
        double x2, double y2, double width2, double height2) =>
        x1 < x2 + width2 && x1 + width1 > x2 && y1 < y2 + height2 && y1 + height1 > y2;

    public static bool IsRelationshipEmphasized(CharacterRelationship relationship, string? selectedRelationshipId,
        IReadOnlySet<string> selectedCharacters) => relationship.Id == selectedRelationshipId
            || selectedCharacters.Contains(relationship.FirstCharacterId)
            || selectedCharacters.Contains(relationship.SecondCharacterId);
}
