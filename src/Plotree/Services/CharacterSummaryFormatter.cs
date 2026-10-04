using System.Globalization;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>Character names and group memberships for details and cards.</summary>
public static class CharacterSummaryFormatter
{
    public static string DetailLines(PlotProject project, IEnumerable<string> characterIds,
        Func<string, string>? getResource = null)
    {
        getResource ??= Loc.Get;
        string Resource(string key, string fallback)
        {
            var value = getResource(key);
            return value == key || string.IsNullOrEmpty(value) ? fallback : value;
        }
        var lineFormat = Resource("Detail_CharacterLine", "• {0}");
        var groupedLineFormat = Resource("Detail_CharacterLineWithGroups", "• {0} ({1})");
        var separator = Resource("Detail_CharacterGroupSeparator", ", ");
        return string.Join("\n", Resolve(project, characterIds).Select(character =>
        {
            var groups = project.Groups.Where(group => character.GroupIds.Contains(group.Id))
                .Select(group => group.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            return groups.Length == 0 ? string.Format(CultureInfo.CurrentCulture, lineFormat, character.Name)
                : string.Format(CultureInfo.CurrentCulture, groupedLineFormat, character.Name, string.Join(separator, groups));
        }));
    }

    public static string CardNames(PlotProject project, IEnumerable<string> characterIds) =>
        string.Join(", ", Resolve(project, characterIds).Select(character => character.Name));

    private static IEnumerable<Character> Resolve(PlotProject project, IEnumerable<string> characterIds) =>
        characterIds.Distinct(StringComparer.Ordinal)
            .Select(id => project.Characters.FirstOrDefault(character => character.Id == id))
            .OfType<Character>().Where(character => !string.IsNullOrWhiteSpace(character.Name));
}
