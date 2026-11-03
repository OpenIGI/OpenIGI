using System.Text.Json;

namespace OpenIGI.Editor.Core.Graphs.Exec;

/// <summary>The action layer's part of a graph sidecar: its nodes, wires, and the task ids each node lowered to.</summary>
public static class ExecLayerJson
{
    /// <summary>Writes a layer as the sidecar's <c>actions</c> object.</summary>
    /// <param name="json">The writer, positioned where the object goes.</param>
    /// <param name="layer">The layer.</param>
    public static void Write(Utf8JsonWriter json, ExecLayer layer)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(layer);
        json.WriteStartObject("actions");
        json.WriteStartArray("nodes");
        foreach (ExecNode node in layer.Nodes)
        {
            json.WriteStartObject();
            json.WriteString("id", node.Id);
            json.WriteString("kind", node.Kind.ToString());
            WriteIfAny(json, "condition", node.Condition);
            WriteIfAny(json, "call", node.Call);
            WriteIfAny(json, "pin", node.TargetPin);
            WriteIfAny(json, "macro", node.Macro);
            if (node.Arguments.Count > 0)
            {
                json.WriteStartObject("arguments");
                foreach ((string name, string value) in node.Arguments.OrderBy(static a => a.Key, StringComparer.Ordinal))
                {
                    json.WriteString(name, value);
                }

                json.WriteEndObject();
            }
            if (node.Target is { } target)
            {
                json.WriteString("target", target.Value);
            }

            if (node.Seconds != 0)
            {
                json.WriteNumber("seconds", node.Seconds);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("wires");
        foreach (ExecWire wire in layer.Wires)
        {
            json.WriteStartArray();
            json.WriteStringValue(wire.From);
            json.WriteStringValue(wire.Pin);
            json.WriteStringValue(wire.To);
            json.WriteEndArray();
        }

        json.WriteEndArray();
        json.WriteStartObject("generated");
        foreach ((string node, List<int> ids) in layer.Generated.OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            json.WriteStartArray(node);
            foreach (int id in ids)
            {
                json.WriteNumberValue(id);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>Reads the sidecar's <c>actions</c> object into a layer.</summary>
    /// <param name="root">The sidecar's root object.</param>
    /// <param name="layer">The layer to fill.</param>
    /// <exception cref="InvalidDataException">A node names a kind this editor does not have.</exception>
    public static void Read(JsonElement root, ExecLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (!root.TryGetProperty("actions", out JsonElement actions))
        {
            return;
        }

        foreach (JsonElement node in actions.GetProperty("nodes").EnumerateArray())
        {
            string kind = node.GetProperty("kind").GetString() ?? string.Empty;
            layer.Nodes.Add(new ExecNode(
                node.GetProperty("id").GetString() ?? string.Empty,
                Enum.TryParse(kind, out ExecNodeKind parsed) ? parsed : throw new InvalidDataException($"Unknown action kind '{kind}'."))
            {
                Condition = Text(node, "condition"),
                Call = Text(node, "call"),
                TargetPin = Text(node, "pin"),
                Macro = Text(node, "macro"),
                Arguments = node.TryGetProperty("arguments", out JsonElement arguments)
                    ? arguments.EnumerateObject().ToDictionary(static a => a.Name, static a => a.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                    : ExecNode.NoArguments,
                Target = node.TryGetProperty("target", out JsonElement target) ? new GraphNodeKey(target.GetString() ?? string.Empty) : null,
                Seconds = node.TryGetProperty("seconds", out JsonElement seconds) ? seconds.GetDouble() : 0,
            });
        }

        foreach (JsonElement wire in actions.GetProperty("wires").EnumerateArray())
        {
            layer.Wires.Add(new ExecWire(wire[0].GetString() ?? string.Empty, wire[1].GetString() ?? string.Empty, wire[2].GetString() ?? string.Empty));
        }

        foreach (JsonProperty node in actions.GetProperty("generated").EnumerateObject())
        {
            layer.Generated[node.Name] = [.. node.Value.EnumerateArray().Select(static id => id.GetInt32())];
        }
    }

    private static void WriteIfAny(Utf8JsonWriter json, string name, string value)
    {
        if (value.Length > 0)
        {
            json.WriteString(name, value);
        }
    }

    private static string Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
}
