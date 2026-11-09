using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// Draws each burning blast through the blast volume shader (<see cref="IGraphicsDevice.DrawBlastVolume"/>): a cube about it
/// whose far faces march the fireball and the column and bend the picture where the shock front passes.
/// </summary>
/// <remarks>
/// One unit cube, made once and scaled to each blast at its draw, so a blast whose front has gone shrinks its box to its
/// column and stops costing the pixels the front reached. At quality one the cube still draws, with no fire and no smoke:
/// the shock front alone, which is cheap; at two the volume too.
/// </remarks>
/// <param name="device">The device it draws with.</param>
public sealed class BlastVolumeRenderer(IGraphicsDevice device) : IDisposable
{
    // The march's steps at the volume's quality.
    private const int VolumeSteps = 36;

    // How brightly the fire glows, against the exposure the resolve applies.
    private const float FireGlow = 1.6f;

    private MeshHandle _cube;
    private bool _disposed;

    /// <summary>Draws every blast still burning.</summary>
    /// <param name="blasts">The blasts.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    public void Draw(IReadOnlyList<BombBlast> blasts, in Camera3D camera, in Matrix4f projection, float blend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int quality = BombBlastEffects.Quality;
        if (blasts.Count == 0 || quality <= 0)
        {
            return;
        }

        if (!_cube.IsValid)
        {
            _cube = device.CreateMesh(CubeVertices(), CubeIndices);
        }

        device.SetProjection(projection);
        Matrix3f worldToView = camera.Orientation.Transposed();
        foreach (BombBlast blast in blasts)
        {
            float age = blast.Age(blend);
            bool volume = quality >= 2;
            Vector3f relative = new(
                (float)(blast.CentreMetres.X - camera.Position.X),
                (float)(blast.CentreMetres.Y - camera.Position.Y),
                (float)(blast.CentreMetres.Z - camera.Position.Z));
            float shock = BombBlast.ShockStrength(age);
            float heat = volume ? BombBlast.FireballHeat(age) : 0f;
            float smoke = volume ? BombBlast.SmokeDensity(age) : 0f;
            if (shock <= 0.001f && heat <= 0.001f && smoke <= 0.001f)
            {
                continue;
            }

            var look = new BlastVolumeLook(
                worldToView.Transform(relative),
                blast.RadiusMetres,
                age,
                blast.Seed,
                blast.FireballRadiusMetres(age),
                heat,
                blast.ColumnHeightMetres(age),
                smoke,
                blast.ShockRadiusMetres(age),
                shock,
                ColourTemperature.ToLinearRgb(BombBlast.LightKelvin(age)),
                FireGlow * MathF.Max(heat, 0.05f),
                VolumeSteps);

            // The cube the shader keeps the far face of, the size it reckons from the same look.
            float half = look.BoxHalfSize;
            Matrix3f scale = Matrix3f.FromRows(new Vector3f(half, 0f, 0f), new Vector3f(0f, half, 0f), new Vector3f(0f, 0f, half));
            Matrix4f modelView = camera.CreateModelView(blast.CentreMetres, scale);
            device.DrawBlastVolume(_cube, 0, CubeIndices.Length, modelView, look);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_cube.IsValid)
        {
            device.DestroyMesh(_cube);
            _cube = MeshHandle.None;
        }
    }

    // The unit cube's corners; the faces carry no normals or uvs the shader reads.
    private static Vertex3D[] CubeVertices()
    {
        var vertices = new Vertex3D[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3f corner = new((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f);
            vertices[i] = new Vertex3D(corner, corner, 0f, 0f, 0f, 0f);
        }

        return vertices;
    }

    private static readonly ushort[] CubeIndices =
    [
        0, 2, 1, 1, 2, 3, // -z
        4, 5, 6, 5, 7, 6, // +z
        0, 1, 4, 1, 5, 4, // -y
        2, 6, 3, 3, 6, 7, // +y
        0, 4, 2, 2, 4, 6, // -x
        1, 3, 5, 3, 7, 5, // +x
    ];
}
