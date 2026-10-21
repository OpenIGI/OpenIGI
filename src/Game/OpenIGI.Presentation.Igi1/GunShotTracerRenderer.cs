using System.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Draws the tracers the guns' <c>GunShot</c> tasks file, through the tracer shader: the original's thin amber line
/// under the vanilla preset, the remaster's glowing streak under every other.
/// </summary>
/// <remarks>
/// <para>
/// <b>The retail line</b> is the task's render, <c>0x47ABD0</c>. Each live tracer is one pass of a round's march. Its
/// two ends take the ammunition's colours at their distances along the round's range
/// (<see cref="AmmoTracer.ColourAt"/>), and their brightness is that same fraction, faded by the end's depth in view
/// and floored at a fifth (<see cref="AmmoTracer.IntensityAt"/>). When both ends are on the same side of the eye the
/// line goes through <c>0x4E0E20</c>: clipped a tenth of a metre in front of the eye and submitted as a one-pixel
/// line strip whose vertex alpha is the brightness, source-alpha blended, depth-tested without writing depth, and
/// with Direct3D's fog switched off (<c>0x50D4C0</c>, <c>0x50DCC0</c>, <c>0x50E000</c>). The port draws it as a
/// camera-facing strip <see cref="LineWidthPixels"/> wide in the image it is rasterised into. When the ends
/// straddle the eye -- a round flying past the player -- <c>0x47C610</c> builds a world-space wedge instead: the two
/// ends and a third point <see cref="WedgeWidthUnits"/> across, level, at alphas 0, one half and one half (a round
/// from behind puts the third point at its start, at alpha 0). Drawing a tracer spends it: <c>0x47ABD0</c> zeroes its
/// life and the task frees it on its next tick, so the port draws it every frame until then.
/// </para>
/// <para>
/// <b>The remastered streak</b> is a port addition, presentation only: the simulation files, spends and frees its
/// tracers exactly as above whatever the preset. Each tracer this renderer has not seen before starts a streak of
/// its own that flies from the start of its pass to its end at <see cref="StreakSpeedMetresPerSecond"/>, a
/// <see cref="StreakLengthMetres"/> trail behind a hot head, so a round fired at the player is seen leaving the
/// muzzle and coming in rather than flashing for one frame. It is light: the shader adds it in linear radiance at
/// <see cref="StreakRadiance"/>, which the bloom picks up.
/// </para>
/// </remarks>
public sealed class GunShotTracerRenderer : IDisposable
{
    /// <summary>The depth in front of the eye a line is clipped at, in world units.</summary>
    /// <remarks><c>0x4E0F0F</c>'s <c>409.60001</c>: a tenth of a metre.</remarks>
    public const float NearClipUnits = 409.60001f;

    /// <summary>How far across the wedge's third point sits from its end, in world units.</summary>
    /// <remarks><c>0x47C780</c>'s <c>286.72</c>: seven centimetres.</remarks>
    public const float WedgeWidthUnits = 286.72f;

    /// <summary>The alpha the wedge's far end carries (<c>0x47C87D</c>).</summary>
    public const float WedgeAlpha = 0.5f;

    /// <summary>How wide the retail line is drawn, in pixels of the image it is rasterised into.</summary>
    /// <remarks>A Direct3D 7 line primitive is one pixel wide, whatever the resolution.</remarks>
    public const float LineWidthPixels = 1f;

    /// <summary>The picture height at which <see cref="StreakWidthPixels"/> applies; the streak scales from there.</summary>
    public const float ReferenceHeight = 768f;

    /// <summary>The thinnest the remastered streak is drawn, in pixels of the image it is rasterised into.</summary>
    /// <remarks>
    /// A port decision. Under a temporal upscaler the scene is drawn below the window's resolution with a sub-pixel
    /// jitter, and a strip under one of those pixels wide covers some pixel centres and misses others: the line came
    /// out as dashes. One and a half keeps it whole at any jitter.
    /// </remarks>
    public const float MinimumRenderPixels = 1.5f;

    /// <summary>How fast a remastered streak flies, in metres a second.</summary>
    /// <remarks>
    /// Slower than a real round, deliberately: at a rifle's 700 m/s a round crosses a firefight's twenty metres
    /// inside one frame and is never seen to move. This is fast enough to read as a bullet and slow enough to be
    /// seen coming.
    /// </remarks>
    public const float StreakSpeedMetresPerSecond = 380f;

    /// <summary>How long a remastered streak's trail is at the least, in metres.</summary>
    /// <remarks>
    /// The trail is stretched past this to cover how far the round moved since the last frame -- a full-frame
    /// shutter -- so at 60 fps, where a streak moves 6.3 m a frame, it reads as one unbroken streak rather than a
    /// row of separate dashes.
    /// </remarks>
    public const float StreakLengthMetres = 5f;

    /// <summary>The longest frame the shutter stretches a streak over, in seconds.</summary>
    /// <remarks>A hitch -- a load, a stall -- would otherwise smear one frame's streak down the whole of its flight.</remarks>
    public const double MaximumShutterSeconds = 0.05;

    /// <summary>The least a stretched streak's light is dimmed to, as a share of an unstretched one's.</summary>
    /// <remarks>
    /// A blurred streak spreads the same light over more of its length, so it is dimmed by how much longer it is --
    /// but never below this, or a streak at a low frame rate would fade to nothing.
    /// </remarks>
    public const float MinimumSmearBrightness = 0.5f;

    /// <summary>How wide a remastered streak's halo is, in pixels at <see cref="ReferenceHeight"/>.</summary>
    /// <remarks>A hairline: the hot core is about half of it, the rest a faint glow falling away.</remarks>
    public const float StreakWidthPixels = 2.5f;

    /// <summary>The remastered streak's head radiance, in the scene's linear units before exposure.</summary>
    public const float StreakRadiance = 1.6f;

    /// <summary>The most quads one frame may draw.</summary>
    public const int MaximumQuads = 512;

    /// <summary>The most remastered streaks in flight at once; past it the oldest are dropped.</summary>
    public const int MaximumStreaks = 256;

    private const int MaximumVertices = MaximumQuads * 4;
    private const double UnitsPerMetre = WorldScene.UnitsPerMetre;

    private readonly IGraphicsDevice _device;
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];
    private readonly HashSet<GunShotTracer> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<GunShotTracer> _live = new(ReferenceEqualityComparer.Instance);
    private readonly List<Streak> _streaks = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastStreakFrame = double.NaN;
    private MeshHandle _mesh = MeshHandle.None;
    private int _used;
    private int _lastUsed;
    private double _halfWidthPerMetre;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="GunShotTracerRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public GunShotTracerRenderer(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Gets how many strips the last draw submitted.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Gets how many remastered streaks are in flight.</summary>
    public int StreaksInFlight => _streaks.Count;

    /// <summary>Draws the tracers and spends each one, as <c>0x47ABD0</c> does.</summary>
    /// <param name="tracers">The live tracers.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="renderHeight">
    /// The height of the image the scene is rasterised into: the render resolution under an upscaler, the window's
    /// otherwise.
    /// </param>
    /// <param name="style">The original's line, or the remaster's streak.</param>
    /// <param name="muzzle">
    /// Where a shooter's gun muzzle is drawn this frame, in world units, or null when it has none. A round's first pass
    /// is drawn from there rather than from the point the simulation fires it from.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="tracers"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The simulation fires a soldier's round from its view point (<c>0x465820</c>: the origin raised 0.35 m or 0.7 m),
    /// which lies inside the body; drawn from there, the line came out of the soldier's stomach. Presentation only:
    /// the round still flew, and struck, from where the simulation put it.
    /// </remarks>
    public void Draw(
        IEnumerable<GunShotTracer> tracers,
        in Camera3D camera,
        in Matrix4f projection,
        int renderHeight,
        TracerStyle style,
        Func<object?, Vector3d?>? muzzle = null)
    {
        ArgumentNullException.ThrowIfNull(tracers);
        ObjectDisposedException.ThrowIf(_disposed, this);

        LastDrawCount = 0;
        _used = 0;

        // Half a strip's width, in metres, per metre of depth: the pixel width over the pixels a metre covers at one
        // metre (the projection's vertical scale times half the image's height).
        float height = Math.Max(1, renderHeight);
        float pixels = style == TracerStyle.Retail
            ? LineWidthPixels
            : MathF.Max(MinimumRenderPixels, StreakWidthPixels * height / ReferenceHeight);
        float metrePixels = MathF.Abs(projection.M22) * height * 0.5f;
        _halfWidthPerMetre = metrePixels > 0f ? pixels * 0.5 / metrePixels : 0.0;

        // The camera is placed in metres and the rounds in world units: the eye goes into units, and every point is
        // made camera-relative there, in double, before it is narrowed.
        Vector3d eye = camera.Position * UnitsPerMetre;
        Vector3f forward = camera.Orientation.Row3;
        double now = _clock.Elapsed.TotalSeconds;

        _live.Clear();
        foreach (GunShotTracer tracer in tracers)
        {
            _live.Add(tracer);

            // A round's first pass leaves the gun, not the body.
            Vector3d start = tracer.StartDistance == 0f && muzzle?.Invoke(tracer.Shooter) is { } gun ? gun : tracer.Start;
            if (style == TracerStyle.Retail)
            {
                AddRetail(tracer, start, eye, forward);
            }
            else if (_seen.Add(tracer))
            {
                StartStreak(tracer, start, now);
            }

            // 0x47B0A4: drawn, so freed on the task's next tick.
            tracer.Life = 0;
        }

        // Forget the tracers the simulation has freed, so the set does not grow.
        _seen.IntersectWith(_live);

        if (style == TracerStyle.Remastered)
        {
            AddStreaks(eye, forward, now);
        }
        else
        {
            _streaks.Clear();
        }

        if (_used == 0)
        {
            _lastUsed = 0;
            return;
        }

        if (_lastUsed > _used)
        {
            Array.Clear(_vertices, _used, _lastUsed - _used);
        }

        _lastUsed = _used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);

        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);
        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, BuildQuadIndices());
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);
            _device.ClearPoseInterpolation();
            _device.DrawTracers(_mesh, 0, _used / 4 * 6, modelView, style, StreakRadiance);
            LastDrawCount = _used / 4;
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // Presentation only: a tracer that cannot upload this frame is simply not drawn.
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
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
        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }
    }

    // ---- The retail line --------------------------------------------------------------------------------------

    private void AddRetail(GunShotTracer tracer, Vector3d from, Vector3d eye, Vector3f forward)
    {
        if (_used + 4 > MaximumVertices || !(tracer.Range > 0f))
        {
            return;
        }

        // 0x47AC65: each end's fraction of the range, which is both its place in the colour ramp and its brightness
        // before the depth fade.
        float startFraction = tracer.StartDistance / tracer.Range;
        float endFraction = tracer.EndDistance / tracer.Range;
        (float R, float G, float B) startColour = AmmoTracer.ColourAt(tracer.Ammunition, startFraction);
        (float R, float G, float B) endColour = AmmoTracer.ColourAt(tracer.Ammunition, endFraction);

        // Camera-relative in double, then the depth along the view (0x497F20's third row).
        Vector3d start = from - eye;
        Vector3d end = tracer.End - eye;
        double startDepth = Depth(start, forward);
        double endDepth = Depth(end, forward);

        if (startDepth < 0.0 == endDepth < 0.0)
        {
            float startAlpha = AmmoTracer.IntensityAt(Math.Clamp(startFraction, 0f, 1f), (float)startDepth);
            float endAlpha = AmmoTracer.IntensityAt(Math.Clamp(endFraction, 0f, 1f), (float)endDepth);
            AddStrip(start, startDepth, Tint(startColour, startAlpha), end, endDepth, Tint(endColour, endAlpha));
        }
        else
        {
            AddWedge(start, end, startDepth > 0.0, Tint(startColour, 0f), Tint(endColour, WedgeAlpha), endColour);
        }
    }

    // 0x47C610: the two ends and a third point seven centimetres across, level. The third point sits by the end when
    // the start is in front of the eye, by the start otherwise. Alphas: start 0, end 1/2, third 1/2 beside the end and
    // 0 beside the start; the end and the third point take the end's colour. The hardware clips what is behind the
    // eye.
    private void AddWedge(
        Vector3d start,
        Vector3d end,
        bool fromInFront,
        Bgra32 startColour,
        Bgra32 endColour,
        (float R, float G, float B) farColour)
    {
        // 0x47C692: the level perpendicular (-dy, dx, 0) of the pass.
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double across = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(across > 1e-9) || _used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d offset = new(-dy / across * WedgeWidthUnits, dx / across * WedgeWidthUnits, 0.0);
        Vector3d third = (fromInFront ? end : start) + offset;
        Bgra32 thirdColour = Tint(farColour, fromInFront ? WedgeAlpha : 0f);

        Vector3f ps = Metres(start);
        Vector3f pe = Metres(end);
        Vector3f pt = Metres(third);
        if (!IsFinite(ps) || !IsFinite(pe) || !IsFinite(pt))
        {
            return;
        }

        // 0x49A6E0 draws the wedge with D3DRS_CULLMODE = D3DCULL_CW: only a triangle that winds counter-clockwise as
        // the eye sees it -- its (end - start) x (third - start) normal towards the eye -- is drawn at all. The eye is
        // the origin here, so the test is the normal against the start's direction back to it.
        Vector3f facing = Vector3f.Cross(pe - ps, pt - ps);
        if (!((facing.X * -ps.X) + (facing.Y * -ps.Y) + (facing.Z * -ps.Z) > 0f))
        {
            return;
        }

        Vector3f normal = new(0f, 0f, 1f);
        _vertices[_used] = new Vertex3D(ps, normal, 0f, 0.5f, colour: startColour);
        _vertices[_used + 1] = new Vertex3D(pe, normal, 1f, 0.5f, colour: endColour);
        _vertices[_used + 2] = new Vertex3D(pt, normal, 1f, 0.5f, colour: thirdColour);
        _vertices[_used + 3] = _vertices[_used + 2];
        _used += 4;
    }

    // ---- The remastered streak --------------------------------------------------------------------------------

    private void StartStreak(GunShotTracer tracer, Vector3d from, double now)
    {
        if (!(tracer.EndDistance > tracer.StartDistance))
        {
            return;
        }

        if (_streaks.Count >= MaximumStreaks)
        {
            _streaks.RemoveAt(0);
        }

        _streaks.Add(new Streak(from, tracer.End, tracer.StartDistance, tracer.EndDistance, tracer.Range, tracer.Ammunition, now));
    }

    private void AddStreaks(Vector3d eye, Vector3f forward, double now)
    {
        double speed = StreakSpeedMetresPerSecond * UnitsPerMetre;

        // The shutter: as far as a streak moved since the last frame, so consecutive frames' trails meet end to end.
        double shutter = double.IsNaN(_lastStreakFrame) ? 0.0 : Math.Clamp(now - _lastStreakFrame, 0.0, MaximumShutterSeconds);
        _lastStreakFrame = now;
        double minimum = StreakLengthMetres * UnitsPerMetre;
        double trail = Math.Max(minimum, speed * shutter);
        float smear = (float)Math.Clamp(minimum / trail, MinimumSmearBrightness, 1.0);

        for (int i = _streaks.Count - 1; i >= 0; i--)
        {
            Streak streak = _streaks[i];

            // Where the round's head is along its whole flight, and so which part of this pass the trail covers.
            double head = (now - streak.Born) * speed;
            double from = Math.Max(streak.StartDistance, head - trail);
            double to = Math.Min(streak.EndDistance, head);
            if (from >= streak.EndDistance)
            {
                _streaks.RemoveAt(i);
                continue;
            }

            if (!(to > from) || _used + 4 > MaximumVertices)
            {
                continue;
            }

            double span = streak.EndDistance - streak.StartDistance;
            Vector3d a = PointAt(streak, (from - streak.StartDistance) / span) - eye;
            Vector3d b = PointAt(streak, (to - streak.StartDistance) / span) - eye;

            // Brightest at the head, dying away along the trail.
            float tailEnergy = (float)Math.Clamp(1.0 - ((head - from) / trail), 0.0, 1.0) * smear;
            float headEnergy = (float)Math.Clamp(1.0 - ((head - to) / trail), 0.0, 1.0) * smear;
            Bgra32 tailColour = Tint(AmmoTracer.ColourAt(streak.Ammunition, (float)(from / streak.Range)), tailEnergy);
            Bgra32 headColour = Tint(AmmoTracer.ColourAt(streak.Ammunition, (float)(to / streak.Range)), headEnergy);

            AddStrip(a, Depth(a, forward), tailColour, b, Depth(b, forward), headColour);
        }
    }

    private static Vector3d PointAt(Streak streak, double t) => streak.Start + ((streak.End - streak.Start) * t);

    // ---- Shared -----------------------------------------------------------------------------------------------

    // Both ends clipped to the near depth (0x4E0E20), or nothing when both are inside it; then a strip facing the eye,
    // u along it from a to b and v across it, keeping its pixel width at every depth.
    private void AddStrip(Vector3d a, double depthA, Bgra32 colourA, Vector3d b, double depthB, Bgra32 colourB)
    {
        if (depthA <= NearClipUnits)
        {
            if (depthB <= NearClipUnits)
            {
                return;
            }

            a = Vector3d.Lerp(a, b, (NearClipUnits - depthA) / (depthB - depthA));
            depthA = NearClipUnits;
        }
        else if (depthB <= NearClipUnits)
        {
            b = Vector3d.Lerp(b, a, (NearClipUnits - depthB) / (depthA - depthB));
            depthB = NearClipUnits;
        }

        Vector3d am = a / UnitsPerMetre;
        Vector3d bm = b / UnitsPerMetre;
        Vector3d along = bm - am;
        if (!(along.Length > 1e-9) || _used + 4 > MaximumVertices)
        {
            return;
        }

        if (!Across(along, am, depthA / UnitsPerMetre * _halfWidthPerMetre, out Vector3f acrossA)
            || !Across(along, bm, depthB / UnitsPerMetre * _halfWidthPerMetre, out Vector3f acrossB))
        {
            return;
        }

        Vector3f pa = new((float)am.X, (float)am.Y, (float)am.Z);
        Vector3f pb = new((float)bm.X, (float)bm.Y, (float)bm.Z);
        Vector3f normal = Normal(am);
        _vertices[_used] = new Vertex3D(pa + acrossA, normal, 0f, 0f, colour: colourA);
        _vertices[_used + 1] = new Vertex3D(pa - acrossA, normal, 0f, 1f, colour: colourA);
        _vertices[_used + 2] = new Vertex3D(pb - acrossB, normal, 1f, 1f, colour: colourB);
        _vertices[_used + 3] = new Vertex3D(pb + acrossB, normal, 1f, 0f, colour: colourB);
        _used += 4;
    }

    // The strip's half-width at one end: across the line and square to the eye, so it keeps its pixel width.
    private static bool Across(Vector3d along, Vector3d at, double half, out Vector3f across)
    {
        Vector3d toEye = -at;
        Vector3d cross = new(
            (along.Y * toEye.Z) - (along.Z * toEye.Y),
            (along.Z * toEye.X) - (along.X * toEye.Z),
            (along.X * toEye.Y) - (along.Y * toEye.X));
        double length = cross.Length;
        across = default;
        if (!(half > 0.0))
        {
            return false;
        }

        // A round coming straight at the eye has no across of its own: it is seen end-on, as a point. Any
        // perpendicular draws that point; the one in the horizontal plane is as good as another.
        if (!(length > 1e-9 * Math.Max(along.Length * toEye.Length, 1e-9)))
        {
            Vector3d side = Math.Abs(along.Z) < 0.9 * along.Length ? new Vector3d(0.0, 0.0, 1.0) : new Vector3d(1.0, 0.0, 0.0);
            cross = new Vector3d(
                (along.Y * side.Z) - (along.Z * side.Y),
                (along.Z * side.X) - (along.X * side.Z),
                (along.X * side.Y) - (along.Y * side.X));
            length = cross.Length;
            if (!(length > 1e-12))
            {
                return false;
            }
        }

        cross *= half / length;
        across = new Vector3f((float)cross.X, (float)cross.Y, (float)cross.Z);
        return IsFinite(across);
    }

    private static double Depth(Vector3d at, Vector3f forward) =>
        (at.X * forward.X) + (at.Y * forward.Y) + (at.Z * forward.Z);

    private static Vector3f Metres(Vector3d units) =>
        new((float)(units.X / UnitsPerMetre), (float)(units.Y / UnitsPerMetre), (float)(units.Z / UnitsPerMetre));

    private static Vector3f Normal(Vector3d at)
    {
        double length = at.Length;
        return length > 1e-9
            ? new Vector3f((float)(-at.X / length), (float)(-at.Y / length), (float)(-at.Z / length))
            : new Vector3f(0f, 0f, 1f);
    }

    // The colour at the display encoding and the brightness as the alpha, which is what the tracer shader reads.
    private static Bgra32 Tint((float R, float G, float B) colour, float alpha) => new(
        (byte)Math.Clamp(colour.B * 255f, 0f, 255f),
        (byte)Math.Clamp(colour.G * 255f, 0f, 255f),
        (byte)Math.Clamp(colour.R * 255f, 0f, 255f),
        (byte)Math.Clamp(alpha * 255f, 0f, 255f));

    private static bool IsFinite(Vector3f v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static ushort[] BuildQuadIndices()
    {
        ushort[] indices = new ushort[MaximumQuads * 6];
        for (int quad = 0; quad < MaximumQuads; quad++)
        {
            int vertex = quad * 4;
            int index = quad * 6;
            indices[index] = (ushort)vertex;
            indices[index + 1] = (ushort)(vertex + 1);
            indices[index + 2] = (ushort)(vertex + 2);
            indices[index + 3] = (ushort)vertex;
            indices[index + 4] = (ushort)(vertex + 2);
            indices[index + 5] = (ushort)(vertex + 3);
        }

        return indices;
    }

    // One remastered streak in flight: the pass it flies along and when it was first seen.
    private readonly record struct Streak(
        Vector3d Start,
        Vector3d End,
        double StartDistance,
        double EndDistance,
        double Range,
        AmmoType Ammunition,
        double Born);
}
