using Plotree.Models;
using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class NodeTextLayoutCalculatorTests
{
    [TestMethod]
    public void Calculate_FullModeUsesWrappedTitleLinesWhenBudgetingBody()
    {
        const double contentWidth = 100;
        const double contentHeight = 100;

        var shortTitle = NodeTextLayoutCalculator.Calculate(
            "Short",
            NodeDisplayMode.Full,
            contentWidth,
            contentHeight,
            hasBody: true);
        var wrappedTitle = NodeTextLayoutCalculator.Calculate(
            "A title that wraps onto a second line",
            NodeDisplayMode.Full,
            contentWidth,
            contentHeight,
            hasBody: true);

        Assert.AreEqual(1, shortTitle.TitleLineCount);
        Assert.AreEqual(2, wrappedTitle.TitleLineCount);
        Assert.AreEqual(shortTitle.BodyLineCapacity - 1, wrappedTitle.BodyLineCapacity);
    }

    [TestMethod]
    public void Calculate_PreservesDisplayModeBodyRules()
    {
        var compact = NodeTextLayoutCalculator.Calculate(
            "Title",
            NodeDisplayMode.Compact,
            100,
            200,
            hasBody: true);
        var titleOnly = NodeTextLayoutCalculator.Calculate(
            "Title",
            NodeDisplayMode.TitleOnly,
            100,
            200,
            hasBody: true);

        Assert.AreEqual(1, compact.BodyLineCapacity);
        Assert.AreEqual(0, titleOnly.BodyLineCapacity);
    }

    [TestMethod]
    public void Calculate_FullModeBodyBudgetGrowsWithHeight()
    {
        var shortCard = NodeTextLayoutCalculator.Calculate(
            "Title",
            NodeDisplayMode.Full,
            100,
            44,
            hasBody: true);
        var tallCard = NodeTextLayoutCalculator.Calculate(
            "Title",
            NodeDisplayMode.Full,
            100,
            176,
            hasBody: true);

        Assert.IsGreaterThan(shortCard.BodyLineCapacity, tallCard.BodyLineCapacity);
    }
}
