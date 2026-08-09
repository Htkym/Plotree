namespace Plotree.Models;

/// <summary>A directed connection between two plot nodes.</summary>
public class PlotEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string FromId { get; set; } = string.Empty;

    public string ToId { get; set; } = string.Empty;

    /// <summary>Choice label shown on choice edges (e.g. "Accept the quest").</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Side of the source node the edge leaves from (file format version 2).
    /// Null means "decide from the layout direction".
    /// </summary>
    public EdgeSide? FromSide { get; set; }

    /// <summary>
    /// Side of the target node the edge arrives at (file format version 2).
    /// Null means "decide from the layout direction".
    /// </summary>
    public EdgeSide? ToSide { get; set; }
}
