using System.Globalization;
using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Host.Editor;

namespace OpenIGI.Host;

/// <content>
/// The textured level of <c>mp_mapshot</c>, for the layout page to draw from straight above at any zoom: the
/// <see cref="OverheadScene"/> the editor's map draws, written as one mesh, its textures and a manifest in
/// <c>&lt;path&gt;.mesh/</c>.
/// </content>
public sealed partial class GameHost
{
    // Writes the scene's mesh; a problem, or null once written.
    private string? WriteMapMesh(string path, OverheadScene scene)
    {
        string folder = $"{path}.mesh";
        Directory.CreateDirectory(Path.Combine(folder, "textures"));
        using (FileStream file = File.Create(Path.Combine(folder, "mesh.json")))
        using (Utf8JsonWriter json = new(file, new JsonWriterOptions { Indented = true }))
        using (BinaryWriter raw = new(File.Create(Path.Combine(folder, "mesh.bin"))))
        {
            json.WriteStartObject();
            json.WriteNumber("stride", OverheadScene.Stride);
            json.WriteStartArray("textures");
            for (int i = 0; i < scene.Textures.Count; i++)
            {
                OverheadTexture texture = scene.Textures[i];
                string name = string.Create(CultureInfo.InvariantCulture, $"textures/{i}.png");
                PngWriter.Write(Path.Combine(folder, name), texture.Pixels, texture.Width, texture.Height);
                json.WriteStartObject();
                json.WriteString("file", name);
                json.WriteBoolean("repeat", texture.Repeat);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("groups");
            long offset = 0;
            foreach (OverheadGroup group in scene.Groups)
            {
                json.WriteStartObject();
                json.WriteNumber("kind", (int)group.Pass);
                json.WriteNumber("texture", group.Texture);
                json.WriteNumber("first", offset);
                json.WriteNumber("count", group.Corners.Length / OverheadScene.Stride);
                json.WriteEndObject();
                foreach (float f in group.Corners)
                {
                    raw.Write(f);
                }

                offset += group.Corners.Length / OverheadScene.Stride;
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        int faces = scene.Groups.Where(static g => g.Pass >= OverheadPass.Solid).Sum(static g => g.Corners.Length) / (3 * OverheadScene.Stride);
        _logger.Information("Desktop", $"Wrote the overhead mesh {folder}: {faces} model faces, ground {(scene.HasGround ? "drawn" : "missing")}.");
        return null;
    }
}
