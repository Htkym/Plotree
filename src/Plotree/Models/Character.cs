namespace Plotree.Models;

/// <summary>A story character that can be referenced from plot nodes.</summary>
public class Character
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Display color as a hex string (e.g. "#FF8800"), or null for default.</summary>
    public string? Color { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>Ids of <see cref="CharacterGroup"/>s this character belongs to.</summary>
    public List<string> GroupIds { get; set; } = [];
}
