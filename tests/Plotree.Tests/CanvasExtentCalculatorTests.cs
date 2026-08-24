using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class CanvasExtentCalculatorTests
{
    [TestMethod]
    public void Calculate_IncludesNegativeCoordinatesAndPadding()
    {
        var extent = CanvasExtentCalculator.Calculate(
            [
                new CanvasNodeBounds(-300, -120, 100, 80),
                new CanvasNodeBounds(500, 400, 200, 160),
            ],
            viewportWidth: 800,
            viewportHeight: 600,
            zoom: 2,
            padding: 50);

        Assert.AreEqual(350d, extent.OriginX, 1e-9);
        Assert.AreEqual(170d, extent.OriginY, 1e-9);
        Assert.AreEqual(1_100d, extent.Width, 1e-9);
        Assert.AreEqual(780d, extent.Height, 1e-9);
    }

    [TestMethod]
    public void Calculate_AtLeastFillsViewportAfterZoom()
    {
        var extent = CanvasExtentCalculator.Calculate(
            [new CanvasNodeBounds(0, 0, 20, 20)],
            viewportWidth: 1_000,
            viewportHeight: 600,
            zoom: 4,
            padding: 10);

        Assert.AreEqual(250d, extent.Width, 1e-9);
        Assert.AreEqual(150d, extent.Height, 1e-9);
    }

    [TestMethod]
    public void Calculate_CentersEmptyGraphOnViewportAnchorWithoutClampedOffset()
    {
        var extent = CanvasExtentCalculator.Calculate(
            Array.Empty<CanvasNodeBounds>(),
            viewportWidth: 800,
            viewportHeight: 600,
            zoom: 2,
            padding: 50,
            anchorWorldX: -125,
            anchorWorldY: 75,
            anchorViewportX: 400,
            anchorViewportY: 300);

        Assert.AreEqual(325d, extent.OriginX, 1e-9);
        Assert.AreEqual(75d, extent.OriginY, 1e-9);
        Assert.AreEqual(400d, extent.Width, 1e-9);
        Assert.AreEqual(300d, extent.Height, 1e-9);

        Assert.AreEqual(400d, (extent.OriginX - 125d) * 2, 1e-9);
        Assert.AreEqual(300d, (extent.OriginY + 75d) * 2, 1e-9);
    }

    [TestMethod]
    public void Calculate_CentersSmallNegativeGraphOnAnchorWhenBarsAreUnnecessary()
    {
        var extent = CanvasExtentCalculator.Calculate(
            [new CanvasNodeBounds(-10, -20, 20, 20)],
            viewportWidth: 800,
            viewportHeight: 600,
            zoom: 1,
            padding: 50,
            anchorWorldX: 250,
            anchorWorldY: -100,
            anchorViewportX: 400,
            anchorViewportY: 300);

        Assert.AreEqual(150d, extent.OriginX, 1e-9);
        Assert.AreEqual(400d, extent.OriginY, 1e-9);
        Assert.AreEqual(800d, extent.Width, 1e-9);
        Assert.AreEqual(600d, extent.Height, 1e-9);
        Assert.AreEqual(400d, extent.OriginX + 250d, 1e-9);
        Assert.AreEqual(300d, extent.OriginY - 100d, 1e-9);
    }
}
