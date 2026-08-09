namespace Plotree.Models;

/// <summary>A named group that one or more characters can belong to.</summary>
public class CharacterGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;
}
