using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// How much of the open sky reaches a point, and from which directions: the geometry behind hearing
/// the weather from indoors.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it answers.</b> <see cref="ShelterHeightField.IsUnderCover"/> answers one bit — is there a
/// roof over this point — and that bit is all the picture needs, because rain either falls on you or
/// does not. The ear needs more than that. Standing in a warehouse with the loading door open, the
/// roof is overhead in every direction and the rain is nonetheless plainly audible, plainly to the
/// left, and plainly brighter than the same rain through the wall behind you. A single bit cannot say
/// any of that. This says all three.
/// </para>
/// <para>
/// <b>How.</b> A fixed set of directions is cast from the ear, and each one is asked a single
/// question: is the far end of what this ray travelled through out in the open. A ray that meets
/// nothing is judged where it gave up; a ray that meets a surface is judged a step back from it, in
/// the last of the air it crossed. So a ray out of a door and onto the yard beyond has escaped, and a
/// ray into an interior wall, a floor under a roof, or a ceiling has not. Looking at the wet ground
/// outside counts, which is as it should be — most of the rain heard outdoors is rain striking the
/// ground, not rain passing through the air beside you.
/// </para>
/// <para>
/// <b>Why a surface, not just the sky.</b> Casting only upwards would make an open field score a half
/// — half a sphere of directions ends in the ground — and would make a doorway score nothing at all,
/// because a doorway is level with the ear and what you see through it is the yard, not the sky. Both
/// answers would be wrong in the direction that matters.
/// </para>
/// <para>
/// <b>The directions are the six world axes.</b> Every ray's result is spread across them by the
/// square of its cosine, which for an axis-aligned set sums to exactly one per ray — the components
/// of a unit vector squared — so the spreading neither creates nor destroys energy. Six numbers then
/// carry the whole directional picture: all six high is standing in a field, one high and five low is
/// standing beside an open door, all six low is standing in a cellar. A listener that puts one
/// emitter on each axis reproduces the field it came from, and a source between two axes lands
/// between the two emitters where it belongs.
/// </para>
/// <para>
/// <b>Cost.</b> The rays are spread over consecutive calls — <see cref="RaysPerUpdate"/> of them each
/// time — so the per-frame cost is a fixed handful of casts against the same collision the grenades
/// use, and the full set is never more than a few frames stale. The spreading weights are constant
/// and computed once, so everything after the casts is adding sixty-odd floats.
/// </para>
/// </remarks>
public sealed class SkyExposureProbe
{
    /// <summary>How many rays are cast in total, by default.</summary>
    public const int DefaultRayCount = 64;

    /// <summary>How far a ray travels before it is judged on where it ended, in metres.</summary>
    /// <remarks>
    /// Far enough to leave any room and cross any yard, short enough that a ray down a long corridor
    /// gives up rather than escaping through a door at the far end of the building and reporting the
    /// weather as though it were next to you.
    /// </remarks>
    public const double DefaultReachMetres = 30.0;

    /// <summary>How far above the ear a roof stops being heard as a roof, in metres.</summary>
    /// <remarks>
    /// A hangar ceiling is still a ceiling, and it does still drum — less than a shed roof two metres
    /// over your head, but it is the loudest thing in a warehouse in a storm. Fifteen metres was too
    /// mean: it cut the drumming to nothing in exactly the large interiors that have no other way to
    /// sound wet, leaving them on the through-the-wall level alone and shut in like a cellar.
    /// </remarks>
    public const double RoofFadeMetres = 25.0;

    /// <summary>How far above the ear a roof is as close as it needs to be, in metres.</summary>
    public const double RoofNearMetres = 3.0;

    /// <summary>How far back along a ray from what it struck the escape test is taken, in metres.</summary>
    /// <remarks>
    /// <para>
    /// <b>The test is taken beside the surface, not on it.</b> Asking whether the struck point itself
    /// is under a roof cannot tell a ceiling from the ground: this game's roofs are frequently single
    /// planes with no thickness at all, so the underside of a roof and the top of a roof are the
    /// <i>same height</i>, and any test at that height says the same thing about both. A step back
    /// along the ray moves the question to the space the sound would have travelled through, which is
    /// the room for a ceiling and the open air for the ground outside, and those two are never
    /// confusable.
    /// </para>
    /// <para>
    /// Half a metre — one raster cell — is far enough to clear the surface and its quantisation, and
    /// near enough that it cannot step through a wall into a different room. It must stay comfortably
    /// larger than <see cref="SurfaceEpsilonMetres"/>, or a ceiling stops reading as a ceiling.
    /// </para>
    /// </remarks>
    public const double PullbackMetres = 0.5;

    /// <summary>How far above the sampled point a roof must stand for it to be out of the rain.</summary>
    /// <remarks>
    /// A tenth of a metre: enough to absorb the raster's quantisation, and far below
    /// <see cref="PullbackMetres"/> so that the space just under a roof still reads as roofed. The
    /// field's own <see cref="ShelterHeightField.SurfaceHeadroomMetres"/> is three quarters of a metre
    /// and is sized for a different question — whether a surface collects rain — where a kerb must not
    /// shelter the road it stands on.
    /// </remarks>
    public const double SurfaceEpsilonMetres = 0.1;

    private static readonly Vector3f[] AxisDirections =
    [
        new(1f, 0f, 0f),
        new(-1f, 0f, 0f),
        new(0f, 1f, 0f),
        new(0f, -1f, 0f),
        new(0f, 0f, 1f),
        new(0f, 0f, -1f),
    ];

    private readonly Vector3f[] _rays;
    private readonly float[] _spread;
    private readonly float[] _rayTotal;
    private readonly bool[] _escaped;
    private readonly float[] _open;
    private readonly float[] _directional;
    private readonly double _reachMetres;

    private int _cursor;
    private float _roofNearness;

    /// <summary>Initializes a new instance of the <see cref="SkyExposureProbe"/> class.</summary>
    /// <param name="rayCount">How many directions to sample. More is smoother and costs more.</param>
    /// <param name="reachMetres">How far a ray travels before it is judged on where it ended.</param>
    /// <exception cref="ArgumentOutOfRangeException">The ray count or the reach is not positive.</exception>
    public SkyExposureProbe(int rayCount = DefaultRayCount, double reachMetres = DefaultReachMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rayCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reachMetres);

        _reachMetres = reachMetres;
        _rays = BuildRayDirections(rayCount);
        _escaped = new bool[rayCount];
        _open = new float[AxisDirections.Length];
        _directional = new float[AxisDirections.Length];
        _rayTotal = new float[AxisDirections.Length];
        _spread = new float[rayCount * AxisDirections.Length];

        // The spreading weights never change, so they are paid for once. The square of the cosine
        // against each axis is just that axis' component squared, and the three squares of a unit
        // vector sum to one -- which is why nothing here needs normalising afterwards.
        for (int ray = 0; ray < rayCount; ray++)
        {
            for (int axis = 0; axis < AxisDirections.Length; axis++)
            {
                float cosine = Vector3f.Dot(_rays[ray], AxisDirections[axis]);
                float weight = cosine > 0f ? cosine * cosine : 0f;
                _spread[(ray * AxisDirections.Length) + axis] = weight;
                _rayTotal[axis] += weight;
            }
        }

        RaysPerUpdate = Math.Max(1, rayCount / 4);
    }

    /// <summary>Gets the six directions <see cref="DirectionalExposure"/> is measured along.</summary>
    /// <remarks>World axes, in the order +X, −X, +Y, −Y, +Z, −Z. The world is Z-up, so the last pair
    /// is the sky above and the ground below.</remarks>
    public static ReadOnlySpan<Vector3f> Directions => AxisDirections;

    /// <summary>Gets or sets how many rays are recast on each <see cref="Update"/>.</summary>
    /// <remarks>
    /// A quarter of the set by default, so the whole sphere is no more than four frames old. Setting
    /// it to the full ray count makes one call a complete measurement, which is what a test wants and
    /// what a mission start wants.
    /// </remarks>
    public int RaysPerUpdate { get; set; }

    /// <summary>Gets or sets how long a change takes to travel most of the way into the readings.</summary>
    /// <remarks>
    /// Zero makes every reading immediate, for tests and for the first measurement of a mission. In
    /// play it wants to be long enough to hide the round-robin's own flutter — a third of a second is
    /// slower than the rays and faster than a player walking through a door.
    /// </remarks>
    public float SmoothingSeconds { get; set; } = 0.35f;

    /// <summary>Gets how much of the whole sphere reaches the open weather, zero to one.</summary>
    /// <remarks>
    /// One is an open field. Zero is sealed in. A doorway you are standing in is around a half, and a
    /// room with one open door across it is a few hundredths.
    /// </remarks>
    public float Exposure { get; private set; }

    /// <summary>Gets how much of each of the six <see cref="Directions"/> reaches the open weather.</summary>
    /// <remarks>Indexed as <see cref="Directions"/> is. Each entry runs zero to one.</remarks>
    public ReadOnlySpan<float> DirectionalExposure => _directional;

    /// <summary>Gets how close a roof stands over the ear, one for right overhead and zero for none.</summary>
    /// <remarks>
    /// What makes an interior sound like an interior rather than merely a quiet exterior: the drumming
    /// of the rain on the roof itself, which a shed has and a hangar has much less of.
    /// </remarks>
    public float RoofNearness => _roofNearness;

    /// <summary>Forgets everything measured, so the next update starts from nothing.</summary>
    /// <remarks>Called when the listener is teleported — a new mission, a cutscene cut — where gliding
    /// from the last level's reading would be a slow wrong answer instead of a fast right one.</remarks>
    public void Reset()
    {
        Array.Clear(_escaped);
        Array.Clear(_open);
        Array.Clear(_directional);
        _cursor = 0;
        Exposure = 0f;
        _roofNearness = 0f;
    }

    /// <summary>Recasts a slice of the rays and folds the result into the readings.</summary>
    /// <param name="earMetres">Where the listener's ear is, in world metres.</param>
    /// <param name="shelter">The level's roof raster; <see cref="ShelterHeightField.Empty"/> means open ground.</param>
    /// <param name="cast">
    /// The collision the rays are tested against, or <see langword="null"/> to fall back on the roof
    /// raster alone — which still tells roofed from unroofed, but cannot find a doorway.
    /// </param>
    /// <param name="unitsPerMetre">What <paramref name="cast"/> measures in, since it works in engine units.</param>
    /// <param name="deltaSeconds">How long since the last update, for the smoothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shelter"/> is <see langword="null"/>.</exception>
    public void Update(
        Vector3d earMetres,
        ShelterHeightField shelter,
        WorldRayCast? cast,
        double unitsPerMetre,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(shelter);

        float blend = SmoothingSeconds <= 0f || deltaSeconds <= 0f
            ? 1f
            : 1f - MathF.Exp(-deltaSeconds / SmoothingSeconds);

        // Nothing in the level carries collision, so nothing can be sheltering anybody. Skipping the
        // casts here is not an optimisation for its own sake: without it, an outdoor level would pay
        // for sixty-four traces a frame to be told what its empty raster already said.
        if (shelter.IsEmpty)
        {
            Array.Fill(_escaped, true);
            Approach(1f, blend);
            _roofNearness += (0f - _roofNearness) * blend;
            return;
        }

        int rays = Math.Clamp(RaysPerUpdate, 1, _rays.Length);
        for (int i = 0; i < rays; i++)
        {
            int ray = _cursor;
            _cursor = (_cursor + 1) % _rays.Length;
            _escaped[ray] = Escapes(earMetres, _rays[ray], shelter, cast, unitsPerMetre);
        }

        Accumulate(blend);
        _roofNearness += (MeasureRoofNearness(earMetres, shelter) - _roofNearness) * blend;
    }

    /// <summary>Spreads the current escape flags over the six axes and eases the readings towards them.</summary>
    private void Accumulate(float blend)
    {
        Array.Clear(_open);
        int escapedCount = 0;

        for (int ray = 0; ray < _escaped.Length; ray++)
        {
            if (!_escaped[ray])
            {
                continue;
            }

            escapedCount++;
            int at = ray * AxisDirections.Length;
            for (int axis = 0; axis < AxisDirections.Length; axis++)
            {
                _open[axis] += _spread[at + axis];
            }
        }

        for (int axis = 0; axis < AxisDirections.Length; axis++)
        {
            float target = _rayTotal[axis] > 0f ? _open[axis] / _rayTotal[axis] : 0f;
            _directional[axis] += (target - _directional[axis]) * blend;
        }

        Exposure += ((escapedCount / (float)_escaped.Length) - Exposure) * blend;
    }

    /// <summary>Eases every reading towards one value, for the case where geometry cannot block anything.</summary>
    private void Approach(float target, float blend)
    {
        for (int axis = 0; axis < AxisDirections.Length; axis++)
        {
            _directional[axis] += (target - _directional[axis]) * blend;
        }

        Exposure += (target - Exposure) * blend;
    }

    /// <summary>Decides whether the weather reaches the ear along one direction.</summary>
    private bool Escapes(
        Vector3d earMetres,
        Vector3f direction,
        ShelterHeightField shelter,
        WorldRayCast? cast,
        double unitsPerMetre)
    {
        Vector3d step = new(direction.X, direction.Y, direction.Z);
        Vector3d endMetres = earMetres + (step * _reachMetres);
        Vector3d sample = endMetres;

        if (cast is not null
            && unitsPerMetre > 0.0
            && cast(earMetres * unitsPerMetre, endMetres * unitsPerMetre, out WorldRayHit hit))
        {
            // A step back down the ray from what was struck: the last of the open air the sound would
            // have crossed. Clamped so that a surface closer than the step does not put the sample
            // behind the ear -- it would still be in the same space, but the clamp costs nothing.
            // Measured from the contact point rather than from the reported distance, so that a caster
            // which fills one and not the other cannot quietly move every sample somewhere else.
            Vector3d contact = hit.Position / unitsPerMetre;
            double travelled = (contact - earMetres).Length;
            sample = contact - (step * Math.Min(PullbackMetres, travelled * 0.5));
        }

        // Both branches ask the same question of the same kind of point: is the air the sound came
        // through under a roof. Under a ceiling, at the back of a room or inside a hangar it is; out
        // in the yard, over the road or above the ground beyond an open door it is not.
        return !shelter.IsSheltered(sample.X, sample.Y, sample.Z, SurfaceEpsilonMetres);
    }

    /// <summary>Measures how closely a roof stands over the ear, zero to one.</summary>
    /// <remarks>
    /// Averaged over the same metre-wide cross <see cref="ShelterHeightField.IsUnderCover"/> uses, so
    /// that walking the line of an eave fades rather than flickers. A single cell would switch the
    /// drumming on and off twice a second at walking pace.
    /// </remarks>
    private static float MeasureRoofNearness(Vector3d earMetres, ShelterHeightField shelter)
    {
        const double Arm = 1.0;
        ReadOnlySpan<(double X, double Y)> taps =
        [
            (0.0, 0.0),
            (-Arm, 0.0),
            (Arm, 0.0),
            (0.0, -Arm),
            (0.0, Arm),
        ];

        float total = 0f;
        foreach ((double x, double y) in taps)
        {
            float over = shelter.SurfaceOver(earMetres.X + x, earMetres.Y + y);
            if (float.IsNegativeInfinity(over))
            {
                continue;
            }

            double height = over - earMetres.Z;
            if (height <= ShelterHeightField.EyeHeadroomMetres || height >= ShelterHeightField.MaxOverheadMetres)
            {
                continue;
            }

            total += height <= RoofNearMetres
                ? 1f
                : (float)Math.Clamp((RoofFadeMetres - height) / (RoofFadeMetres - RoofNearMetres), 0.0, 1.0);
        }

        return total / taps.Length;
    }

    /// <summary>Spreads the requested number of directions as evenly over the sphere as they go.</summary>
    /// <remarks>
    /// The Fibonacci spiral, which has no poles and no seams — a latitude/longitude grid would crowd
    /// its samples straight up and straight down, and straight up is the one direction whose answer a
    /// roof already decides.
    /// </remarks>
    private static Vector3f[] BuildRayDirections(int count)
    {
        Vector3f[] directions = new Vector3f[count];
        double golden = Math.PI * (3.0 - Math.Sqrt(5.0));

        for (int i = 0; i < count; i++)
        {
            double z = 1.0 - ((2.0 * i) + 1.0) / count;
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double angle = golden * i;
            directions[i] = new Vector3f(
                (float)(Math.Cos(angle) * radius),
                (float)(Math.Sin(angle) * radius),
                (float)z);
        }

        return directions;
    }
}
