using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Viewport;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A small picture of a model for the content browser's tiles: textured as the running level binds it and seen as the
/// model preview first shows it, on a clear background. Drawn on the CPU with the preview's raster, so a tile needs no
/// render target and looks like the preview.
/// </summary>
internal static class ModelThumbnail
{
    // The model preview's opening view.
    private const float Yaw = 0.6f;
    private const float Pitch = 0.3f;

    /// <summary>Draws a model.</summary>
    /// <param name="model">The model.</param>
    /// <param name="size">The picture's width and height, in pixels.</param>
    /// <param name="texture">The texture a draw record samples, or null for flat grey.</param>
    /// <param name="animator">The model's rig, to stand a skinned model up in, or null for one drawn as stored.</param>
    /// <returns>The pixels, row by row, or <see langword="null"/> for a model with no faces.</returns>
    public static Bgra32[]? Render(MefModel model, int size, Func<MefDrawRecord, RasterTexture?> texture, IModelAnimator? animator = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(texture);
        ReadOnlySpan<MefVertex> source = model.Vertices;
        if (source.Length == 0 || model.TriangleCount == 0)
        {
            return null;
        }

        Vertex3D[] vertices = new Vertex3D[source.Length];
        Vector3f min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3f max = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < source.Length; i++)
        {
            MefVertex v = source[i];
            vertices[i] = new Vertex3D(v.Position, v.Normal, v.DiffuseU, v.DiffuseV);
        }

        // A skinned model's vertices sit about their bones until posed: it is drawn standing, as the preview opens on it.
        List<(int A, int B, int C)>[] parts = [.. model.DrawRecords.Select(r => model.Triangles(r).ToList())];
        if (animator is not null && ModelPoses.Standing(animator, vertices, parts.SelectMany(static t => t)) is int clip and >= 0)
        {
            animator.Pose(clip, 0f, vertices);
        }

        foreach (Vertex3D v in vertices)
        {
            min = new Vector3f(MathF.Min(min.X, v.Position.X), MathF.Min(min.Y, v.Position.Y), MathF.Min(min.Z, v.Position.Z));
            max = new Vector3f(MathF.Max(max.X, v.Position.X), MathF.Max(max.Y, v.Position.Y), MathF.Max(max.Z, v.Position.Z));
        }

        // The preview's orbit camera: turn about Z, tilt, and stand back far enough to fit the model.
        Vector3f centre = (min + max) * 0.5f;
        float radius = MathF.Max(1e-3f, ((max - min) * 0.5f).Length);
        Matrix3f view = Matrix3f.RotationZ(-Yaw) * Matrix3f.RotationX(-Pitch);
        Vector3f eye = view.Transform(centre) + new Vector3f(0f, -radius * 2.6f, 0f);
        float focal = size * 1.2f;

        ModelRaster raster = new();
        raster.Begin(size, size, default);
        for (int r = 0; r < parts.Length; r++)
        {
            raster.Draw(vertices, parts[r], texture(model.DrawRecords[r]), view, eye, focal);
        }

        return [.. raster.Pixels];
    }
}

/// <summary>Poses for a rigged model.</summary>
internal static class ModelPoses
{
    /// <summary>
    /// Finds the clip to show a model in: the one it stands in where the game places it, else the one whose first frame stands
    /// tallest, as a rig's clips start with deaths and prone poses as often as with standing ones.
    /// </summary>
    /// <param name="animator">The model's rig.</param>
    /// <param name="rest">The model's vertices as stored; left as they were.</param>
    /// <param name="triangles">The triangles drawn, so vertices no face uses do not count.</param>
    /// <returns>The clip's index, or -1 when the rig has none.</returns>
    public static int Standing(IModelAnimator animator, ReadOnlySpan<Vertex3D> rest, IEnumerable<(int A, int B, int C)> triangles)
    {
        if (animator.StandClip >= 0)
        {
            return animator.StandClip;
        }

        int count = rest.Length;
        int[] drawn = [.. triangles.SelectMany(static t => (int[])[t.A, t.B, t.C]).Distinct().Where(i => (uint)i < (uint)count)];
        Vertex3D[] posed = new Vertex3D[rest.Length];
        int best = -1;
        float tallest = float.MinValue;
        for (int clip = 0; clip < animator.Clips.Count; clip++)
        {
            rest.CopyTo(posed);
            animator.Pose(clip, 0f, posed);
            float low = float.MaxValue, high = float.MinValue;
            foreach (int index in drawn)
            {
                low = MathF.Min(low, posed[index].Position.Z);
                high = MathF.Max(high, posed[index].Position.Z);
            }

            if (high - low > tallest + 1e-3f)
            {
                tallest = high - low;
                best = clip;
            }
        }

        return best;
    }
}

/// <summary>Reads the textures the running level gives a model's parts, for the model preview and the browser's tiles.</summary>
internal static class ModelTextures
{
    /// <summary>Reads the texture one of a model's draw records samples.</summary>
    /// <param name="host">The running game.</param>
    /// <param name="model">The model's name, as level data spells it (<c>435_01_1</c>).</param>
    /// <param name="record">The draw record.</param>
    /// <param name="largestEdge">The largest width or height wanted: the smallest stored level at least this big is used.</param>
    /// <returns>The texture, or null when the record has none or the level does not know the model.</returns>
    public static RasterTexture? Read(IEditorHost host, string model, MefDrawRecord record, int largestEdge = int.MaxValue)
    {
        if (record.MaterialSlot == MefDrawRecord.NoMaterialSlot || !host.TryReadModelTexture(model, record.MaterialSlot, out ReadOnlyMemory<byte> bytes))
        {
            return null;
        }

        try
        {
            if (!ImageSniffer.TryDecode(bytes.Span, out TextureImage? picture) || picture is null)
            {
                return null;
            }

            int level = 0;
            while (level + 1 < picture.MipLevels && Math.Max(picture.GetLevelSize(level + 1).Width, picture.GetLevelSize(level + 1).Height) >= largestEdge)
            {
                level++;
            }

            (int width, int height) = picture.GetLevelSize(level);
            return new RasterTexture(width, height, [.. picture.GetLevel(level)], record.IsTransparent);
        }
        catch (TextureFormatException)
        {
            return null;
        }
    }
}
