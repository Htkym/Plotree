using Windows.System;

namespace Plotree.Services;

public readonly record struct GraphLineSegment(double X1, double Y1, double X2, double Y2);
public readonly record struct RelationshipGeometry(GraphLineSegment Visible, GraphLineSegment HitTarget,
    double HitThickness, bool CanHit);

/// <summary>Rendering layers and screen-sized relationship hit targets, independent of visible stroke width.</summary>
public static class CharacterGraphInteraction
{
    public static void SelectCharacter(ISet<string> selected, string id, bool control, bool shift,
        bool preserveExistingSelection = false)
    {
        if (control)
        {
            if (!selected.Add(id)) selected.Remove(id);
        }
        else if (shift) selected.Add(id);
        else if (!preserveExistingSelection || !selected.Contains(id))
        {
            selected.Clear();
            selected.Add(id);
        }
    }

    public const int BackgroundLayer = 0;
    public const int GroupNameLayer = 1;
    public const int CharacterLayer = 2;
    public const int RelationshipLayer = 3;
    public const int RelationshipHitLayer = 4;
    public const int RelationshipLabelLayer = 5;
    public const double SelectionRingThickness = 6;
    public const double HitThicknessInPixels = 16;
    public const double AvatarDiameter = 68;
    public const double SelectionRingDiameter = AvatarDiameter + SelectionRingThickness * 2;
    public const double SelectionRingInset = SelectionRingThickness;
    public const double VisibleLineInset = SelectionRingDiameter / 2 + 3;

    /// <summary>Outer node bounds include the entire ring while the avatar retains its original coordinates.</summary>
    public static CanvasNodeBounds CalculateCharacterBounds(double x, double y, double width = 120, double height = 110) =>
        new(x, y - SelectionRingInset, width, height + SelectionRingInset);

    public static CanvasNodeBounds CalculateSelectionRingBounds(double x, double y, double width = 120) =>
        new(x + (width - SelectionRingDiameter) / 2, y - SelectionRingInset,
            SelectionRingDiameter, SelectionRingDiameter);

    public static RelationshipGeometry Calculate(double x1, double y1, double x2, double y2, double zoom)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var thickness = HitThicknessInPixels / Math.Clamp(zoom, 0.25, 4);
        if (distance < 0.001)
        {
            var point = new GraphLineSegment(x1, y1, x1, y1);
            return new(point, point, thickness, false);
        }
        GraphLineSegment Trim(double inset)
        {
            inset = Math.Min(inset, distance / 2);
            var ox = dx * inset / distance;
            var oy = dy * inset / distance;
            return new(x1 + ox, y1 + oy, x2 - ox, y2 - oy);
        }
        // Stop outside the avatar ring. The larger hit target also leaves the nearby connector usable.
        var hitInset = 56 + thickness / 2;
        return new(Trim(VisibleLineInset), Trim(hitInset), thickness, distance > hitInset * 2);
    }

    public static bool ContainsHit(RelationshipGeometry geometry, double x, double y)
    {
        if (!geometry.CanHit) return false;
        var line = geometry.HitTarget;
        var dx = line.X2 - line.X1;
        var dy = line.Y2 - line.Y1;
        var t = Math.Clamp(((x - line.X1) * dx + (y - line.Y1) * dy) / (dx * dx + dy * dy), 0, 1);
        var ox = x - line.X1 - t * dx;
        var oy = y - line.Y1 - t * dy;
        return ox * ox + oy * oy <= geometry.HitThickness * geometry.HitThickness / 4;
    }
}

/// <summary>Native Button clicks supplement keyboard/automation without repeating pointer selection.</summary>
public sealed class CharacterNodeActivation
{
    private string? _pointerCharacterId;
    private int _generation;

    public void BeginPointer(string id)
    {
        _pointerCharacterId = id;
        _generation++;
    }

    public void PrepareKeyboard(VirtualKey key)
    {
        if (key is VirtualKey.Enter or VirtualKey.Space) Reset();
    }

    public bool ShouldSelectFromClick(string id) => id != _pointerCharacterId;

    public Action CompletePointer()
    {
        var generation = _generation;
        return () => { if (generation == _generation) Reset(); };
    }

    public void Reset()
    {
        _pointerCharacterId = null;
        _generation++;
    }
}
