namespace Plotree.Services;

/// <summary>Options controlling the SVG graph export.</summary>
public sealed class SvgExportOptions
{
    /// <summary>Whitespace, in graph units, around the complete graph.</summary>
    public double Padding { get; init; } = 32;

    /// <summary>
    /// Largest allowed exported view-box dimension, in graph units. This guards
    /// against corrupt coordinates producing impractically large SVG documents.
    /// </summary>
    public double MaxDimension { get; init; } = 1_000_000;
}

/// <summary>Options controlling PNG rendering from a WinUI visual.</summary>
public sealed class PngExportOptions
{
    /// <summary>Largest supported width or height of a rendered PNG.</summary>
    public const uint MaxPixelDimension = 8_192;

    /// <summary>Largest supported number of output pixels (128 MiB of BGRA pixels).</summary>
    public const long MaxPixelCount = 33_554_432;

    /// <summary>Required PNG width in physical pixels.</summary>
    public required uint PixelWidth { get; init; }

    /// <summary>Required PNG height in physical pixels.</summary>
    public required uint PixelHeight { get; init; }
}
