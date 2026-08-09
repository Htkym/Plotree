using System.Text.Json;
using System.Text.Json.Serialization;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>JSON (de)serialization for .plotree project files.</summary>
public static class ProjectSerializer
{
    /// <summary>Highest file format version this build can read.</summary>
    public const int CurrentVersion = 3;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Keeps version 2 documents free of the many unset (null) appearance members.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new NodeTypeJsonConverter(),
            new JsonStringEnumConverter<LayoutDirection>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<NodeDisplayMode>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<EdgeSide>(JsonNamingPolicy.CamelCase),
        },
    };

    private static readonly PlotreeJsonContext Context = new(new JsonSerializerOptions(Options));

    public static string Serialize(PlotProject project)
    {
        NormalizeCollectionsAndLegacyMetadata(project);
        project.Version = CurrentVersion;
        return JsonSerializer.Serialize(project, Context.PlotProject);
    }

    public static PlotProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize(json, Context.PlotProject)
            ?? throw new InvalidDataException("The file does not contain a valid Plotree project.");

        if (project.Version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"This file was created by a newer version of Plotree (file version {project.Version}, supported up to {CurrentVersion}).");
        }

        Migrate(project);
        return project;
    }

    /// <summary>Upgrades an older document in place to <see cref="CurrentVersion"/>.</summary>
    private static void Migrate(PlotProject project)
    {
        if (project.Version < 2)
        {
            MigrateV1ToV2(project);
        }

        if (project.Version < 3)
        {
            MigrateV2ToV3(project);
        }

        // Be tolerant of malformed current-version documents too: null collections would make
        // every consuming view-model defensively complicated, and a stray legacy colorTag
        // must not be written back into an otherwise version 3 file.
        NormalizeCollectionsAndLegacyMetadata(project);
        project.Version = CurrentVersion;
    }

    /// <summary>
    /// Version 1 → 2. The retired "start" node type and the "branch" spelling are already handled
    /// while reading by <see cref="NodeTypeJsonConverter"/> (a v1 start node becomes a Scene and a
    /// v1 branch node becomes a Choice), so every node keeps its data and every edge keeps its
    /// endpoints. This step only fills in the version 2 structures; v1 nodes and edges carry no
    /// appearance or side information, so those stay null and resolve through the document defaults.
    /// </summary>
    private static void MigrateV1ToV2(PlotProject project)
    {
        project.Appearance ??= new AppearanceDefaults();
        project.Appearance.Scene ??= new NodeAppearance();
        project.Appearance.Choice ??= new NodeAppearance();
        project.Appearance.Ending ??= new NodeAppearance();
    }

    /// <summary>
    /// Version 2 → 3. Nodes now retain every assigned color tag in <see cref="PlotNode.TagNames"/>
    /// rather than one scalar <c>colorTag</c>; characters may belong to several project groups.
    /// Groups and memberships are initialized even when absent from a version 2 document.
    /// </summary>
    private static void MigrateV2ToV3(PlotProject project) =>
        NormalizeCollectionsAndLegacyMetadata(project);

    /// <summary>
    /// Restores collection invariants after deserialization and consumes the retired scalar
    /// <c>colorTag</c> member. This is also called before writes, guaranteeing v3 output uses
    /// <c>tagNames</c> exclusively.
    /// </summary>
    private static void NormalizeCollectionsAndLegacyMetadata(PlotProject project)
    {
        project.Nodes ??= [];
        project.Edges ??= [];
        project.Characters ??= [];
        project.Groups ??= [];
        project.Tags ??= [];

        foreach (var node in project.Nodes)
        {
            node.TagNames ??= [];
            node.CharacterIds ??= [];

            if (!string.IsNullOrWhiteSpace(node.LegacyColorTag)
                && !node.TagNames.Contains(node.LegacyColorTag, StringComparer.Ordinal))
            {
                node.TagNames.Add(node.LegacyColorTag);
            }

            node.LegacyColorTag = null;
        }

        foreach (var character in project.Characters)
        {
            character.GroupIds ??= [];
        }
    }
}
