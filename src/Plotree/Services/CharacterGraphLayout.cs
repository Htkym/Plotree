using Plotree.Models;

namespace Plotree.Services;

public readonly record struct CharacterGroupRegion(string GroupId, double X, double Y, double Width, double Height);
public readonly record struct CharacterGroupLabel(string GroupId, int RegionIndex, double X, double Y, double Width, double Height);

/// <summary>Backgrounds and attached names form a stack without moving their member characters.</summary>
public static class CharacterGraphLayout
{
    public const double MinimumGroupLabelWidth = 48;
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
        double nodeWidth = 120, double nodeHeight = 110)
    {
        var stack = CalculateStack(project, null, nodeWidth, nodeHeight);
        return stack.Bounds.Concat(CalculateLabelEnvelopes(stack.Headers))
            .OrderBy(bound => bound.X).ThenBy(bound => bound.Y).ToArray();
    }

    private static (IReadOnlyList<CharacterGroupRegion> Regions, IReadOnlyList<CanvasNodeBounds> Bounds, IReadOnlyList<CanvasNodeBounds> Headers)
        CalculateStack(PlotProject project, string? selectedGroupId, double nodeWidth, double nodeHeight)
    {
        var regions = new List<CharacterGroupRegion>();
        var bounds = new List<CanvasNodeBounds>();
        var headers = new List<CanvasNodeBounds>();
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
                headers.Add(new(raw.X - horizontalReserve + 12, raw.Y - verticalReserve + 8,
                    horizontalReserve + Math.Max(MinimumGroupLabelWidth, Math.Min(220, raw.Width + horizontalReserve - 24)),
                    verticalReserve + GroupLabelHeight));
            }
        }
        return (regions, bounds.OrderBy(bound => bound.X).ThenBy(bound => bound.Y).ToArray(), headers);
    }

    /// <summary>Keep usable click targets; crowded headers use additional rows above their backgrounds.</summary>
    public static IReadOnlyList<CharacterGroupLabel> CalculateLabels(IReadOnlyList<CharacterGroupRegion> regions)
    {
        var labels = new List<CharacterGroupLabel>();
        // Place the frontmost group first so overflow rows never hide its name.
        for (var index = regions.Count - 1; index >= 0; index--)
        {
            var region = regions[index];
            var original = new CharacterGroupLabel(region.GroupId, index, region.X + 12, region.Y + 8,
                Math.Max(MinimumGroupLabelWidth, Math.Min(220, region.Width - 24)), GroupLabelHeight);
            var label = original;
            for (var previousIndex = 0; previousIndex < labels.Count; previousIndex++)
            {
                var previous = labels[previousIndex];
                if (!Overlaps(label.X, label.Y, label.Width, label.Height,
                    previous.X, previous.Y, previous.Width, previous.Height)) continue;
                if (label.X + GroupLabelGap + MinimumGroupLabelWidth <= previous.X)
                    label = label with { Width = previous.X - label.X - GroupLabelGap };
                else if (previous.X + GroupLabelGap + MinimumGroupLabelWidth <= label.X)
                    labels[previousIndex] = previous with { Width = label.X - previous.X - GroupLabelGap };
                else
                {
                    var right = label.X + label.Width;
                    var overlapLeft = Math.Max(previous.X, label.X);
                    var overlapRight = Math.Min(previous.X + previous.Width, right);
                    var split = (overlapLeft + overlapRight) / 2;
                    var previousWidth = split - GroupLabelGap / 2 - previous.X;
                    var newX = split + GroupLabelGap / 2;
                    if (previousWidth >= MinimumGroupLabelWidth && right - newX >= MinimumGroupLabelWidth)
                    {
                        labels[previousIndex] = previous with { Width = previousWidth };
                        label = label with { X = newX, Width = right - newX };
                        previousIndex = -1;
                    }
                    else
                    {
                        label = original with { Y = previous.Y - StackStepY };
                        previousIndex = -1;
                    }
                }
            }
            labels.Add(label);
        }
        return labels.OrderBy(label => label.RegionIndex).ToArray();
    }

    private static IEnumerable<CanvasNodeBounds> CalculateLabelEnvelopes(IReadOnlyList<CanvasNodeBounds> headers)
    {
        var parents = Enumerable.Range(0, headers.Count).ToArray();
        var counts = Enumerable.Repeat(1, headers.Count).ToArray();
        var minimumY = headers.Select(header => header.Y).ToArray();
        int Root(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }
        CanvasNodeBounds Envelope(int index)
        {
            var root = Root(index);
            var top = minimumY[root] - (counts[root] - 1) * StackStepY;
            var header = headers[index];
            return header with { Y = top, Height = header.Y + header.Height - top };
        }
        // Overflow can reach another header above the original stack. Merge those
        // envelopes until all possible selection orders fit, without enlarging backgrounds.
        bool changed;
        do
        {
            changed = false;
            for (var a = 0; a < headers.Count; a++)
                for (var b = a + 1; b < headers.Count; b++)
                {
                    var firstRoot = Root(a); var secondRoot = Root(b);
                    if (firstRoot == secondRoot) continue;
                    var first = Envelope(a); var second = Envelope(b);
                    if (!Overlaps(first.X, first.Y, first.Width, first.Height,
                        second.X, second.Y, second.Width, second.Height)) continue;
                    parents[secondRoot] = firstRoot;
                    counts[firstRoot] += counts[secondRoot];
                    minimumY[firstRoot] = Math.Min(minimumY[firstRoot], minimumY[secondRoot]);
                    changed = true;
                }
        } while (changed);
        return Enumerable.Range(0, headers.Count).Select(Envelope).ToArray();
    }

    private static bool Overlaps(double x1, double y1, double width1, double height1,
        double x2, double y2, double width2, double height2) =>
        x1 < x2 + width2 && x1 + width1 > x2 && y1 < y2 + height2 && y1 + height1 > y2;

    public static bool IsRelationshipEmphasized(CharacterRelationship relationship, string? selectedRelationshipId,
        IReadOnlySet<string> selectedCharacters) => relationship.Id == selectedRelationshipId
            || selectedCharacters.Contains(relationship.FirstCharacterId)
            || selectedCharacters.Contains(relationship.SecondCharacterId);
}
