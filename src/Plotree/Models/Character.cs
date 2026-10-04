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

    /// <summary>Persistent position in the character relationship graph.</summary>
    public double? GraphX { get; set; }

    public double? GraphY { get; set; }

    /// <summary>Legacy graph group. GroupIds is the shared membership list used by both tabs.</summary>
    public string? GraphGroupId { get; set; }

    /// <summary>PNG/JPEG image bytes encoded as Base64 for portable project files.</summary>
    public string? AvatarData { get; set; }

    public string? AvatarContentType { get; set; }
}
