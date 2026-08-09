using System.Text.Json;
using System.Text.Json.Serialization;
using Plotree.Models;

namespace Plotree.Services;

/// <summary>
/// Wire converter for <see cref="NodeType"/>.
/// Writing keeps the version 1 vocabulary ("branch" for <see cref="NodeType.Choice"/>) so files
/// stay readable by older builds. Reading is deliberately permissive: the retired v1 "start"
/// member maps to <see cref="NodeType.Scene"/> (node data and edges are kept intact), "choice" is
/// accepted alongside "branch", and anything unknown degrades to <see cref="NodeType.Scene"/>
/// instead of failing the whole load.
/// </summary>
internal sealed class NodeTypeJsonConverter : JsonConverter<NodeType>
{
    private const string SceneWire = "scene";
    private const string ChoiceWire = "branch"; // v1 wire name, preserved on write
    private const string EndingWire = "ending";

    public override NodeType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => Parse(reader.GetString()),
            JsonTokenType.Number => FromLegacyOrdinal(reader.TryGetInt32(out var ordinal) ? ordinal : -1),
            _ => NodeType.Scene,
        };

    public override void Write(Utf8JsonWriter writer, NodeType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToWire(value));

    public override NodeType ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString());

    public override void WriteAsPropertyName(Utf8JsonWriter writer, NodeType value, JsonSerializerOptions options) =>
        writer.WritePropertyName(ToWire(value));

    internal static string ToWire(NodeType value) => value switch
    {
        NodeType.Choice => ChoiceWire,
        NodeType.Ending => EndingWire,
        _ => SceneWire,
    };

    private static NodeType Parse(string? value) => value?.ToLowerInvariant() switch
    {
        ChoiceWire or "choice" => NodeType.Choice,
        EndingWire => NodeType.Ending,
        // "start" was removed in file format version 2; such nodes become plain scenes.
        _ => NodeType.Scene,
    };

    /// <summary>Maps the v1 enum ordinals (Start, Scene, Branch, Ending) in case a file stored numbers.</summary>
    private static NodeType FromLegacyOrdinal(int ordinal) => ordinal switch
    {
        2 => NodeType.Choice,
        3 => NodeType.Ending,
        _ => NodeType.Scene,
    };
}
