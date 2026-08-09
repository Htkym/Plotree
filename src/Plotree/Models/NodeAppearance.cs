using System.Text.Json.Serialization;

namespace Plotree.Models;

/// <summary>How much of a node's content a card shows.</summary>
public enum NodeDisplayMode
{
    /// <summary>Title plus a body excerpt.</summary>
    Full,

    /// <summary>Title plus a single body line.</summary>
    Compact,

    /// <summary>Title only.</summary>
    TitleOnly,
}

/// <summary>
/// Appearance values for a node card. Every member is nullable: null means
/// "not specified here", so the value falls through to the next level of the
/// resolution chain (node override → document per-type default → built-in).
/// Introduced in file format version 2.
/// </summary>
public class NodeAppearance
{
    /// <summary>Header/accent color as a hex string (e.g. "#FF6750A4"), or null.</summary>
    public string? HeaderColor { get; set; }

    /// <summary>Card width in world units, or null.</summary>
    public double? Width { get; set; }

    /// <summary>Card height in world units, or null.</summary>
    public double? Height { get; set; }

    /// <summary>Card display mode, or null.</summary>
    public NodeDisplayMode? DisplayMode { get; set; }

    /// <summary>True when nothing is specified here (never persisted).</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        HeaderColor is null && Width is null && Height is null && DisplayMode is null;

    public NodeAppearance Clone() => new()
    {
        HeaderColor = HeaderColor,
        Width = Width,
        Height = Height,
        DisplayMode = DisplayMode,
    };
}

/// <summary>Document-level appearance defaults, one entry per <see cref="NodeType"/>.</summary>
public class AppearanceDefaults
{
    public NodeAppearance Scene { get; set; } = new();

    public NodeAppearance Choice { get; set; } = new();

    public NodeAppearance Ending { get; set; } = new();

    /// <summary>Returns the defaults for a node type.</summary>
    public NodeAppearance For(NodeType type) => type switch
    {
        NodeType.Choice => Choice,
        NodeType.Ending => Ending,
        _ => Scene,
    };
}

/// <summary>Built-in appearance values used when neither the node nor the document specifies one.</summary>
public static class NodeAppearanceFallback
{
    public const double Width = 180;

    public const double Height = 76;

    public const NodeDisplayMode DisplayMode = NodeDisplayMode.Full;

    public const double MinWidth = 96;

    public const double MaxWidth = 640;

    public const double MinHeight = 48;

    public const double MaxHeight = 480;
}
