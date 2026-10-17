using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Draws the ballistic rounds in flight: a glow at the head, a streak behind it, a fading ribbon tail.</summary>
/// <remarks>
/// <para>
/// The modern-shooter tracer, for the <c>g_ballistics</c> option. Each round is drawn as three
/// things: a <b>camera-facing glow dot</b> at the interpolated head — the one part that survives
/// being looked at end-on, which is exactly how the shooter sees their own round — a
/// velocity-stretched <b>head streak</b>, and a <b>trail ribbon</b> through the round's recorded
/// past positions, tapering and fading tailward. All are quads over one procedurally built glow
/// texture, blended additively and never depth-written: a glow is light, and light does not occlude.
/// </para>
/// <para>
/// <b>The texture carries its profile in the COLOUR channels with a solid alpha.</b> The device
/// blends <c>src·srcA + dst</c> with both texture and vertex colour multiplied in; putting the
/// falloff in colour <i>and</i> alpha squares it and reads as nothing. Ribbons sample the texture's
/// centre column (<c>u = 0.5</c>) so <c>v</c> runs across the radial profile's full brightness;
/// only a streak's tip runs <c>u</c> out to the rim, which is what rounds it off.
/// </para>
/// <para>
/// The first metres of a player round are drawn <b>from the gun, not the eye</b>: the simulation
/// flies the retail muzzle — the eye, dead centre of the screen, where a receding streak projects
/// to a point — so each round carries a purely visual offset that converges onto the true
/// trajectory over <see cref="MuzzleConvergeMetres"/>. The round every game since has drawn, for
/// the same reason.
/// </para>
/// <para>
/// The colour is <b>white</b>, kept subtle, with only retail's gentle distance fade
/// (<see cref="AmmoTracer.IntensityAt"/>) applied — the retail amber-to-yellow ammunition gradient
/// is available in <see cref="AmmoTracer.ColourAt"/> but reads as flame against this game's dusk
/// levels. Geometry is CAMERA-RELATIVE in double precision before narrowing, for the reason
/// <c>BulletMarkRenderer</c> documents.
/// </para>
/// </remarks>
public sealed class TracerTrailRenderer : IDisposable
{
    /// <summary>The glow texture's edge, in texels.</summary>
    public const int GlowTextureSize = 64;

    /// <summary>How wide the head streak is, in metres.</summary>
    public const float HeadWidthInMetres = 0.02f;

    /// <summary>How wide the ribbon is at the head end, in metres.</summary>
    public const float TrailWidthInMetres = 0.012f;

    /// <summary>The glow dot's diameter at the head, in metres.</summary>
    public const float DotDiameterInMetres = 0.05f;

    /// <summary>Extra width per metre of distance from the eye, so a far tracer stays a visible line.</summary>
    /// <remarks>
    /// Kept small on purpose: enough that a far tracer holds a pixel or two of line, not enough to
    /// swell into the fat blurry ribbon the first tuning had.
    /// </remarks>
    public const float WidthPerMetreOfDistance = 0.0003f;

    /// <summary>Over how many metres a player round's visual muzzle offset converges onto the true line.</summary>
    /// <remarks>
    /// Long enough that a rifle round — thirty metres a tick — still draws its first segments
    /// leaving the gun rather than the crosshair; a short convergence is spent inside one tick and
    /// never seen.
    /// </remarks>
    public const float MuzzleConvergeMetres = 40f;

    /// <summary>Inside this distance from the eye a tracer is not drawn at all, in metres.</summary>
    /// <remarks>
    /// On the firing frame the head is centimetres from the eye, where any quad blows up into a
    /// screen-filling square for a frame. Nothing that close is a picture of anything.
    /// </remarks>
    public const float NearCullMetres = 0.9f;

    /// <summary>Over how many metres past the cull a tracer fades up to full strength.</summary>
    public const float NearFadeMetres = 4f;

    /// <summary>The fraction of a tick's travel the head streak stretches over.</summary>
    /// <remarks>
    /// A round covers its whole inter-tick distance between two drawn frames, but stretching the quad
    /// over all of it reads as a solid rod. A third is the streak films actually leave on a sensor.
    /// </remarks>
    public const float HeadStretchFraction = 0.35f;

    // The muzzle flame's own recipe: fully emissive, casting no occlusion, no temporal history. A
    // tracer is a light source, and the forward shader must not shade it like a surface.
    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    /// <summary>The most quads one frame may draw; anything past it is silently not drawn.</summary>
    /// <remarks>
    /// 2048 covers sixty-four rounds each trailing at full length. The cap exists because the mesh
    /// is FIXED-CAPACITY by design: it is created once and its vertices replaced in place each
    /// frame through <see cref="IGraphicsDevice.UpdateAnimatedMesh"/> — the same battle-tested path
    /// the CPU-skinned soldiers stream through every frame. The first cut destroyed and re-created
    /// a mesh per frame instead, and a buffer drawn on the very frame it was created is exactly the
    /// pattern that rasterised as a one-frame garbage quad — the intermittent black square — on a
    /// threaded driver.
    /// </remarks>
    public const int MaximumQuads = 2048;

    private const int MaximumVertices = MaximumQuads * 4;

    private readonly IGraphicsDevice _device;
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumVertices];

    // Per-point scratch for one round's connected trail ribbon, sized by the trail capacity.
    private readonly Vector3d[] _ribbonPoints = new Vector3d[BallisticSimulation.TrailCapacity];
    private readonly Vector3f[] _ribbonAcross = new Vector3f[BallisticSimulation.TrailCapacity];
    private readonly Bgra32[] _ribbonColour = new Bgra32[BallisticSimulation.TrailCapacity];
    private int _used;
    private int _lastUsed;
    private TextureHandle _glow = TextureHandle.None;
    private MeshHandle _mesh = MeshHandle.None;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TracerTrailRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public TracerTrailRenderer(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Gets how many quads the last <see cref="Draw"/> submitted.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Gets how many quads have ever been refused for carrying a non-finite value.</summary>
    /// <remarks>
    /// Diagnostic, cumulative. Zero is the healthy value; anything else means a NaN or infinity
    /// reached the geometry build and was stopped here instead of rasterising as a black quad.
    /// </remarks>
    public int SkippedQuads { get; private set; }

    /// <summary>Draws every round in flight and every trail still fading.</summary>
    /// <param name="ballistics">The simulation being drawn.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">
    /// How far the frame sits between the last tick and the current one, 0–1 — the same value the
    /// soldier renderer blends with, so the head moves as smoothly as everything else.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ballistics"/> is <see langword="null"/>.</exception>
    public void Draw(BallisticSimulation ballistics, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(ballistics);

        LastDrawCount = 0;
        if (ballistics.Rounds.Count == 0)
        {
            return;
        }

        _used = 0;
        Vector3d eye = camera.Position;
        foreach (BallisticRound round in ballistics.Rounds)
        {
            Build(round, eye, alpha);
        }

        if (_used == 0)
        {
            return;
        }

        // Whatever last frame wrote past this frame's count is degenerate-cleared, so the fixed
        // index range never rasterises a stale quad. All-zero vertices are zero-area triangles.
        if (_lastUsed > _used)
        {
            Array.Clear(_vertices, _used, _lastUsed - _used);
        }

        _lastUsed = _used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, BuildQuadIndices());
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);

            // The animated mesh carries a previous-pose stream this renderer has no use for -- the
            // trail IS its own motion record -- so the blend is cleared or a stale soldier
            // interpolation would drag every quad toward last frame's buffer.
            _device.ClearPoseInterpolation();

            _device.DrawMesh(
                _mesh,
                0,
                _used / 4 * 6,
                modelView,
                GetGlowTexture(),
                TextureHandle.None,
                BlendMode.Additive);

            LastDrawCount = _used / 4;
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // A tracer that cannot upload this frame is simply not drawn; there is nothing to log a
            // thousand times a firefight.
        }

        _device.SetDepthState(DepthState.TestAndWrite);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    // The index pattern is static -- quad n is vertices 4n..4n+3 -- so it is built once, at the
    // size the mesh will keep for its whole life.
    private static ushort[] BuildQuadIndices()
    {
        ushort[] indices = new ushort[MaximumQuads * 6];
        for (int quad = 0; quad < MaximumQuads; quad++)
        {
            int b = quad * 4;
            int i = quad * 6;
            indices[i] = (ushort)b;
            indices[i + 1] = (ushort)(b + 1);
            indices[i + 2] = (ushort)(b + 2);
            indices[i + 3] = (ushort)b;
            indices[i + 4] = (ushort)(b + 2);
            indices[i + 5] = (ushort)(b + 3);
        }

        return indices;
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

        if (_glow.IsValid)
        {
            _device.DestroyTexture(_glow);
            _glow = TextureHandle.None;
        }
    }

    private static (float Red, float Green, float Blue) ColourOf(BallisticRound round, double distanceUnits)
    {
        // White, deliberately, where retail's tracer ran the ammunition's amber-to-yellow gradient
        // (AmmoTracer has it, should anyone want it back): against this game's dusk-and-fog levels
        // the retail colours read as blobs of flame. Only retail's gentle distance fade is kept.
        float intensity = AmmoTracer.IntensityAt(1f, (float)distanceUnits);

        // A pellet is one of thirty; drawing each at full blast turns a shotgun into a flare.
        if (round.Weapon.BulletsPerRound > 1)
        {
            intensity *= 0.45f;
        }

        return (intensity, intensity, intensity);
    }

    private static Bgra32 Tint((float Red, float Green, float Blue) colour, float alpha)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

        // Alpha is floored at one because Vertex3D reads an all-zero colour as "no tint given" and
        // substitutes white -- a faded-to-nothing tail vertex must never round to that.
        return new Bgra32(
            Channel(colour.Blue),
            Channel(colour.Green),
            Channel(colour.Red),
            Math.Max((byte)1, Channel(alpha)));
    }

    // Where a point of the round's path is DRAWN: the simulated position plus the round's visual
    // muzzle offset, faded out over the first metres of flight so the streak leaves the gun and
    // converges onto the trajectory the simulation actually flew.
    private static Vector3d Presented(BallisticRound round, Vector3d simulated)
    {
        if (round.VisualOffset is not { } offset)
        {
            return simulated;
        }

        double travelled = (simulated - round.Origin).Length;
        double converge = MuzzleConvergeMetres * WorldScene.UnitsPerMetre;
        double remaining = 1.0 - (travelled / converge);
        return remaining <= 0.0 ? simulated : simulated + (offset * remaining);
    }

    private void Build(BallisticRound round, Vector3d eye, float alpha)
    {
        double fade = round.Alive
            ? 1.0
            : (double)round.FadeTicks / BallisticSimulation.FadeLifetimeTicks;
        if (fade <= 0.0)
        {
            return;
        }

        // A round whose state has gone non-finite draws nothing at all; see AddRibbonSegment for
        // what one NaN quad costs.
        if (!IsFinite(round.Position) || !IsFinite(round.PreviousPosition))
        {
            SkippedQuads++;
            return;
        }

        // The head: only while the round flies. Interpolated between the last two tick positions,
        // a glow dot that reads from any angle plus a streak stretched backwards along the travel.
        if (round.Alive)
        {
            // The tail's parameter is clamped at zero: early in a round's first drawn frame the
            // stretch would extrapolate BEHIND the previous position -- behind the camera, for the
            // shooter's own round -- and a quad with a vertex behind the near plane rasterises as a
            // one-frame screen-filling wedge.
            Vector3d head = Presented(round, Vector3d.Lerp(round.PreviousPosition, round.Position, alpha));
            Vector3d tail = Presented(round, Vector3d.Lerp(
                round.PreviousPosition,
                round.Position,
                Math.Max(0f, alpha - HeadStretchFraction)));

            (float r, float g, float b) = ColourOf(round, (head - round.Origin).Length);
            AddDot(head, eye, DotDiameterInMetres, Tint((r, g, b), 1f));
            AddRibbonSegment(
                head,
                tail,
                eye,
                HeadWidthInMetres,
                HeadWidthInMetres * 0.6f,
                Tint((r, g, b), 1f),
                Tint((r * 0.55f, g * 0.55f, b * 0.55f), 1f),
                tipFades: true);
        }

        // The ribbon: recorded tick positions, newest to oldest, as ONE CONNECTED STRIP. The facing
        // axis is computed PER POINT (from the two segments meeting there) and each quad's joint
        // edge reuses the next quad's start edge exactly. Independent per-segment quads each chose
        // their own axis, and where two disagreed the joint opened a wedge of background -- which on
        // a thin bright line over a dark sky read as a row of small black dots.
        BuildTrailRibbon(round, eye, alpha, (float)fade);
    }

    private void BuildTrailRibbon(BallisticRound round, Vector3d eye, float alpha, float fade)
    {
        int count = round.TrailCount;
        if (count < 2)
        {
            return;
        }

        // Per-point state, newest first: camera-relative metres, the across half-vector, and the
        // colour. A non-finite or near-culled point truncates the strip rather than bridging it.
        int points = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3d metres = (Presented(round, round.TrailAt(i)) / WorldScene.UnitsPerMetre) - eye;
            if (!IsFinite(metres))
            {
                SkippedQuads++;
                break;
            }

            _ribbonPoints[points] = metres;
            points++;
        }

        if (points < 2)
        {
            return;
        }

        for (int i = 0; i < points; i++)
        {
            Vector3d before = _ribbonPoints[Math.Max(0, i - 1)];
            Vector3d after = _ribbonPoints[Math.Min(points - 1, i + 1)];
            Vector3d along = after - before;
            Vector3d toEye = -_ribbonPoints[i];
            double distance = toEye.Length;

            Vector3d acrossD = new(
                (along.Y * toEye.Z) - (along.Z * toEye.Y),
                (along.Z * toEye.X) - (along.X * toEye.Z),
                (along.X * toEye.Y) - (along.Y * toEye.X));
            double acrossLength = acrossD.Length;

            float age = (float)i / BallisticSimulation.TrailCapacity;
            float strength = (1f - age) * fade * NearFade(distance);
            float half = (TrailWidthInMetres * strength) + ((float)distance * WidthPerMetreOfDistance);

            // A point whose segment runs straight at the eye has no across of its own; its
            // neighbour's keeps the strip watertight instead of pinching a hole into it.
            _ribbonAcross[i] = acrossLength > 1e-9
                ? new Vector3f(
                    (float)(acrossD.X / acrossLength * half),
                    (float)(acrossD.Y / acrossLength * half),
                    (float)(acrossD.Z / acrossLength * half))
                : (i > 0 ? _ribbonAcross[i - 1] : default);

            (float r, float g, float b) = ColourOf(round, (round.TrailAt(i) - round.Origin).Length);
            _ribbonColour[i] = Tint(
                (r * strength * 0.55f, g * strength * 0.55f, b * strength * 0.55f), strength);
        }

        for (int i = 0; i < points - 1; i++)
        {
            if (_used + 4 > MaximumVertices)
            {
                return;
            }

            Vector3f near = new((float)_ribbonPoints[i].X, (float)_ribbonPoints[i].Y, (float)_ribbonPoints[i].Z);
            Vector3f far = new((float)_ribbonPoints[i + 1].X, (float)_ribbonPoints[i + 1].Y, (float)_ribbonPoints[i + 1].Z);
            Vector3f nearAcross = _ribbonAcross[i];
            Vector3f farAcross = _ribbonAcross[i + 1];

            if (!IsFinite(nearAcross) || !IsFinite(farAcross))
            {
                SkippedQuads++;
                continue;
            }

            double midDistance = (_ribbonPoints[i].Length + _ribbonPoints[i + 1].Length) * 0.5;
            Vector3f normal = midDistance > 1e-6
                ? new Vector3f(
                    (float)(-_ribbonPoints[i].X / midDistance),
                    (float)(-_ribbonPoints[i].Y / midDistance),
                    (float)(-_ribbonPoints[i].Z / midDistance))
                : new Vector3f(0f, 0f, 1f);

            float farU = i == points - 2 ? 1f : 0.5f;
            _vertices[_used] = new Vertex3D(near + nearAcross, normal, 0.5f, 0f, colour: _ribbonColour[i]);
            _vertices[_used + 1] = new Vertex3D(near - nearAcross, normal, 0.5f, 1f, colour: _ribbonColour[i]);
            _vertices[_used + 2] = new Vertex3D(far - farAcross, normal, farU, 1f, colour: _ribbonColour[i + 1]);
            _vertices[_used + 3] = new Vertex3D(far + farAcross, normal, farU, 0f, colour: _ribbonColour[i + 1]);
            _used += 4;
        }
    }

    // How much of a thing this close to the eye is drawn: nothing inside the cull, easing up to full
    // over the fade. This is what stops the firing frame's head -- centimetres from the camera --
    // exploding into a one-frame square, and what keeps the streak's first metres off the gun modest.
    private static float NearFade(double distanceFromEyeMetres) =>
        Math.Clamp(((float)distanceFromEyeMetres - NearCullMetres) / NearFadeMetres, 0f, 1f);

    private static bool IsFinite(Vector3f v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool IsFinite(Vector3d v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private static Bgra32 Dimmed(Bgra32 colour, float factor)
    {
        static byte Scale(byte value, float f) => (byte)Math.Clamp((int)(value * f), 0, 255);
        return factor >= 1f
            ? colour
            : new Bgra32(
                Scale(colour.B, factor),
                Scale(colour.G, factor),
                Scale(colour.R, factor),
                Math.Max((byte)1, Scale(colour.A, factor)));
    }

    // One camera-facing quad along a world-space segment. The across axis is segment x to-eye, which
    // is what keeps a ribbon seen from the side full width -- and why a ribbon alone vanishes seen
    // end-on, which is what the head dot is for. Sampling runs across the glow's centre column so
    // the profile peaks along the ribbon's spine; a fading tip runs u out to the rim instead.
    private void AddRibbonSegment(
        Vector3d nearUnits,
        Vector3d farUnits,
        Vector3d eye,
        float nearHalfWidthMetres,
        float farHalfWidthMetres,
        Bgra32 nearColour,
        Bgra32 farColour,
        bool tipFades)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        // Units to metres, then camera-relative in double before narrowing.
        Vector3d near = (nearUnits / WorldScene.UnitsPerMetre) - eye;
        Vector3d far = (farUnits / WorldScene.UnitsPerMetre) - eye;

        // Checked FIRST, so a poisoned input is counted by the diagnostic rather than silently
        // swallowed by a length guard downstream.
        if (!IsFinite(near) || !IsFinite(far))
        {
            SkippedQuads++;
            return;
        }

        // EVERY threshold below is spelled `!(x > limit)` and not `x < limit`, deliberately: the
        // comparisons must also reject NaN, and NaN compares false BOTH ways. A NaN length walking
        // through a `< limit` guard poisons all four vertices of the quad, and four NaN vertices
        // rasterise -- on the drivers that rasterise them at all -- as a solid quad of NaN
        // fragments, which the additive blend turns into a one-frame BLACK SQUARE on the HDR
        // target: NaN is the one value an additive blend can darken with.
        Vector3d along = far - near;
        double alongLength = along.Length;
        if (!(alongLength > 1e-9))
        {
            return;
        }

        Vector3d mid = (near + far) * 0.5;
        Vector3d toEye = -mid;
        Vector3d acrossD = new(
            (along.Y * toEye.Z) - (along.Z * toEye.Y),
            (along.Z * toEye.X) - (along.X * toEye.Z),
            (along.X * toEye.Y) - (along.Y * toEye.X));
        double acrossLength = acrossD.Length;
        if (!(acrossLength > 1e-9))
        {
            // The segment points straight at the eye; the dot carries this view.
            return;
        }

        double midDistance = mid.Length;
        float nearFade = NearFade(midDistance);
        if (!(nearFade > 0f))
        {
            return;
        }

        acrossD /= acrossLength;
        Vector3f across = new((float)acrossD.X, (float)acrossD.Y, (float)acrossD.Z);
        Vector3f normal = new((float)(toEye.X / midDistance), (float)(toEye.Y / midDistance), (float)(toEye.Z / midDistance));

        if (!IsFinite(across) || !IsFinite(normal))
        {
            SkippedQuads++;
            return;
        }

        nearColour = Dimmed(nearColour, nearFade);
        farColour = Dimmed(farColour, nearFade);

        // The distance cheat: a far tracer keeps a few pixels of width, or it is a firefight of
        // invisible geometry.
        float boost = (float)midDistance * WidthPerMetreOfDistance;
        float nearHalf = nearHalfWidthMetres + boost;
        float farHalf = farHalfWidthMetres + boost;

        Vector3f nearF = new((float)near.X, (float)near.Y, (float)near.Z);
        Vector3f farF = new((float)far.X, (float)far.Y, (float)far.Z);

        float farU = tipFades ? 1f : 0.5f;
        _vertices[_used] = new Vertex3D(nearF + (across * nearHalf), normal, 0.5f, 0f, colour: nearColour);
        _vertices[_used + 1] = new Vertex3D(nearF - (across * nearHalf), normal, 0.5f, 1f, colour: nearColour);
        _vertices[_used + 2] = new Vertex3D(farF - (across * farHalf), normal, farU, 1f, colour: farColour);
        _vertices[_used + 3] = new Vertex3D(farF + (across * farHalf), normal, farU, 0f, colour: farColour);
        _used += 4;
    }

    // A camera-facing square over the whole radial glow: the round's light itself. This is the part
    // of the tracer that survives every viewing angle -- from directly behind, a fired round IS a
    // receding glowing dot and nothing else.
    private void AddDot(Vector3d positionUnits, Vector3d eye, float diameterMetres, Bgra32 colour)
    {
        if (_used + 4 > MaximumVertices)
        {
            return;
        }

        Vector3d centre = (positionUnits / WorldScene.UnitsPerMetre) - eye;
        double distance = centre.Length;
        float nearFade = NearFade(distance);

        // `!(x > limit)` rather than `x < limit` so NaN is rejected too; see AddRibbonSegment.
        if (!(distance > 1e-6) || !(nearFade > 0f))
        {
            return;
        }

        colour = Dimmed(colour, nearFade);
        Vector3d toEye = centre / -distance;

        // Any two axes perpendicular to the view will do -- a glow has no up. The same conditioning
        // trick the decal renderer plays.
        Vector3d reference = Math.Abs(toEye.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d rightD = new(
            (reference.Y * toEye.Z) - (reference.Z * toEye.Y),
            (reference.Z * toEye.X) - (reference.X * toEye.Z),
            (reference.X * toEye.Y) - (reference.Y * toEye.X));
        double rightLength = rightD.Length;
        if (!(rightLength > 1e-9))
        {
            return;
        }

        rightD /= rightLength;
        Vector3d upD = new(
            (toEye.Y * rightD.Z) - (toEye.Z * rightD.Y),
            (toEye.Z * rightD.X) - (toEye.X * rightD.Z),
            (toEye.X * rightD.Y) - (toEye.Y * rightD.X));

        float half = (diameterMetres * 0.5f) + ((float)distance * WidthPerMetreOfDistance);
        Vector3f right = new((float)(rightD.X * half), (float)(rightD.Y * half), (float)(rightD.Z * half));
        Vector3f up = new((float)(upD.X * half), (float)(upD.Y * half), (float)(upD.Z * half));
        Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
        Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);

        if (!IsFinite(centreF) || !IsFinite(right) || !IsFinite(up) || !IsFinite(normal))
        {
            SkippedQuads++;
            return;
        }

        _vertices[_used] = new Vertex3D(centreF - right - up, normal, 0f, 0f, colour: colour);
        _vertices[_used + 1] = new Vertex3D(centreF + right - up, normal, 1f, 0f, colour: colour);
        _vertices[_used + 2] = new Vertex3D(centreF + right + up, normal, 1f, 1f, colour: colour);
        _vertices[_used + 3] = new Vertex3D(centreF - right + up, normal, 0f, 1f, colour: colour);
        _used += 4;
    }

    // A radial soft glow, built once: bright core, falloff to nothing at the rim. THE PROFILE IS
    // SPLIT AS ITS SQUARE ROOT ACROSS COLOUR AND ALPHA: the additive blend multiplies the two, so
    // the product is the intended linear profile -- same brightness as colour-only with solid
    // alpha, which an earlier cut used. The difference is the rim's ALPHA is now zero, and that is
    // a robustness property, not a look: with solid alpha the quad's whole RECTANGLE stamped the
    // scene target's alpha channel and rendered as a hard box under anything that was not exactly
    // additive-over-colour (the intermittent black square around a trail). Shaped alpha degrades
    // every such path to a soft round glow instead.
    private TextureHandle GetGlowTexture()
    {
        if (_glow.IsValid)
        {
            return _glow;
        }

        Bgra32[] pixels = new Bgra32[GlowTextureSize * GlowTextureSize];
        float centre = (GlowTextureSize - 1) * 0.5f;
        for (int y = 0; y < GlowTextureSize; y++)
        {
            for (int x = 0; x < GlowTextureSize; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                float glow = Math.Clamp(1f - distance, 0f, 1f);

                // The gamma is what decides crisp against blurry: 2.4 pulls the energy into a hot
                // narrow core with a fast falloff, so a tracer reads as a LINE of light rather than
                // a soft fat ribbon. The earlier 1.6 was the blur.
                glow = MathF.Pow(glow, 2.4f);

                byte value = (byte)(MathF.Sqrt(glow) * 255f);
                pixels[(y * GlowTextureSize) + x] = new Bgra32(value, value, value, value);
            }
        }

        _glow = _device.CreateTexture(
            new TextureDescription(
                GlowTextureSize,
                GlowTextureSize,
                MipLevels: 1,
                TextureFilter.Linear,
                TextureAddressMode.Clamp),
            pixels);
        return _glow;
    }
}
