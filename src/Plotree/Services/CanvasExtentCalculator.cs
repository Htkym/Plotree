namespace Plotree.Services;

/// <summary>World-space bounds of one node used when calculating the scrollable canvas extent.</summary>
public readonly record struct CanvasNodeBounds(double X, double Y, double Width, double Height);

/// <summary>Scrollable canvas dimensions in world units and the positive-space origin.</summary>
public readonly record struct CanvasExtent(double OriginX, double OriginY, double Width, double Height);

/// <summary>Calculates zoom-aware canvas bounds while retaining room around the graph.</summary>
public static class CanvasExtentCalculator
{
    /// <summary>
    /// Calculates an extent around the supplied viewport anchor. The interval always contains
    /// the graph bounds and the complete viewport-sized interval around the anchor, which means
    /// the anchor can be restored without relying on a clamped ScrollViewer offset.
    /// </summary>
    public static CanvasExtent Calculate(
        IEnumerable<CanvasNodeBounds> nodes,
        double viewportWidth,
        double viewportHeight,
        double zoom,
        double padding,
        double anchorWorldX,
        double anchorWorldY,
        double anchorViewportX,
        double anchorViewportY)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight)
            || !double.IsFinite(zoom) || !double.IsFinite(padding)
            || !double.IsFinite(anchorWorldX) || !double.IsFinite(anchorWorldY)
            || !double.IsFinite(anchorViewportX) || !double.IsFinite(anchorViewportY)
            || viewportWidth < 0 || viewportHeight < 0 || zoom <= 0 || padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), "Canvas extent inputs must be finite and positive.");
        }

        var bounds = nodes.ToArray();
        var graphMinX = anchorWorldX - padding;
        var graphMinY = anchorWorldY - padding;
        var graphMaxX = anchorWorldX + padding;
        var graphMaxY = anchorWorldY + padding;

        if (bounds.Length > 0)
        {
            graphMinX = bounds.Min(node => node.X) - padding;
            graphMinY = bounds.Min(node => node.Y) - padding;
            graphMaxX = bounds.Max(node => node.X + node.Width) + padding;
            graphMaxY = bounds.Max(node => node.Y + node.Height) + padding;
        }

        // Include the complete viewport interval around the anchor. This distributes any spare
        // extent on the side needed to keep the anchor visible instead of letting ChangeView
        // clamp it to zero when a small graph exactly fills the viewport.
        var viewportMinX = anchorWorldX - anchorViewportX / zoom;
        var viewportMinY = anchorWorldY - anchorViewportY / zoom;
        var viewportMaxX = anchorWorldX + (viewportWidth - anchorViewportX) / zoom;
        var viewportMaxY = anchorWorldY + (viewportHeight - anchorViewportY) / zoom;

        var minX = Math.Min(graphMinX, viewportMinX);
        var minY = Math.Min(graphMinY, viewportMinY);
        var maxX = Math.Max(graphMaxX, viewportMaxX);
        var maxY = Math.Max(graphMaxY, viewportMaxY);

        return new CanvasExtent(
            -minX,
            -minY,
            maxX - minX,
            maxY - minY);
    }

    /// <summary>Compatibility overload using a zero-world center anchor.</summary>
    public static CanvasExtent Calculate(
        IEnumerable<CanvasNodeBounds> nodes,
        double viewportWidth,
        double viewportHeight,
        double zoom,
        double padding) =>
        Calculate(
            nodes,
            viewportWidth,
            viewportHeight,
            zoom,
            padding,
            anchorWorldX: 0,
            anchorWorldY: 0,
            anchorViewportX: viewportWidth / 2,
            anchorViewportY: viewportHeight / 2);
}
