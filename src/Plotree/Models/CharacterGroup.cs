namespace Plotree.Models;

/// <summary>A named group that one or more characters can belong to.</summary>
public class CharacterGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Opaque #RRGGBB color used for the relationship graph cluster background.</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Whether this group background is shown in the character graph.</summary>
    public bool IsVisible { get; set; } = true;
}
