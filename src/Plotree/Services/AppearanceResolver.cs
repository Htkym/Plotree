using Plotree.Models;

namespace Plotree.Services;

/// <summary>Fully resolved appearance for one node — every value is concrete.</summary>
/// <param name="HeaderColor">Header color hex string, or null when the node has no explicit color.</param>
/// <param name="Width">Card width in world units.</param>
/// <param name="Height">Card height in world units.</param>
/// <param name="DisplayMode">How much content the card shows.</param>
public readonly record struct ResolvedAppearance(
    string? HeaderColor,
    double Width,
    double Height,
    NodeDisplayMode DisplayMode);

/// <summary>
/// Resolves node appearance through the version 2 chain:
/// per-node override → document per-type default → built-in fallback.
/// Pure model logic; it draws nothing.
/// </summary>
public static class AppearanceResolver
{
    /// <summary>Resolves the effective appearance of a node within its project.</summary>
    public static ResolvedAppearance Resolve(PlotProject? project, PlotNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var overrides = node.Appearance;
        var defaults = project?.Appearance?.For(node.Type);

        return new ResolvedAppearance(
            Pick(overrides?.HeaderColor, defaults?.HeaderColor),
            Clamp(
                Pick(overrides?.Width, defaults?.Width) ?? NodeAppearanceFallback.Width,
                NodeAppearanceFallback.MinWidth,
                NodeAppearanceFallback.MaxWidth),
            Clamp(
                Pick(overrides?.Height, defaults?.Height) ?? NodeAppearanceFallback.Height,
                NodeAppearanceFallback.MinHeight,
                NodeAppearanceFallback.MaxHeight),
            Pick(overrides?.DisplayMode, defaults?.DisplayMode) ?? NodeAppearanceFallback.DisplayMode);
    }

    /// <summary>Resolves the defaults for a node type, without any per-node override.</summary>
    public static ResolvedAppearance ResolveDefaults(PlotProject? project, NodeType type) =>
        Resolve(project, new PlotNode { Type = type });

    private static string? Pick(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? (string.IsNullOrWhiteSpace(second) ? null : second) : first;

    private static T? Pick<T>(T? first, T? second)
        where T : struct => first ?? second;

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
