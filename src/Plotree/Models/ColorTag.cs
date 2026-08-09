namespace Plotree.Models;

/// <summary>A named color tag that nodes can reference by <see cref="Name"/>.</summary>
public class ColorTag
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Color as a hex string (e.g. "#4F6BED").</summary>
    public string Color { get; set; } = "#808080";
}
