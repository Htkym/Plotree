using Plotree.Services;

namespace Plotree.Tests;

[TestClass]
public sealed class SvgTextWrapperTests
{
    [TestMethod]
    public void Wrap_PreservesExplicitLineBreaks()
    {
        var lines = SvgTextWrapper.Wrap("First line\nSecond line", 400, 3, 12);

        CollectionAssert.AreEqual(new[] { "First line", "Second line" }, lines.ToArray());
    }

    [TestMethod]
    public void Wrap_NeverSplitsEmojiOrCombiningTextElements()
    {
        var lines = SvgTextWrapper.Wrap("😀😀 e\u0301e\u0301e\u0301", 18, 2, 12);

        Assert.IsTrue(lines.Count > 0);
        Assert.IsFalse(string.Concat(lines).Contains('\uFFFD'));
        Assert.IsFalse(lines.Any(line => char.IsHighSurrogate(line[^1])));
        Assert.IsFalse(lines.Any(line => line.Length > 0 && char.IsLowSurrogate(line[0])));
        Assert.IsTrue(string.Concat(lines).Contains("😀", StringComparison.Ordinal));
    }
}
