namespace Plotree.Services;

public readonly record struct MenuPlacement(double X, double Y, double Width, double Height);

/// <summary>Bounds for a top menu beneath its owner and inside the application root.</summary>
public static class MenuPlacementCalculator
{
    public static MenuPlacement BelowItem(double rootWidth, double rootHeight, double ownerX, double ownerY,
        double ownerHeight, double requestedWidth, double requestedHeight)
    {
        const double margin = 4;
        var left = Math.Min(margin, Math.Max(0, rootWidth) / 2);
        var top = Math.Min(margin, Math.Max(0, rootHeight) / 2);
        var availableWidth = Math.Max(0, rootWidth - left * 2);
        var width = Math.Clamp(requestedWidth, 0, availableWidth);
        var x = Math.Clamp(ownerX, left, Math.Max(left, rootWidth - left - width));
        var y = Math.Clamp(ownerY + ownerHeight, top, Math.Max(top, rootHeight - top));
        var height = Math.Clamp(requestedHeight, 0, Math.Max(0, rootHeight - top - y));
        return new(x, y, width, height);
    }
}
