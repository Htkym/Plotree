namespace Plotree.Models;

/// <summary>Direction used by the automatic graph layout.</summary>
public enum LayoutDirection
{
    LeftToRight,
    TopToBottom,
}

/// <summary>Root document of a .plotree file.</summary>
public class PlotProject
{
    /// <summary>File format version, used for migrations. Kept in sync with ProjectSerializer.CurrentVersion.</summary>
    public int Version { get; set; } = 3;

    public string Title { get; set; } = "Untitled";

    public LayoutDirection LayoutDirection { get; set; } = LayoutDirection.LeftToRight;

    public List<PlotNode> Nodes { get; set; } = [];

    public List<PlotEdge> Edges { get; set; } = [];

    public List<Character> Characters { get; set; } = [];

    public List<CharacterGroup> Groups { get; set; } = [];

    public List<ColorTag> Tags { get; set; } = [];

    /// <summary>Per-node-type appearance defaults (file format version 2).</summary>
    public AppearanceDefaults Appearance { get; set; } = new();
}
