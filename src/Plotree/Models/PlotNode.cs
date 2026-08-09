using System.Text.Json.Serialization;

namespace Plotree.Models;

/// <summary>Kind of a plot node.</summary>
public enum NodeType
{
    Scene,

    /// <summary>A branching point that offers labelled choices. Persisted as "branch" for v1 compatibility.</summary>
    Choice,

    Ending,
}

/// <summary>A single node in the plot graph.</summary>
public class PlotNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public NodeType Type { get; set; } = NodeType.Scene;

    public string Title { get; set; } = string.Empty;

    /// <summary>Main content / synopsis of the scene.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Free-form author memo (not part of the story text).</summary>
    public string Memo { get; set; } = string.Empty;

    /// <summary>Names of the <see cref="ColorTag"/>s defined on the project and assigned to this node.</summary>
    public List<string> TagNames { get; set; } = [];

    /// <summary>
    /// The version 1/2 <c>colorTag</c> JSON member. It is read during deserialization and moved
    /// into <see cref="TagNames"/> by <see cref="Services.ProjectSerializer"/>, so current files
    /// never write it back out.
    /// </summary>
    [JsonPropertyName("colorTag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyColorTag { get; set; }

    /// <summary>
    /// Compatibility view of the first assigned tag for callers that have not yet moved to
    /// <see cref="TagNames"/>. This member is never serialized.
    /// </summary>
    [JsonIgnore]
    public string? ColorTag
    {
        get => TagNames.FirstOrDefault();
        set
        {
            TagNames.Clear();
            if (!string.IsNullOrWhiteSpace(value))
            {
                TagNames.Add(value);
            }
        }
    }

    /// <summary>Ids of <see cref="Character"/>s appearing in this node.</summary>
    public List<string> CharacterIds { get; set; } = [];

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>True when the node was positioned manually and must be excluded from auto-layout.</summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Per-node appearance overrides (file format version 2). Null — and any null member —
    /// falls back to the document defaults in <see cref="PlotProject.Appearance"/>.
    /// </summary>
    public NodeAppearance? Appearance { get; set; }
}
