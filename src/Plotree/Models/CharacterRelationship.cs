namespace Plotree.Models;

/// <summary>An undirected relationship between two characters with an optional label.</summary>
public class CharacterRelationship
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string FirstCharacterId { get; set; } = string.Empty;

    public string SecondCharacterId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Optional opaque label background color in #RRGGBB format.</summary>
    public string? LabelBackgroundColor { get; set; }

    /// <summary>Optional opaque label foreground color in #RRGGBB format.</summary>
    public string? LabelForegroundColor { get; set; }
}
