using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class MenuPlacementCalculatorTests
{
    [TestMethod]
    [DataRow(20d, 0d, 32d)]
    [DataRow(700d, 40d, 48d)]
    [DataRow(-20d, 130d, 32d)]
    public void TopMenus_OpenBelowOwnerAndStayInsideWindow(double ownerX, double ownerY, double ownerHeight)
    {
        var menu = MenuPlacementCalculator.BelowItem(800, 600, ownerX, ownerY, ownerHeight, 320, 400);
        Assert.AreEqual(ownerY + ownerHeight, menu.Y);
        Assert.IsTrue(menu.X >= 0 && menu.X + menu.Width <= 800);
        Assert.IsLessThanOrEqualTo(600d, menu.Y + menu.Height);
    }

    [TestMethod]
    public void LargeManagementMenu_IsLimitedToSpaceBelowItemInsteadOfOpeningAboveIt()
    {
        var menu = MenuPlacementCalculator.BelowItem(360, 240, 300, 80, 40, 572, 600);
        Assert.AreEqual(120d, menu.Y);
        Assert.AreEqual(352d, menu.Width);
        Assert.AreEqual(116d, menu.Height);
        Assert.AreEqual(4d, menu.X);
    }

    [TestMethod]
    [DataRow(0d, 0d)]
    [DataRow(6d, 6d)]
    [DataRow(200d, 40d)]
    public void TinyOrResizingWindows_NeverProduceNegativeMenuDimensions(double width, double height)
    {
        var menu = MenuPlacementCalculator.BelowItem(width, height, 300, 10, 40, 572, 600);
        Assert.IsTrue(menu.Width >= 0 && menu.Height >= 0);
        Assert.IsTrue(menu.X >= 0 && menu.X + menu.Width <= width);
        Assert.IsTrue(menu.Y >= 0 && menu.Y + menu.Height <= height);
    }
}
