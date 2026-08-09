using Plotree.Models;

namespace Plotree.Services;

/// <summary>Calculates the shared title and body line budget for node cards and SVG output.</summary>
public static class NodeTextLayoutCalculator
{
    public const double TitleFontSize = 14;
    public const double TitleLineHeight = 16;
    public const double BodyFontSize = 12;
    public const double BodyLineHeight = 14;
    public const double SectionSpacing = 2;

    public static NodeTextLineBudget Calculate(
        string title,
        NodeDisplayMode displayMode,
        double contentWidth,
        double contentHeight,
        bool hasBody)
    {
        var titleLineLimit = displayMode == NodeDisplayMode.TitleOnly ? 3 : 2;
        var titleCapacity = Math.Min(titleLineLimit, MaximumLines(contentHeight, TitleLineHeight));
        hasBody = displayMode != NodeDisplayMode.TitleOnly && hasBody;

        if (hasBody && contentHeight >= TitleLineHeight + SectionSpacing + BodyLineHeight)
        {
            titleCapacity = Math.Min(
                titleCapacity,
                MaximumLines(contentHeight - SectionSpacing - BodyLineHeight, TitleLineHeight));
        }

        if (titleCapacity == 0 && contentHeight >= TitleFontSize)
        {
            titleCapacity = 1;
        }

        var titleLineCount = SvgTextWrapper.Wrap(
            title,
            contentWidth,
            titleCapacity,
            TitleFontSize).Count;
        var remainingHeight = contentHeight - titleLineCount * TitleLineHeight - SectionSpacing;
        var bodyCapacity = hasBody
            ? Math.Min(
                displayMode == NodeDisplayMode.Compact ? 1 : int.MaxValue,
                MaximumLines(remainingHeight, BodyLineHeight))
            : 0;

        return new NodeTextLineBudget(titleCapacity, titleLineCount, bodyCapacity);
    }

    private static int MaximumLines(double availableHeight, double lineHeight) =>
        availableHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(availableHeight / lineHeight));
}

public readonly record struct NodeTextLineBudget(
    int TitleLineCapacity,
    int TitleLineCount,
    int BodyLineCapacity);
