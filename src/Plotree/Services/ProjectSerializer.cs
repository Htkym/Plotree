using System.Text.Json;
using System.Text.Json.Serialization;
using Plotree.Helpers;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>JSON (de)serialization for .plotree project files.</summary>
public static class ProjectSerializer
{
    /// <summary>Highest file format version this build can read.</summary>
    public const int CurrentVersion = 4;

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
        ValidateRelationships(project);
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

        if (project.Version < 4)
        {
            project.Relationships = [];
        }

        // Be tolerant of malformed current-version documents too: null collections would make
        // every consuming view-model defensively complicated, and a stray legacy colorTag
        // must not be written back into an otherwise current-version file.
        NormalizeCollectionsAndLegacyMetadata(project);
        ValidateRelationships(project);
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
    /// <c>colorTag</c> member. This is also called before writes, guaranteeing current output uses
    /// <c>tagNames</c> exclusively.
    /// </summary>
    private static void NormalizeCollectionsAndLegacyMetadata(PlotProject project)
    {
        project.Nodes ??= [];
        project.Edges ??= [];
        project.Characters ??= [];
        project.Relationships ??= [];
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
            if (character.GraphGroupId is { } legacyGroup
                && project.Groups.Any(group => group.Id == legacyGroup)
                && !character.GroupIds.Contains(legacyGroup))
            {
                character.GroupIds.Add(legacyGroup);
            }
        }

        AssignMissingCharacterPositions(project.Characters);
    }

    /// <summary>Assigns a stable vacant grid position only to characters without one.</summary>
    public static void AssignMissingCharacterPositions(IReadOnlyList<Character> characters)
    {
        var occupied = characters
            .Where(character => character.GraphX is { } x && double.IsFinite(x)
                && character.GraphY is { } y && double.IsFinite(y))
            .Select(character => (X: character.GraphX!.Value, Y: character.GraphY!.Value))
            .ToList();

        var next = 0;
        foreach (var character in characters)
        {
            if (character.GraphX is { } x && double.IsFinite(x)
                && character.GraphY is { } y && double.IsFinite(y))
            {
                continue;
            }

            (double X, double Y) position;
            do
            {
                position = ((next % 4) * 220d, (next / 4) * 180d);
                next++;
            }
            while (occupied.Any(existing =>
                Math.Abs(existing.X - position.X) < 120 && Math.Abs(existing.Y - position.Y) < 110));

            character.GraphX = position.X;
            character.GraphY = position.Y;
            occupied.Add(position);
        }
    }

    private static void ValidateRelationships(PlotProject project)
    {
        var ids = project.Characters.Select(character => character.Id).ToHashSet(StringComparer.Ordinal);
        if (project.Relationships.Count > 0 && (ids.Count != project.Characters.Count
            || project.Characters.Any(character => string.IsNullOrWhiteSpace(character.Id))))
        {
            throw new InvalidDataException("The project contains ambiguous character IDs.");
        }
        var relationshipIds = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<(string, string)>();
        foreach (var relationship in project.Relationships)
        {
            if (relationship is null || string.IsNullOrWhiteSpace(relationship.Id) || !relationshipIds.Add(relationship.Id)
                || !ids.Contains(relationship.FirstCharacterId)
                || !ids.Contains(relationship.SecondCharacterId)
                || relationship.FirstCharacterId == relationship.SecondCharacterId
                || relationship.Label is null
                || !IsOpaqueHexColorOrEmpty(relationship.LabelBackgroundColor)
                || !IsOpaqueHexColorOrEmpty(relationship.LabelForegroundColor))
            {
                throw new InvalidDataException("The project contains an invalid character relationship.");
            }

            var pair = string.CompareOrdinal(relationship.FirstCharacterId, relationship.SecondCharacterId) < 0
                ? (relationship.FirstCharacterId, relationship.SecondCharacterId)
                : (relationship.SecondCharacterId, relationship.FirstCharacterId);
            if (!pairs.Add(pair))
            {
                throw new InvalidDataException("The project contains duplicate character relationships.");
            }
        }
    }

    private static bool IsOpaqueHexColorOrEmpty(string? color) =>
        string.IsNullOrWhiteSpace(color) || (color.Trim().Length is 4 or 7 && ColorHex.Parse(color) is not null);
}
