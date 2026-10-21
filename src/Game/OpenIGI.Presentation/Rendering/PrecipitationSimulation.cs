using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The storm's particle state and how it moves: the wandering wind, every drop's spawn and fall,
/// the splashes raised around the eye and the far sheets drifting with the wind.
/// </summary>
/// <remarks>
/// <see cref="StormRenderer"/> owns the settings and the drawing; this owns everything that is
/// stepped on the fixed tick, and the one random stream the storm draws from, in the order it has
/// always drawn.
/// </remarks>
internal sealed class PrecipitationSimulation
{
    private const float TicksPerSecond = 30f;

    // The smallest drop, as a share of the largest. Sizes are drawn evenly between this and one, so most of
    // the column is fine drizzle-sized drops with the odd heavy one among them, and the size range is a
    // little over three to one.
    private const float SmallestDropScale = 0.32f;

    // How far one drop's pull may differ from the settings' gravity, either way. Narrow, because a drop is
    // born at its terminal speed and the difference barely shows; it is there so that drops which do slow
    // or speed up (through the vertical drift) do not all recover at the same rate.
    private const float GravitySpread = 0.08f;

    // The shortest life a drop may be given, as a share of LifetimeSeconds. Even the shortest must outlast
    // the slowest drop's fall from the top of the spawn slab to the ground, or drops would wink out mid-air.
    private const float ShortestLifeShare = 0.45f;

    // The crown flipbook. A crown's life is drawn between this share of SplashLifetimeSeconds and all of
    // it, which plays the eight frames at roughly fifteen to twenty-five a second; its size between this
    // share of SplashRadiusMetres and all of it.
    private const float ShortestCrownLifeShare = 0.55f;

    /// <summary>The smallest crown, as a share of <see cref="PrecipitationSettings.SplashRadiusMetres"/>.</summary>
    internal const float SmallestCrownShare = 0.66f;

    // Which of the three crown animations a splash plays, as cumulative shares: one is favoured and the
    // other two split the rest unevenly, because three animations in equal turns read as a pattern.
    private const float FirstVariantShare = 0.5f;
    private const float SecondVariantShare = 0.8f;

    // The small droplets thrown out of an impact: launched upwards and sideways, pulled back by a weakened
    // gravity under a strong drag, so they rise a few centimetres, drift a few more and fall back.
    private const float DropletUpSpeed = 0.55f;          // metres per second, at the slowest
    private const float DropletUpSpread = 0.3f;          // the fastest is this much quicker again
    private const float DropletSideSpeed = 0.35f;        // metres per second, at the most
    private const float DropletGravity = 4.5f;           // metres per second squared
    private const float DropletDrag = 1.0f;              // per second
    private const float DropletShortestLife = 0.05f;     // seconds
    private const float DropletLongestLife = 0.4f;       // seconds
    private const float DropletSizeShare = 0.15f;        // of SplashRadiusMetres, before the spread below
    private const float DropletSizeLow = 0.55f;
    private const float DropletSizeHigh = 1.5f;

    private readonly Random _random = new(0x5A1FA11);

    private PrecipitationDrop[] _drops = [];
    private readonly PrecipitationSplash[] _splashes = new PrecipitationSplash[StormRenderer.MaximumSplashes];
    private readonly SurfaceHeights _surface = new();
    private readonly PrecipitationCurtain[] _curtains = new PrecipitationCurtain[StormRenderer.MaximumCurtains];

    private int _dropCapacity;
    private int _liveDrops;
    private int _liveSplashes;
    private int _liveCurtains;
    private float _spawnCredit;
    private float _splashCredit;

    // Where the wind is now and where it is heading. State rather than settings, because the settings
    // give the range it wanders over and this is the wander.
    private float _windSpeed = float.NaN;
    private float _windDegrees;
    private float _windToSpeed;
    private float _windToDegrees;
    private float _windHold;

    // Where the level's roofs are, as a top-down raster. IsSheltered is asked once per live drop per
    // FRAME as well as once per splash per tick, and this answers each in a single array read at a
    // constant cost -- where the building-shell list it replaced needed a narrowed square, a bucketed
    // grid inside that square and a run of box tests inside the bucket, all of it to decide something
    // one texel already knows.
    private ShelterHeightField _shelter = ShelterHeightField.Empty;

    /// <summary>Gets how many drops the pool holds.</summary>
    internal int DropCapacity => _dropCapacity;

    /// <summary>Gets the drop pool; the first <see cref="LiveDrops"/> are alive.</summary>
    internal PrecipitationDrop[] Drops => _drops;

    /// <summary>Gets the number of drops currently alive.</summary>
    internal int LiveDrops => _liveDrops;

    /// <summary>Gets the splash pool; the first <see cref="LiveSplashes"/> are alive.</summary>
    internal PrecipitationSplash[] Splashes => _splashes;

    /// <summary>Gets the number of splashes currently alive.</summary>
    internal int LiveSplashes => _liveSplashes;

    /// <summary>Gets the far sheets; the first <see cref="LiveCurtains"/> are alive.</summary>
    internal PrecipitationCurtain[] Curtains => _curtains;

    /// <summary>Gets the number of far sheets currently alive.</summary>
    internal int LiveCurtains => _liveCurtains;

    /// <summary>Supplies the roofs a drop is considered sheltered underneath.</summary>
    /// <param name="shelter">The level's top-down map of what the solid world stands over.</param>
    internal void SetShelter(ShelterHeightField shelter) => _shelter = shelter ?? ShelterHeightField.Empty;

    /// <summary>Resizes the drop pool, keeping as many live drops as still fit.</summary>
    /// <param name="capacity">The new number of drops the pool holds.</param>
    internal void Resize(int capacity)
    {
        _dropCapacity = capacity;
        _drops = new PrecipitationDrop[_dropCapacity];
        _liveDrops = Math.Min(_liveDrops, _dropCapacity);
    }

    /// <summary>Drops everything alive and the spawn credit, as a storm that is switched off does.</summary>
    internal void Halt()
    {
        _liveDrops = 0;
        _liveSplashes = 0;
        _liveCurtains = 0;
        _spawnCredit = 0f;
        _splashCredit = 0f;
    }

    /// <summary>Removes every drop, splash and sheet and forgets the wind and the ground, as changing level does.</summary>
    internal void Clear()
    {
        _liveDrops = 0;
        _liveSplashes = 0;
        _liveCurtains = 0;
        _spawnCredit = 0f;
        _splashCredit = 0f;
        _windSpeed = float.NaN;
        _surface.Clear();
    }

    /// <summary>Advances the storm by one simulation tick.</summary>
    /// <param name="eyeMetres">Where the eye is now, in world metres; the spawn column follows it.</param>
    /// <param name="s">The storm's tuning.</param>
    /// <param name="ground">The flat plane rain lands at, in world metres.</param>
    /// <param name="groundCast">The world trace the landing surfaces are found with, if any.</param>
    /// <param name="unitsPerMetre">How many world units make a metre, for <paramref name="groundCast"/>.</param>
    internal void Tick(
        Vector3d eyeMetres,
        PrecipitationSettings s,
        double ground,
        WorldRayCast? groundCast,
        double unitsPerMetre)
    {
        const float Step = 1f / TicksPerSecond;
        (float windX, float windY, float windZ) = StepWind(Step, s);
        float fall = MathF.Max(0f, s.FallSpeedMetresPerSecond * s.FallSpeedScale);

        AgeSplashes(Step);
        StepCurtains(Step, eyeMetres, s, windX, windY);

        if (groundCast is { } trace)
        {
            _surface.Tick(eyeMetres, trace, unitsPerMetre, ground);
        }

        bool landing = double.IsFinite(ground) || groundCast is not null;
        SpawnSplashes(Step, eyeMetres, s, ground);

        int survivors = 0;
        for (int i = 0; i < _liveDrops; i++)
        {
            ref PrecipitationDrop drop = ref _drops[i];
            drop.Age += Step;
            if (drop.Age >= drop.Life)
            {
                continue;
            }

            // The sideways velocity is the one the drop was born with and is simply carried through;
            // only the fall is integrated, and the speed limit is applied to the fall ALONE.
            //
            // Clamping the whole vector was a bug with a very particular look: a drop was handed the wind
            // once, then gravity built the vertical until the total hit the limit, and from there every
            // further tick scaled the WHOLE velocity down to fit -- quietly eating the sideways part. The
            // rain set off leaning and straightened up in mid-air, and the harder the gust the more
            // completely it collapsed. Nothing here touches the lateral.
            (float driftX, float driftY) = DriftAt(drop.WindX, drop.WindY, drop.DriftX, drop.DriftY, drop.Age);

            // The air pushes up and down as well as along, and it accumulates with age the same way, so
            // the speed a drop settles at drifts from the one its size gave it.
            float terminal = MathF.Max(0.5f, drop.Terminal + (drop.DriftZ * drop.Age));
            drop.Velocity = new Vector3f(
                driftX,
                driftY,
                MathF.Max(-terminal, drop.Velocity.Z - (drop.Gravity * Step)));

            drop.Previous = drop.Position;
            drop.Position = new Vector3d(
                drop.Position.X + (drop.Velocity.X * Step),
                drop.Position.Y + (drop.Velocity.Y * Step),
                drop.Position.Z + (drop.Velocity.Z * Step));

            // Landing is a crossing rather than a proximity test: a fast drop covers thirty centimetres
            // in a tick, so anything that asks "is it near the ground" either misses most of them or
            // fires several ticks running for the same drop.
            // Retired where it meets the world, tested against the sampled height grid. It no longer raises
            // anything -- see SpawnSplashes -- but a drop that falls through a roof goes on being drawn
            // inside the building under it.
            if (landing)
            {
                double surface = SurfaceUnder(drop.Position.X, drop.Position.Y, ground);
                if (double.IsFinite(surface) && drop.Previous.Z > surface && drop.Position.Z <= surface)
                {
                    continue;
                }
            }

            if (survivors != i)
            {
                _drops[survivors] = drop;
            }

            survivors++;
        }

        _liveDrops = survivors;

        // Carry the fraction over rather than rounding it away: at low intensities the whole rate is
        // under one drop a tick, and rounding would spawn nothing at all.
        _spawnCredit += MathF.Max(0f, s.DropsPerSecond * Math.Clamp(s.Intensity, 0f, 1f)) * Step;
        int wanted = (int)_spawnCredit;
        _spawnCredit -= wanted;

        for (int n = 0; n < wanted && _liveDrops < _dropCapacity; n++)
        {
            Spawn(ref _drops[_liveDrops], eyeMetres, s, windX, windY, windZ, fall);
            _liveDrops++;
        }
    }

    /// <summary>Wanders the wind towards a new speed and heading, and returns where it is now.</summary>
    /// <remarks>
    /// <para>
    /// A target inside the settings' range, eased towards, and replaced before it is reached. What that
    /// gives is rain whose lean is never quite the same twice and never changes abruptly either — the
    /// two things a single fixed wind speed cannot do. A step change would be worse than no wind at all:
    /// the whole column shears in one frame, which reads as the camera being knocked rather than as air.
    /// </para>
    /// <para>
    /// The easing is per second and independent of the target, so an unusually large change takes
    /// proportionally longer rather than snapping — and the hold before the next target is itself
    /// randomised, so the wind has no period for the eye to lock onto.
    /// </para>
    /// </remarks>
    private (float X, float Y, float Z) StepWind(float step, PrecipitationSettings s)
    {
        float mean = s.WindMetresPerSecond;
        float gust = MathF.Max(0f, s.WindGustMetresPerSecond);
        float swing = MathF.Max(0f, s.WindSwingDegrees);

        // Started at the mean rather than at zero, so the first seconds of a storm are not a dead calm
        // that then picks up for no reason anyone can see.
        if (!float.IsFinite(_windSpeed))
        {
            _windSpeed = mean;
            _windDegrees = s.WindDirectionDegrees;
            _windHold = 0f;
        }

        _windHold -= step;
        if (_windHold <= 0f)
        {
            // Two uniforms summed, which is a triangular distribution: signed about the mean, and far
            // more likely to land near it than at either extreme. A flat range between a floor and a
            // ceiling never crosses back through vertical and spends as long at its limits as anywhere
            // else, which is what made the lean look bolted on.
            float signed = ((float)_random.NextDouble() + (float)_random.NextDouble()) - 1f;
            _windToSpeed = mean + (signed * gust);
            _windToDegrees = s.WindDirectionDegrees + (((float)_random.NextDouble() * 2f) - 1f) * swing;
            _windHold = MathF.Max(0.5f, s.WindVariationSeconds) * (0.6f + ((float)_random.NextDouble() * 0.8f));
        }

        float ease = Math.Clamp(step / MathF.Max(0.5f, s.WindVariationSeconds), 0f, 1f);
        _windSpeed += (_windToSpeed - _windSpeed) * ease;
        _windDegrees += (_windToDegrees - _windDegrees) * ease;

        return s.WindVelocity(_windSpeed, _windDegrees);
    }

    /// <summary>A cheap deterministic hash of a lattice point, which <see cref="SmoothNoise"/> is built from.</summary>
    /// <remarks>
    /// <para>
    /// Internal so the gust shape can be asserted on directly: a field that is accidentally constant
    /// makes the storm look even rather than broken, which is not a difference anyone spots by eye.
    /// </para>
    /// <para>
    /// <b>Integers, not a sine.</b> This was the usual <c>frac(sin(dot(p, k)) * 43758)</c>, which costs a
    /// transcendental. Twelve thousand drops each taking two smoothed samples is a hundred and ninety
    /// thousand sines a frame, which is milliseconds of CPU spent on a field nobody can point at — and
    /// the sine's own periodicity puts visible structure in it at large coordinates, where the world
    /// actually is. A bit mix is a handful of integer operations and better distributed.
    /// </para>
    /// </remarks>
    internal static float Noise(float x, float y, float z)
    {
        unchecked
        {
            uint hash = (uint)((int)MathF.Floor(x) * 73856093)
                ^ (uint)((int)MathF.Floor(y) * 19349663)
                ^ (uint)((int)MathF.Floor(z) * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5BD1E995u;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFFu) * (1f / 0xFFFFFFu);
        }
    }

    /// <summary>A smooth value noise in [0, 1]: the hash on a unit lattice, interpolated between.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all.</b> The gust field was the raw hash, sampled at the drop's own
    /// position. A hash has no correlation length: two drops a hand's breadth apart get unrelated
    /// values, so what was meant to arrive as sheets of rain came out as per-drop random opacity —
    /// invisible as sheets, and visible as the storm having a slightly dirty, speckled quality. A gust
    /// field has to be smooth: neighbouring drops must agree, or there is no sheet.
    /// </para>
    /// <para>
    /// The same is true of the glimmer for a different reason. Sampled from a hash on a coordinate that
    /// moves with time, a drop's flash is a fresh random number every frame — a strobe rather than a
    /// glint. Interpolated, it ramps up and back down over the several frames it takes the coordinate
    /// to cross a lattice cell, which is what a drop catching the light actually does.
    /// </para>
    /// </remarks>
    internal static float SmoothNoise(float x, float y, float z)
    {
        float fx = MathF.Floor(x), fy = MathF.Floor(y), fz = MathF.Floor(z);
        float tx = Fade(x - fx), ty = Fade(y - fy), tz = Fade(z - fz);

        float c000 = Noise(fx, fy, fz);
        float c100 = Noise(fx + 1f, fy, fz);
        float c010 = Noise(fx, fy + 1f, fz);
        float c110 = Noise(fx + 1f, fy + 1f, fz);
        float c001 = Noise(fx, fy, fz + 1f);
        float c101 = Noise(fx + 1f, fy, fz + 1f);
        float c011 = Noise(fx, fy + 1f, fz + 1f);
        float c111 = Noise(fx + 1f, fy + 1f, fz + 1f);

        float x00 = c000 + ((c100 - c000) * tx);
        float x10 = c010 + ((c110 - c010) * tx);
        float x01 = c001 + ((c101 - c001) * tx);
        float x11 = c011 + ((c111 - c011) * tx);
        float y0 = x00 + ((x10 - x00) * ty);
        float y1 = x01 + ((x11 - x01) * ty);
        return y0 + ((y1 - y0) * tz);

        // Smoothstep on the cell fraction. Interpolating linearly leaves a crease at every lattice
        // plane, and a crease in a gust field is a straight edge crossing the storm.
        static float Fade(float t) => t * t * (3f - (2f * t));
    }

    private void Spawn(
        ref PrecipitationDrop drop,
        Vector3d eye,
        PrecipitationSettings s,
        float windX,
        float windY,
        float windZ,
        float fall)
    {
        // Anywhere over the disc around the eye: the square root spreads the drops evenly over its area
        // instead of crowding them at its middle.
        float radius = MathF.Sqrt((float)_random.NextDouble()) * MathF.Max(0f, s.SpawnRadiusMetres);
        float bearing = (float)_random.NextDouble() * MathF.Tau;
        float height = s.SpawnAboveCameraMetres + ((float)_random.NextDouble() * s.SpawnHeightMetres);

        drop.Position = new Vector3d(
            eye.X + (MathF.Cos(bearing) * radius),
            eye.Y + (MathF.Sin(bearing) * radius),
            eye.Z + height);
        drop.Previous = drop.Position;

        // The wind it is born in, kept for life; see DriftAt.
        drop.WindX = windX;
        drop.WindY = windY;

        (drop.DriftX, drop.DriftY, drop.DriftZ) = SpawnDrift(
            s.SpreadMetresPerSecondSquared,
            (float)_random.NextDouble(),
            (float)_random.NextDouble(),
            (float)_random.NextDouble());

        drop.Scale = SmallestDropScale + ((float)_random.NextDouble() * (1f - SmallestDropScale));
        drop.Terminal = TerminalFor(fall, drop.Scale);

        // Born already falling at its own terminal speed, so it falls in a straight line from the start.
        drop.Velocity = new Vector3f(0f, 0f, windZ - drop.Terminal);

        float pull = 1f + ((((float)_random.NextDouble() * 2f) - 1f) * GravitySpread);
        drop.Gravity = MathF.Max(0f, s.GravityMetresPerSecondSquared) * pull;

        float share = ShortestLifeShare + ((float)_random.NextDouble() * (1f - ShortestLifeShare));
        drop.Life = MathF.Max(0f, s.LifetimeSeconds) * share;

        drop.Seed = (float)_random.NextDouble();
        drop.Cell = _random.Next(StormRenderer.SheetCells);
        drop.Age = 0f;
    }

    /// <summary>The acceleration the air gives a drop, in metres per second squared.</summary>
    /// <param name="spread">The strongest push the air may give, from the settings.</param>
    /// <param name="magnitudeUnit">A random number, zero to one, for how hard this drop is pushed.</param>
    /// <param name="angleUnit">A random number, zero to one, for its compass direction.</param>
    /// <param name="elevationUnit">A random number, zero to one, for how far above or below level.</param>
    /// <returns>The acceleration on all three axes.</returns>
    /// <remarks>
    /// <para>
    /// Three dimensions, not two. A push confined to the horizontal plane leaves the fall looking the
    /// same from above as from the side, and every drop's streak the same length: the whole variety of
    /// it then lives in one plane, which is exactly what "it looks the same at every viewing angle"
    /// describes. Air moves up and down as well as along.
    /// </para>
    /// <para>
    /// A direction and a magnitude rather than a number per axis, and the direction is even over the
    /// SPHERE — the elevation is drawn as a cosine rather than as an angle, because drawing the angle
    /// evenly crowds the poles. The magnitude takes a cube root so that the magnitudes fill the ball's
    /// VOLUME evenly instead of crowding its middle.
    /// </para>
    /// </remarks>
    internal static (float X, float Y, float Z) SpawnDrift(
        float spread,
        float magnitudeUnit,
        float angleUnit,
        float elevationUnit)
    {
        if (!(spread > 0f))
        {
            return (0f, 0f, 0f);
        }

        float magnitude = spread * MathF.Cbrt(Math.Clamp(magnitudeUnit, 0f, 1f));
        float bearing = Math.Clamp(angleUnit, 0f, 1f) * MathF.Tau;
        float cosine = (Math.Clamp(elevationUnit, 0f, 1f) * 2f) - 1f;
        float sine = MathF.Sqrt(MathF.Max(0f, 1f - (cosine * cosine)));

        return (
            MathF.Cos(bearing) * sine * magnitude,
            MathF.Sin(bearing) * sine * magnitude,
            cosine * magnitude);
    }

    /// <summary>The sideways velocity a drop has drifted to by a given age, in metres per second.</summary>
    /// <param name="windX">The wind the drop was born in, east.</param>
    /// <param name="windY">The wind the drop was born in, north.</param>
    /// <param name="driftX">The drop's own acceleration, east.</param>
    /// <param name="driftY">The drop's own acceleration, north.</param>
    /// <param name="ageSeconds">How long it has been falling.</param>
    /// <returns>The horizontal velocity.</returns>
    /// <remarks>
    /// The wind is the same for every drop born at that moment and the drift is the drop's own, so what
    /// separates two streaks is how long each has been falling. This is why the fall shows a range of
    /// angles at all: it is a population of ages, not a population of velocities.
    /// </remarks>
    internal static (float X, float Y) DriftAt(
        float windX,
        float windY,
        float driftX,
        float driftY,
        float ageSeconds)
    {
        float age = MathF.Max(0f, ageSeconds);
        return (windX + (driftX * age), windY + (driftY * age));
    }

    /// <summary>How fast a drop of a given size settles to falling, in metres per second.</summary>
    /// <param name="fallMetresPerSecond">The settings' fall speed, which the largest drop reaches.</param>
    /// <param name="scale">The drop's size, from the smallest share up to one.</param>
    /// <returns>The speed, never below half a metre a second.</returns>
    /// <remarks>
    /// Straight-line in size: the largest drop falls at the full speed and the smallest at half of it.
    /// Small drops falling slower is what lets one sideways push lean them further than the big ones,
    /// which is a good part of why the column does not read as parallel lines.
    /// </remarks>
    internal static float TerminalFor(float fallMetresPerSecond, float scale)
    {
        float share = 0.5f + (0.5f * (scale - SmallestDropScale) / (1f - SmallestDropScale));
        return MathF.Max(0.5f, fallMetresPerSecond * share);
    }

    /// <summary>Raises this tick's splashes on the ground around the eye.</summary>
    /// <remarks>
    /// See <see cref="PrecipitationSettings.SplashesPerSecond"/> for why these are spawned here at a
    /// rate rather than where a drop lands. The short version: a drop's landing is almost never
    /// somewhere the player is looking, and a storm whose ground detail is correct and invisible is
    /// worth less than one whose ground detail is approximate and there.
    /// </remarks>
    private void SpawnSplashes(float step, Vector3d eye, PrecipitationSettings s, double ground)
    {
        if (!s.Splashes || !double.IsFinite(ground))
        {
            return;
        }

        _splashCredit += MathF.Max(0f, s.SplashesPerSecond * Math.Clamp(s.Intensity, 0f, 1f)) * step;
        int wanted = (int)_splashCredit;
        _splashCredit -= wanted;

        float reach = MathF.Max(s.SplashReachMetres, 1f);
        for (int n = 0; n < wanted; n++)
        {
            float radius = MathF.Sqrt((float)_random.NextDouble()) * reach;
            float bearing = (float)_random.NextDouble() * MathF.Tau;
            double x = eye.X + (MathF.Cos(bearing) * radius);
            double y = eye.Y + (MathF.Sin(bearing) * radius);

            double surface = SurfaceUnder(x, y, ground);
            if (!double.IsFinite(surface))
            {
                continue;
            }

            if (s.ShelterHeightMetres > 0f && IsSheltered(x, y, surface, s.ShelterHeightMetres))
            {
                continue;
            }

            if (_random.NextDouble() < s.SplashSmallFraction)
            {
                RaiseDroplet(x, y, surface, s);
                RaiseDroplet(x, y, surface, s);
                continue;
            }

            ref PrecipitationSplash crown = ref _splashes[Claim()];
            crown.X = x;
            crown.Y = y;
            crown.Z = surface + s.SplashLiftMetres;
            crown.Velocity = default;
            crown.Age = 0f;
            crown.Life = MathF.Max(0f, s.SplashLifetimeSeconds)
                * (ShortestCrownLifeShare + ((float)_random.NextDouble() * (1f - ShortestCrownLifeShare)));
            crown.Radius = MathF.Max(
                0.005f,
                s.SplashRadiusMetres * (SmallestCrownShare + ((float)_random.NextDouble() * (1f - SmallestCrownShare))));

            float pick = (float)_random.NextDouble();
            crown.Variant = pick < FirstVariantShare ? 0 : pick < SecondVariantShare ? 1 : 2;
            crown.Small = false;
            crown.FlipU = _random.NextDouble() < 0.5;
        }
    }

    /// <summary>Throws one small droplet up out of an impact.</summary>
    /// <remarks>
    /// Some die before they have risen at all and some arc over and fall back: the spread of lives is wide
    /// on purpose, because a field of droplets that all live as long reads as a pulse.
    /// </remarks>
    private void RaiseDroplet(double x, double y, double surface, PrecipitationSettings s)
    {
        ref PrecipitationSplash droplet = ref _splashes[Claim()];
        float bearing = (float)_random.NextDouble() * MathF.Tau;
        float side = (float)_random.NextDouble() * DropletSideSpeed;
        float up = DropletUpSpeed * (1f + ((float)_random.NextDouble() * DropletUpSpread));

        droplet.X = x;
        droplet.Y = y;
        droplet.Z = surface + s.SplashLiftMetres;
        droplet.Velocity = new Vector3f(MathF.Cos(bearing) * side, MathF.Sin(bearing) * side, up);
        droplet.Age = 0f;
        droplet.Life = DropletShortestLife + ((float)_random.NextDouble() * (DropletLongestLife - DropletShortestLife));
        droplet.Radius = s.SplashRadiusMetres * DropletSizeShare
            * (DropletSizeLow + ((float)_random.NextDouble() * (DropletSizeHigh - DropletSizeLow)));
        droplet.Variant = 0;
        droplet.Small = true;
        droplet.FlipU = _random.NextDouble() < 0.5;
    }

    /// <summary>Takes a splash slot, replacing an arbitrary live one once the pool is full.</summary>
    /// <remarks>
    /// Oldest-first would be tidier and is not worth the bookkeeping: a splash is a fraction of a second,
    /// so whichever one is replaced was nearly over anyway.
    /// </remarks>
    private int Claim() => _liveSplashes < StormRenderer.MaximumSplashes ? _liveSplashes++ : _random.Next(StormRenderer.MaximumSplashes);

    private void AgeSplashes(float step)
    {
        int survivors = 0;
        for (int i = 0; i < _liveSplashes; i++)
        {
            ref PrecipitationSplash splash = ref _splashes[i];
            splash.Age += step;
            if (splash.Age >= splash.Life)
            {
                continue;
            }

            // A droplet flies: pulled down, slowed by the air, moved. A crown stands where it struck.
            if (splash.Small)
            {
                Vector3f velocity = splash.Velocity;
                velocity = new Vector3f(velocity.X, velocity.Y, velocity.Z - (DropletGravity * step));
                float keep = MathF.Max(0f, 1f - (DropletDrag * step));
                velocity = new Vector3f(velocity.X * keep, velocity.Y * keep, velocity.Z * keep);
                splash.Velocity = velocity;
                splash.X += velocity.X * step;
                splash.Y += velocity.Y * step;
                splash.Z += velocity.Z * step;
            }

            if (survivors != i)
            {
                _splashes[survivors] = splash;
            }

            survivors++;
        }

        _liveSplashes = survivors;
    }

    private void StepCurtains(float step, Vector3d eye, PrecipitationSettings s, float windX, float windY)
    {
        if (!s.Curtains || s.CurtainCount <= 0)
        {
            _liveCurtains = 0;
            return;
        }

        int wanted = Math.Clamp(s.CurtainCount, 0, StormRenderer.MaximumCurtains);
        float reach = CurtainReach(s);

        for (int i = 0; i < wanted; i++)
        {
            ref PrecipitationCurtain curtain = ref _curtains[i];
            if (i >= _liveCurtains)
            {
                PlaceCurtain(ref curtain, eye, reach);
                continue;
            }

            // The sheets drift with the wind rather than falling, and are recycled around the eye when
            // they wander out of reach -- they are the far haze, so they only have to be somewhere out
            // there, not to have come from anywhere in particular.
            curtain.X += (windX * s.CurtainMetresPerSecond * 0.1f) * step;
            curtain.Y += (windY * s.CurtainMetresPerSecond * 0.1f) * step;
            curtain.Z -= s.CurtainMetresPerSecond * 0.25f * step;

            double dx = curtain.X - eye.X;
            double dy = curtain.Y - eye.Y;
            if ((dx * dx) + (dy * dy) > reach * reach || curtain.Z < eye.Z - reach)
            {
                PlaceCurtain(ref curtain, eye, reach);
            }
        }

        _liveCurtains = wanted;
    }

    private void PlaceCurtain(ref PrecipitationCurtain curtain, Vector3d eye, float reach)
    {
        float angle = (float)_random.NextDouble() * MathF.Tau;

        // Never right on top of the eye: a sheet the camera is inside is a wall of fog, not weather.
        float radius = reach * (0.35f + ((float)_random.NextDouble() * 0.6f));
        curtain.X = eye.X + (MathF.Cos(angle) * radius);
        curtain.Y = eye.Y + (MathF.Sin(angle) * radius);

        // Below the eye, not above it. A sheet's opacity falls away as the direction back to the camera
        // turns upwards, so rain too far off to resolve reads as haze lying over the ground; the same haze
        // painted across the sky above the player is a cloud that has come indoors.
        curtain.Z = eye.Z + ((((float)_random.NextDouble() * 0.5f) - 0.45f) * reach);
        curtain.Seed = (float)_random.NextDouble();
    }

    internal static float CurtainReach(PrecipitationSettings s) =>
        MathF.Max(s.SpawnRadiusMetres, 1f) * MathF.Max(s.CurtainReachScale, 1.1f);

    /// <summary>The height of what the rain lands on at a point, or the flat plane if nothing is known.</summary>
    private double SurfaceUnder(double x, double y, double fallback)
    {
        double sampled = _surface.At(x, y);
        return double.IsFinite(sampled) ? sampled : fallback;
    }

    /// <summary>Reports whether a roof stands between a point and the sky.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y, in metres.</param>
    /// <param name="z">The world Z, in metres.</param>
    /// <param name="searchMetres">How far above the point to look for the roof.</param>
    /// <remarks>
    /// One read of the level's roof raster. What it replaced was the bounding box of every task named
    /// <c>Building_</c>, which said the ground was sheltered wherever one of those boxes reached -- and
    /// a building standing at an angle to the world axes fills barely a third of its own box, so the
    /// rain stopped over the open apron in front of it while going on falling through the roof of
    /// anything the level had not labelled a building.
    /// </remarks>
    internal bool IsSheltered(double x, double y, double z, float searchMetres) =>
        _shelter.IsSheltered(x, y, z, ShelterHeightField.SurfaceHeadroomMetres, searchMetres);
}
