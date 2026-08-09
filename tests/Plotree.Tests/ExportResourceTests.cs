using System.Xml.Linq;

namespace Plotree.Tests;

/// <summary>
/// Validates the shipped resource dictionaries directly, because MRT Core cannot resolve
/// resources outside the packaged/bootstrapped app: this is what makes the label assertions
/// in <see cref="PlotExporterTests"/> meaningful for real users.
/// </summary>
[TestClass]
public sealed class ExportResourceTests
{
    private static readonly string[] RequiredExportKeys =
    [
        "Export_Route",
        "Export_Choice",
        "Export_Body",
        "Export_Memo",
        "Export_Empty",
        "Export_Warnings",
        "Export_DeadEnd",
        "Export_Unreachable",
        "Export_NoEndings",
        "Export_NoRoutes",
        "Export_Truncated",
        "NodeType_Scene",
        "NodeType_Choice",
        "NodeType_Ending",
        "Default_UntitledNode",
    ];

    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", $"{language}.Resources.xml");
        Assert.IsTrue(File.Exists(path), $"Resource file '{path}' was not copied next to the tests.");

        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(element => element.Attribute("name") is not null)
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("ja-JP")]
    public void EveryExportKeyIsDefined(string language)
    {
        var resources = Load(language);

        var missing = RequiredExportKeys.Where(key => !resources.ContainsKey(key)).ToList();
        Assert.AreEqual(0, missing.Count, $"{language} is missing: {string.Join(", ", missing)}");
        foreach (var key in RequiredExportKeys)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(resources[key]), $"{language}/{key} is blank.");
        }
    }

    [TestMethod]
    public void BodyAndMemoLabelsAreDistinctInEveryLanguage()
    {
        foreach (var language in new[] { "en-US", "ja-JP" })
        {
            var resources = Load(language);
            Assert.AreNotEqual(
                resources["Export_Body"],
                resources["Export_Memo"],
                $"{language} uses the same label for Body and Memo.");
        }
    }

    [TestMethod]
    [DataRow("en-US", "Export_Route", 2)]
    [DataRow("en-US", "Export_Choice", 1)]
    [DataRow("en-US", "Export_DeadEnd", 2)]
    [DataRow("en-US", "Export_Unreachable", 2)]
    [DataRow("en-US", "Export_Truncated", 1)]
    [DataRow("ja-JP", "Export_Route", 2)]
    [DataRow("ja-JP", "Export_Choice", 1)]
    [DataRow("ja-JP", "Export_DeadEnd", 2)]
    [DataRow("ja-JP", "Export_Unreachable", 2)]
    [DataRow("ja-JP", "Export_Truncated", 1)]
    public void FormattedResourcesDeclareEveryPlaceholder(string language, string key, int placeholderCount)
    {
        var value = Load(language)[key];

        for (var index = 0; index < placeholderCount; index++)
        {
            Assert.IsTrue(
                value.Contains($"{{{index}}}", StringComparison.Ordinal),
                $"{language}/{key} ('{value}') is missing the {{{index}}} placeholder.");
        }
    }

    [TestMethod]
    public void TranslationsCoverTheSameKeySet()
    {
        var english = Load("en-US");
        var japanese = Load("ja-JP");

        var missingInJapanese = english.Keys.Except(japanese.Keys, StringComparer.Ordinal).Order().ToList();
        var missingInEnglish = japanese.Keys.Except(english.Keys, StringComparer.Ordinal).Order().ToList();

        Assert.AreEqual(0, missingInJapanese.Count, $"ja-JP is missing: {string.Join(", ", missingInJapanese)}");
        Assert.AreEqual(0, missingInEnglish.Count, $"en-US is missing: {string.Join(", ", missingInEnglish)}");
    }
}
