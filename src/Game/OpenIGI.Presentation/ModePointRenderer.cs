using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>A place a mode plays for, as the world marks it.</summary>
/// <param name="CentreUnits">Its centre on the ground, in world units.</param>
/// <param name="RadiusMetres">Its radius.</param>
/// <param name="Letter">Its letter, 0 for none, 1 for A, 2 for B, 3 for C.</param>
/// <param name="Look">Its colour and state.</param>
public readonly record struct ModePointMark(Vector3d CentreUnits, double RadiusMetres, int Letter, ModePointLook Look);

/// <summary>
/// Draws the places a multiplayer mode plays for -- search and destroy's bomb sites, a planted bomb -- in the world: a lettered
/// ring on the ground the size of the place and a low wall of light standing on it, through <see cref="IGraphicsDevice.DrawModePoint"/>.
/// </summary>
/// <remarks>
/// The mesh is built camera-relative each frame in double, then narrowed, as the tracers' is, so a site 40 km from the origin
/// does not shimmer. A few dozen vertices a point; one draw each.
/// </remarks>
/// <param name="device">The device drawn through.</param>
public sealed class ModePointRenderer(IGraphicsDevice device) : IDisposable
{
    /// <summary>The most points one frame draws.</summary>
    public const int MaximumPoints = 8;

    // The wall's segments round the ring, its height in metres, and how far the disc floats above the ground.
    private const int Segments = 40;
    private const double WallMetres = 2.2;
    private const double LiftMetres = 0.06;
    private const int VerticesPerPoint = 4 + ((Segments + 1) * 2);
    private const int IndicesPerPoint = 6 + (Segments * 6);

    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumPoints * VerticesPerPoint];
    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;

    /// <summary>Gets the points the last frame drew.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Draws the points.</summary>
    /// <param name="points">The points.</param>
    /// <param name="camera">The eye, placed in metres.</param>
    /// <param name="projection">The projection.</param>
    public void Draw(IReadOnlyList<ModePointMark> points, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(points);
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastDrawCount = 0;
        int count = Math.Min(points.Count, MaximumPoints);
        if (count == 0)
        {
            return;
        }

        Vector3d eye = camera.Position;
        Array.Clear(_vertices);
        for (int i = 0; i < count; i++)
        {
            Build(points[i], eye, i * VerticesPerPoint);
        }

        device.SetProjection(projection);
        device.SetCullMode(CullMode.None);
        device.SetDepthState(DepthState.TestOnly);
        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);
        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = device.CreateAnimatedMesh(_vertices, Indices());
            }

            device.UpdateAnimatedMesh(_mesh, _vertices);
            device.ClearPoseInterpolation();
            for (int i = 0; i < count; i++)
            {
                ModePointMark point = points[i];
                ModePointLook look = point.Look with { Letter = point.Letter };
                device.DrawModePoint(_mesh, i * IndicesPerPoint, IndicesPerPoint, modelView, look);
            }

            LastDrawCount = count;
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // Presentation only: a mark that cannot upload this frame is simply not drawn.
            _ = ex;
        }
        finally
        {
            device.SetDepthState(DepthState.TestAndWrite);
        }
    }

    /// <summary>Gets the marks of a layout's bomb sites, and of a planted bomb, as search and destroy shows them.</summary>
    /// <param name="layout">The map's layout.</param>
    /// <param name="report">What the mode last said, or <see langword="null"/>.</param>
    /// <param name="seconds">The time, for the animation.</param>
    /// <returns>The marks.</returns>
    public static IReadOnlyList<ModePointMark> BombSites(MpLayout layout, ModeReport? report, float seconds)
    {
        ArgumentNullException.ThrowIfNull(layout);
        List<ModePointMark> marks = [];
        string planted = report?.Planted ?? string.Empty;
        foreach (MpPoint point in layout.Points)
        {
            if (point.Kind != MpPointKind.BombSite)
            {
                continue;
            }

            bool alarm = planted.Length > 0 && point.Id == planted;
            int letter = point.Id.Length > 0 ? Math.Clamp(char.ToUpperInvariant(point.Id[0]) - 'A' + 1, 0, 3) : 0;
            ModePointLook look = alarm
                ? new ModePointLook(1f, 0.16f, 0.08f, 2.4f, letter, seconds, 1f)
                : new ModePointLook(1f, 0.62f, 0.14f, 1.8f, letter, seconds, 0f);
            marks.Add(new ModePointMark(new Vector3d(point.Position[0], point.Position[1], point.Position[2]), point.Radius / WorldUnits.PerMetre, letter, look));
        }

        if (report?.BombAt is { } bomb)
        {
            marks.Add(new ModePointMark(bomb, new BombSettings().DefuseMetres, 0, new ModePointLook(1f, 0.1f, 0.06f, 3f, 0, seconds, 1f)));
        }

        return marks;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mesh.IsValid)
        {
            device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }
    }

    // A point's disc, a quad flat on the ground the size of the point, and its wall, a strip round the ring; camera-relative.
    private void Build(ModePointMark point, Vector3d eyeMetres, int first)
    {
        Vector3d centre = (point.CentreUnits / WorldUnits.PerMetre) - eyeMetres + new Vector3d(0, 0, LiftMetres);
        double r = point.RadiusMetres;
        Vector3f up = new(0f, 0f, 1f);
        Vector3f At(double x, double y, double z) => new((float)(centre.X + x), (float)(centre.Y + y), (float)(centre.Z + z));

        _vertices[first] = new Vertex3D(At(-r, -r, 0), up, -1f, -1f, 0f, 0f);
        _vertices[first + 1] = new Vertex3D(At(r, -r, 0), up, 1f, -1f, 0f, 0f);
        _vertices[first + 2] = new Vertex3D(At(r, r, 0), up, 1f, 1f, 0f, 0f);
        _vertices[first + 3] = new Vertex3D(At(-r, r, 0), up, -1f, 1f, 0f, 0f);

        // The wall stands just inside the ring's outer edge.
        double wall = r * 0.97;
        for (int s = 0; s <= Segments; s++)
        {
            double angle = s * 2 * Math.PI / Segments;
            double x = Math.Cos(angle) * wall;
            double y = Math.Sin(angle) * wall;
            Vector3f outward = new((float)Math.Cos(angle), (float)Math.Sin(angle), 0f);
            float u = s / (float)Segments;
            _vertices[first + 4 + (s * 2)] = new Vertex3D(At(x, y, 0), outward, u, 0f, 1f, 0f);
            _vertices[first + 4 + (s * 2) + 1] = new Vertex3D(At(x, y, WallMetres), outward, u, 1f, 1f, 0f);
        }
    }

    private static ushort[] Indices()
    {
        ushort[] indices = new ushort[MaximumPoints * IndicesPerPoint];
        int at = 0;
        for (int p = 0; p < MaximumPoints; p++)
        {
            int v = p * VerticesPerPoint;
            foreach (int i in (int[])[0, 1, 2, 0, 2, 3])
            {
                indices[at++] = (ushort)(v + i);
            }

            for (int s = 0; s < Segments; s++)
            {
                int a = v + 4 + (s * 2);
                foreach (int i in (int[])[a, a + 1, a + 3, a, a + 3, a + 2])
                {
                    indices[at++] = (ushort)i;
                }
            }
        }

        return indices;
    }
}
