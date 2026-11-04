using System.Buffers;
using System.Text.Json;

namespace OpenIGI.Editor.Core.Graphs.Exec;

/// <summary>A project macro as a file: its name, parameters and body, the body in the same form a graph sidecar keeps actions in.</summary>
public static class ExecMacroJson
{
    private const int Schema = 1;

    /// <summary>Writes a macro.</summary>
    /// <param name="macro">The macro.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Write(ExecMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", Schema);
            json.WriteString("name", macro.Name);
            json.WriteStartArray("parameters");
            foreach (string parameter in macro.Parameters)
            {
                json.WriteStringValue(parameter);
            }

            json.WriteEndArray();
            ExecLayerJson.Write(json, macro.Body);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads a macro written by <see cref="Write"/>.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The macro.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a macro of a schema this editor reads.</exception>
    public static ExecMacro Read(ReadOnlySpan<byte> bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("schema", out JsonElement schema) || schema.GetInt32() != Schema)
        {
            throw new InvalidDataException("Not a macro of a schema this editor reads.");
        }

        ExecLayer body = new();
        ExecLayerJson.Read(root, body);
        return new ExecMacro(
            root.GetProperty("name").GetString() ?? string.Empty,
            [.. root.GetProperty("parameters").EnumerateArray().Select(static p => p.GetString() ?? string.Empty)],
            body);
    }
}
