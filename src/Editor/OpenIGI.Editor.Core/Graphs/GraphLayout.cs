using System.Buffers;
using System.Text;
using System.Text.Json;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>A point on the graph canvas, in canvas units.</summary>
/// <param name="X">Rightwards.</param>
/// <param name="Y">Downwards.</param>
public readonly record struct GraphPoint(double X, double Y);

/// <summary>A rectangle on the graph canvas.</summary>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
public readonly record struct GraphRect(double X, double Y, double Width, double Height);

/// <summary>A note on the canvas, which moves the nodes inside it when dragged by its title.</summary>
/// <param name="Id">Its identity, stable across saves.</param>
/// <param name="Text">What it says.</param>
/// <param name="Bounds">Where it is.</param>
/// <param name="Colour">Its tint, as <c>0xRRGGBB</c>.</param>
public sealed record GraphComment(string Id, string Text, GraphRect Bounds, uint Colour);

/// <summary>Everything about a graph the game never reads: where its nodes sit, its comments, which nodes are folded.</summary>
/// <remarks>
/// <para>
/// Kept in the project's <c>graphs</c> folder beside the script, never in it (<see cref="Projects.EditorProject.GraphPathFor"/>).
/// A node with no position is placed by <see cref="GraphAutoLayout"/>, so a mission opened for the first time, or a script
/// written without the graph, still reads.
/// </para>
/// <para>
/// Expression nodes are keyed by their place in their parameter's text, so the layout remembers a hash of each parameter's
/// text: when the text has changed outside the graph, <see cref="Forget"/> drops that parameter's positions rather than
/// putting stale ones on different nodes.
/// </para>
/// </remarks>
public sealed class GraphLayout
{
    private const int Schema = 1;

    /// <summary>Raised after a command changes the layout.</summary>
    public event Action? Changed;

    /// <summary>Gets each node's position, by key.</summary>
    public Dictionary<GraphNodeKey, GraphPoint> Positions { get; } = [];

    /// <summary>Gets the size each frame was given by hand, by its container task's key; a frame not here fits its contents.</summary>
    public Dictionary<GraphNodeKey, GraphPoint> FrameSizes { get; } = [];

    /// <summary>Gets the nodes drawn folded to their title.</summary>
    public HashSet<GraphNodeKey> Collapsed { get; } = [];

    /// <summary>Gets the comments.</summary>
    public List<GraphComment> Comments { get; } = [];

    /// <summary>Gets the hash of each expression parameter's text when its node positions were saved, by <c>task/parameter</c>.</summary>
    public Dictionary<string, ulong> TextHashes { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the graph's action nodes, which compile into the mission as generated tasks.</summary>
    public Exec.ExecLayer Actions { get; } = new();

    /// <summary>Tells listeners the layout changed; called by the commands that change it.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>The hash a parameter's text is remembered by.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Its 64-bit FNV-1a hash over UTF-8.</returns>
    public static ulong HashOf(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text ?? string.Empty))
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Drops the positions of expression nodes whose parameter's text is no longer the text they were saved for.</summary>
    /// <param name="graph">The graph as built now.</param>
    /// <param name="currentText">Each expression parameter's text now, by <c>task/parameter</c>.</param>
    /// <returns>How many parameters were forgotten.</returns>
    public int Forget(MissionGraph graph, IReadOnlyDictionary<string, string> currentText)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(currentText);
        int forgotten = 0;
        foreach ((string owner, ulong hash) in TextHashes.ToList())
        {
            if (!currentText.TryGetValue(owner, out string? text) || HashOf(text) != hash)
            {
                string prefix = owner + "/";
                foreach (GraphNodeKey key in Positions.Keys.Where(k => k.Value.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                {
                    Positions.Remove(key);
                }

                TextHashes.Remove(owner);
                forgotten++;
            }
        }

        return forgotten;
    }

    /// <summary>Writes the layout as the sidecar's JSON.</summary>
    /// <returns>The bytes.</returns>
    public byte[] Write()
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", Schema);
            json.WriteStartObject("nodes");
            foreach ((GraphNodeKey key, GraphPoint at) in Positions.OrderBy(static p => p.Key.Value, StringComparer.Ordinal))
            {
                json.WriteStartArray(key.Value);
                json.WriteNumberValue(Math.Round(at.X, 1));
                json.WriteNumberValue(Math.Round(at.Y, 1));
                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteStartObject("frames");
            foreach ((GraphNodeKey key, GraphPoint size) in FrameSizes.OrderBy(static p => p.Key.Value, StringComparer.Ordinal))
            {
                json.WriteStartArray(key.Value);
                json.WriteNumberValue(Math.Round(size.X, 1));
                json.WriteNumberValue(Math.Round(size.Y, 1));
                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteStartArray("collapsed");
            foreach (GraphNodeKey key in Collapsed.OrderBy(static k => k.Value, StringComparer.Ordinal))
            {
                json.WriteStringValue(key.Value);
            }

            json.WriteEndArray();
            json.WriteStartArray("comments");
            foreach (GraphComment comment in Comments)
            {
                json.WriteStartObject();
                json.WriteString("id", comment.Id);
                json.WriteString("text", comment.Text);
                json.WriteNumber("x", comment.Bounds.X);
                json.WriteNumber("y", comment.Bounds.Y);
                json.WriteNumber("w", comment.Bounds.Width);
                json.WriteNumber("h", comment.Bounds.Height);
                json.WriteString("colour", comment.Colour.ToString("X6", System.Globalization.CultureInfo.InvariantCulture));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            Exec.ExecLayerJson.Write(json, Actions);
            json.WriteStartObject("text");
            foreach ((string owner, ulong hash) in TextHashes.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                json.WriteString(owner, hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads a sidecar written by <see cref="Write"/>.</summary>
    /// <param name="bytes">The sidecar's bytes.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a graph layout this editor writes.</exception>
    public static GraphLayout Read(ReadOnlySpan<byte> bytes)
    {
        GraphLayout layout = new();
        using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("schema", out JsonElement schema) || schema.GetInt32() != Schema)
        {
            throw new InvalidDataException("Not a graph layout of a schema this editor reads.");
        }

        foreach (JsonProperty node in Property(root, "nodes"))
        {
            layout.Positions[new GraphNodeKey(node.Name)] = new GraphPoint(node.Value[0].GetDouble(), node.Value[1].GetDouble());
        }

        foreach (JsonProperty frame in Property(root, "frames"))
        {
            layout.FrameSizes[new GraphNodeKey(frame.Name)] = new GraphPoint(frame.Value[0].GetDouble(), frame.Value[1].GetDouble());
        }

        if (root.TryGetProperty("collapsed", out JsonElement collapsed))
        {
            foreach (JsonElement key in collapsed.EnumerateArray())
            {
                layout.Collapsed.Add(new GraphNodeKey(key.GetString() ?? string.Empty));
            }
        }

        if (root.TryGetProperty("comments", out JsonElement comments))
        {
            foreach (JsonElement c in comments.EnumerateArray())
            {
                layout.Comments.Add(new GraphComment(
                    c.GetProperty("id").GetString() ?? string.Empty,
                    c.GetProperty("text").GetString() ?? string.Empty,
                    new GraphRect(c.GetProperty("x").GetDouble(), c.GetProperty("y").GetDouble(), c.GetProperty("w").GetDouble(), c.GetProperty("h").GetDouble()),
                    uint.Parse(c.GetProperty("colour").GetString() ?? "0", System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        Exec.ExecLayerJson.Read(root, layout.Actions);
        foreach (JsonProperty text in Property(root, "text"))
        {
            layout.TextHashes[text.Name] = ulong.Parse(
                text.Value.GetString() ?? "0", System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        }

        return layout;
    }

    private static List<JsonProperty> Property(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? [.. value.EnumerateObject()] : [];
}
