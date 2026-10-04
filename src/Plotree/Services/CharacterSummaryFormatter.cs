using Plotree.Models;

namespace Plotree.Services;

/// <summary>Character names and group memberships for details and cards.</summary>
public static class CharacterSummaryFormatter
{
    public static string DetailLines(PlotProject project, IEnumerable<string> characterIds) =>
        string.Join("\n", Resolve(project, characterIds).Select(character =>
        {
            var groups = project.Groups.Where(group => character.GroupIds.Contains(group.Id))
                .Select(group => group.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            return groups.Length == 0 ? $"・{character.Name}"
                : $"・{character.Name}（{string.Join(", ", groups)}）";
        }));

    public static string CardNames(PlotProject project, IEnumerable<string> characterIds) =>
        string.Join(", ", Resolve(project, characterIds).Select(character => character.Name));

    private static IEnumerable<Character> Resolve(PlotProject project, IEnumerable<string> characterIds) =>
        characterIds.Distinct(StringComparer.Ordinal)
            .Select(id => project.Characters.FirstOrDefault(character => character.Id == id))
            .OfType<Character>().Where(character => !string.IsNullOrWhiteSpace(character.Name));
}
