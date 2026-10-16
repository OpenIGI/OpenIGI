using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// I.G.I. 2's water module, <c>h/2/water.c</c>: every body of water a level makes (<c>Water_ptQTaskList</c>, in the order they are
/// made), the questions asked of them — how deep a point lies (<c>Water_IsUnderWater</c>) — and the ripples and splashes that
/// steps, bullets and blasts leave on them.
/// </summary>
/// <remarks>
/// <para>
/// The surface is logic, not only looks: its height under a point is the sum of its layers' sine trains and of the ripples
/// still spreading over it (<see cref="Igi2WaterBody.Height"/>), and that height decides whether a human wades, swims or stands
/// dry. The layers move with the flow's clock (<c>Water_vTime</c>, a sixtieth of a second a flow tick).
/// </para>
/// <para>
/// The module keeps its own random stream (<c>Water_tRandomState</c>), seeded once as the module opens, which only a blast's
/// spray draws from; and two throttles shared by every body, one for the ripples of a wading human and one for those of
/// bullets.
/// </para>
/// </remarks>
public sealed class Igi2Water
{
    /// <summary>The water's own query group, <c>QTask_SetQTaskTypeGroup(Water_eQTaskType, 0x40000000)</c>.</summary>
    public const uint Group = 0x40000000;

    /// <summary>How high above a body's level a point still asks it for its depth, ten metres.</summary>
    private const double DepthCeiling = 40960.0;

    /// <summary>The square of the distance a wading human must move for a fresh ripple before the last has half spread, 3 m.</summary>
    private const double MovementRippleSpacing = 150994944.0;

    /// <summary>The square of the distance a bullet's splash must land from the last for a fresh ripple before it has half spread.</summary>
    private const double BulletRippleSpacing = 37748736.0;

    private readonly List<Igi2WaterBody> _bodies = [];
    private Vector3d _lastMovement;
    private int _lastMovementTick;
    private int _lastMovementHalf;
    private Vector3d _lastBullet;
    private int _lastBulletTick;
    private int _lastBulletHalf;

    /// <summary>Gets the module's random stream, <c>Water_tRandomState</c>, seeded as <c>Water_Open</c> seeds it.</summary>
    public IgiRandom Random { get; } = new(unchecked((uint)-925902492), unchecked((uint)-1684277418), 376075981u);

    /// <summary>Gets or sets the flow's tick count, which the layers' clock reads (<c>Timer_nTicks</c>).</summary>
    public Func<int> FlowTicks { get; set; } = static () => 0;

    /// <summary>Gets the bodies of water, in the order they were made.</summary>
    public IReadOnlyList<Igi2WaterBody> Bodies => _bodies;

    /// <summary>Gets the layers' clock, <c>Water_vTime</c>: the flow's ticks over sixty, as a float.</summary>
    public float Time => ((Fpu)(double)FlowTicks() * 0.016666666666666666).F;

    /// <summary>Forgets the level's water, as each body's delete handler takes it off the list.</summary>
    public void Clear() => _bodies.Clear();

    /// <summary>Lists a body as it is made, <c>Water_CreateHandler</c>.</summary>
    /// <param name="water">The water task.</param>
    /// <returns>Its body.</returns>
    public Igi2WaterBody Add(WaterTask water)
    {
        ArgumentNullException.ThrowIfNull(water);
        Igi2WaterBody body = new(this, water);
        _bodies.Add(body);
        return body;
    }

    /// <summary>
    /// How deep a point lies in the water, <c>Water_IsUnderWater</c>: the first body, in the list's order, whose square holds the
    /// point and whose surface is above it, without its distance fade.
    /// </summary>
    /// <param name="position">The point.</param>
    /// <param name="depth">How far the surface is above it, when it is under.</param>
    /// <param name="index">The body's place in the list, or -1.</param>
    /// <returns>Whether the point is under water.</returns>
    public bool IsUnderWater(Vector3d position, out float depth, out int index)
    {
        for (int i = 0; i < _bodies.Count; i++)
        {
            Igi2WaterBody body = _bodies[i];
            Vector3d at = body.Position;
            Fpu radius = (Fpu)body.Size * 4096.0;
            if (((Fpu)at.Z + DepthCeiling).Value <= position.Z
                || ((Fpu)at.X - radius).Value >= position.X || (radius + at.X).Value <= position.X
                || ((Fpu)at.Y - radius).Value >= position.Y || (radius + at.Y).Value <= position.Y)
            {
                continue;
            }

            float z = ((Fpu)position.Z - at.Z).F;
            float y = ((Fpu)position.Y - at.Y).F;
            float x = ((Fpu)position.X - at.X).F;
            float under = body.Height(x, y, z, 0f, out _);
            if (under > 0f)
            {
                depth = under;
                index = i;
                return true;
            }
        }

        depth = 0f;
        index = -1;
        return false;
    }

    /// <summary>
    /// A wading or swimming human ripples the water, <c>Water_PlayerMovementEffect</c>: a ring a running one makes twice as wide and
    /// high, unless the last ring was made within three metres and has not yet half spread.
    /// </summary>
    /// <param name="position">Where the human is.</param>
    /// <param name="index">The body it is in, as <see cref="IsUnderWater"/> named it.</param>
    /// <param name="isRunning">Whether it runs.</param>
    public void PlayerMovementEffect(Vector3d position, int index, bool isRunning)
    {
        if ((uint)index >= (uint)_bodies.Count)
        {
            return;
        }

        Igi2WaterBody body = _bodies[index];
        Fpu dz = (Fpu)position.Z - _lastMovement.Z;
        Fpu dy = (Fpu)position.Y - _lastMovement.Y;
        Fpu dx = (Fpu)position.X - _lastMovement.X;
        if (!((((dz * dz) + (dy * dy)) + (dx * dx)).Value > MovementRippleSpacing) && body.Tick <= _lastMovementTick + _lastMovementHalf)
        {
            return;
        }

        Fpu scale = (Fpu)(double)(isRunning ? 1 : 0) + 1.0;
        float radius = (scale * 16384.0).F;
        float height = (scale * 204.8).F;
        int duration = body.AddWave(position, height, radius, 6144f);
        _lastMovementTick = body.Tick;
        _lastMovementHalf = duration >> 1;
        _lastMovement = position;
    }

    /// <summary>
    /// A bullet strikes the water, <c>Water_GunShotEffect</c> (not a blast): its splash, sized by the square root of the penetration
    /// it had left, and, when it still had more than two and lands three metres from the last bullet's ring or after that has half
    /// spread, a ring of its own.
    /// </summary>
    /// <param name="body">The body struck.</param>
    /// <param name="position">Where it struck.</param>
    /// <param name="penetration">The penetration it had left.</param>
    /// <param name="splashTexture">The splash's texture, the effect's.</param>
    public void BulletEffect(Igi2WaterBody body, Vector3d position, float penetration, string? splashTexture)
    {
        ArgumentNullException.ThrowIfNull(body);
        Igi2WaterSplashDrop drop = body.AddSplash();
        drop.Position = new Vector3f((float)position.X, (float)position.Y, (float)position.Z);
        drop.StartTick = body.Tick;
        Fpu dz = (Fpu)position.Z - _lastBullet.Z;
        Fpu dy = (Fpu)position.Y - _lastBullet.Y;
        Fpu dx = (Fpu)position.X - _lastBullet.X;
        if (penetration > 2f
            && ((((dz * dz) + (dy * dy)) + (dx * dx)).Value > BulletRippleSpacing || body.Tick > _lastBulletTick + _lastBulletHalf))
        {
            int duration = body.AddWave(position, 409.6000061035156f, 20480f, 0f);
            _lastBulletTick = body.Tick;
            _lastBulletHalf = duration >> 1;
            _lastBullet = position;
        }

        drop.IsSpray = false;
        drop.StartAcross = 0.005f;
        drop.StartUp = 0.05f;
        drop.Scale = 1f;
        drop.Alpha = 1f;
        drop.Across = 0f;
        drop.Brightness = 1f;
        drop.Texture = splashTexture;
        float up = (Fpu.Sqrt(penetration) * 1.2000000476837158).F;
        drop.SpeedUp = up;
        drop.SpeedAcross = ((Fpu)up * 0.019999999552965164).F;
    }

    /// <summary>
    /// A blast under water, <c>Water_GunShotEffect</c> for an explosion: seven columns of spray thrown up one after the other, each
    /// drawing its spread and its height from the module's stream, and a ring as wide as the body is large.
    /// </summary>
    /// <param name="index">The body the blast is in, as <see cref="IsUnderWater"/> named it.</param>
    /// <param name="position">Where it went off.</param>
    /// <remarks>Retail asks the surface's height at the blast's world X and Y, not at its place on the body; that is kept.</remarks>
    public void ExplosionEffect(int index, Vector3d position)
    {
        if ((uint)index >= (uint)_bodies.Count)
        {
            return;
        }

        Igi2WaterBody body = _bodies[index];
        for (int k = 0; k < 7; k++)
        {
            Igi2WaterSplashDrop drop = body.AddSplash();
            float surface = body.Height((float)position.X, (float)position.Y, 0f, 0f, out _);
            drop.IsSpray = true;
            drop.Position = new Vector3f(
                (float)position.X,
                (float)position.Y,
                ((((Fpu)surface + body.Position.Z) - 2048.0)).F);
            drop.Across = ((Fpu)(double)k * 819.2000122070312).F;
            drop.StartTick = body.Tick + (8 * k);
            drop.SpeedAcross = (((((Fpu)0.5 - ((double)(ushort)Random.NextRaw() * IgiRandom.Scale)) * 0.5) + 1.0) * 0.05000000074505806).F;
            double second = (ushort)Random.NextRaw();
            drop.Scale = 1f;
            drop.Alpha = 1f;
            drop.StartAcross = 3f;
            drop.StartUp = 0f;
            drop.Texture = Igi2WaterSplashDrop.ExplosionTexture;
            drop.SpeedUp = (((((Fpu)0.5 - (second * IgiRandom.Scale)) * 0.30000001192092896) + 1.0) * 16.0).F;
            drop.Brightness = ((Fpu)1.0 - (double)(k / 6)).F;
        }

        if (body.Size <= 10f)
        {
            body.AddWave(position, 1228.800048828125f, 40960f, 0f);
        }
        else
        {
            body.AddWave(position, 6144f, 81920f, 0f);
        }
    }
}

/// <summary>A ripple spreading over a body of water, <c>Wave_t</c> as <c>Water_AddWave</c> fills it.</summary>
public sealed class Igi2WaterRipple
{
    /// <summary>Gets or sets where it started, in the world.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets where it started, from the body's centre.</summary>
    public Vector3f Local { get; set; }

    /// <summary>Gets or sets how near its centre it does not lift the surface.</summary>
    public float Dead { get; set; }

    /// <summary>Gets or sets its height as it starts.</summary>
    public float StartHeight { get; set; }

    /// <summary>Gets or sets how far it spreads.</summary>
    public float Reach { get; set; }

    /// <summary>Gets or sets its height now, falling to nothing as it spreads.</summary>
    public float Height { get; set; }

    /// <summary>Gets or sets its outer edge now.</summary>
    public float Outer { get; set; }

    /// <summary>Gets or sets its inner edge now.</summary>
    public float Inner { get; set; }

    /// <summary>Gets or sets half its width, a fifth of its reach.</summary>
    public float HalfWidth { get; set; }

    /// <summary>Gets or sets the body's tick it started on.</summary>
    public int StartTick { get; set; }

    /// <summary>Gets or sets how many ticks it spreads for, 150.</summary>
    public int Duration { get; set; }
}

/// <summary>
/// A splash on a body of water, an element of <c>Water_t.ptBulletSplashVector</c>: thrown up and out from where it began, it
/// rises and falls under half of gravity until it is back under (<c>Water_RunHandler</c>), and is drawn as a sprite.
/// </summary>
public sealed class Igi2WaterSplashDrop
{
    /// <summary>The texture a blast's spray is drawn with, <c>tWaterExplosionTexture</c>.</summary>
    public const string ExplosionTexture = "water_explosion";

    /// <summary>Gets or sets where it began.</summary>
    public Vector3f Position { get; set; }

    /// <summary>Gets or sets how fast it moves out, a tick.</summary>
    public float SpeedAcross { get; set; }

    /// <summary>Gets or sets how fast it is thrown up.</summary>
    public float SpeedUp { get; set; }

    /// <summary>Gets or sets how far out it is now.</summary>
    public float CurrentAcross { get; set; }

    /// <summary>Gets or sets how high it is now.</summary>
    public float CurrentUp { get; set; }

    /// <summary>Gets or sets how far out it started.</summary>
    public float StartAcross { get; set; }

    /// <summary>Gets or sets how high it started.</summary>
    public float StartUp { get; set; }

    /// <summary>Gets or sets the texture it is drawn with.</summary>
    public string? Texture { get; set; }

    /// <summary>Gets or sets a value indicating whether it is a blast's spray, which fades as it falls.</summary>
    public bool IsSpray { get; set; }

    /// <summary>Gets or sets how far round its column it sits.</summary>
    public float Across { get; set; }

    /// <summary>Gets or sets how bright it is drawn.</summary>
    public float Brightness { get; set; }

    /// <summary>Gets or sets its size.</summary>
    public float Scale { get; set; }

    /// <summary>Gets or sets how opaque it is drawn.</summary>
    public float Alpha { get; set; }

    /// <summary>Gets or sets the body's tick it starts on.</summary>
    public int StartTick { get; set; }
}

/// <summary>
/// One body of water as the level runs it, a <c>Water_t</c>: its layers, the ripples spreading on it, its splashes and its tick,
/// and the surface lines strike (<c>Water_TraceLinesHandler</c>).
/// </summary>
public sealed class Igi2WaterBody : IIgi2Collider
{
    /// <summary>The ripples a body keeps at once, five; a sixth replaces the oldest.</summary>
    public const int MaxWaves = 5;

    /// <summary>The ticks a ripple spreads for, <c>150</c>.</summary>
    public const int WaveDuration = 150;

    /// <summary>The most layers summed, four.</summary>
    public const int MaxLayers = 4;

    /// <summary>The level material row the surface is, <c>World_ptActiveWorld->aptQMaterial[2]</c>.</summary>
    public const int SurfaceMaterial = 2;

    private readonly Igi2Water _module;
    private readonly WaterTask _task;
    private readonly List<Igi2WaterRipple> _waves = [];
    private readonly List<Igi2WaterSplashDrop> _splashes = [];
    private (float Frequency, float Amplitude, float Speed, float Sin, float Cos)[] _layers = [];
    private float _sampleScale;
    private float _offset;
    private float _scaledFadeOut;
    private float _minX = float.MaxValue;
    private float _minY = float.MaxValue;
    private float _maxX = float.MinValue;
    private float _maxY = float.MinValue;

    /// <summary>Initializes a new instance of the <see cref="Igi2WaterBody"/> class.</summary>
    /// <param name="module">The module.</param>
    /// <param name="task">The water task.</param>
    internal Igi2WaterBody(Igi2Water module, WaterTask task)
    {
        _module = module;
        _task = task;
        Refresh();
    }

    /// <inheritdoc/>
    public object Owner => _task;

    /// <summary>Gets the module the body belongs to.</summary>
    public Igi2Water Module => _module;

    /// <summary>Gets the water task.</summary>
    public WaterTask Task => _task;

    /// <summary>Gets where its surface's centre lies.</summary>
    public Vector3d Position => _task.Position;

    /// <summary>Gets its size, in metres, <c>vRadius</c>: half the side of its square.</summary>
    public float Size => _task.Size;

    /// <summary>Gets its tick, <c>nTick</c>, which its run counts and its ripples are timed by.</summary>
    public int Tick { get; private set; }

    /// <summary>Gets the ripples spreading on it, oldest first.</summary>
    public IReadOnlyList<Igi2WaterRipple> Waves => _waves;

    /// <summary>Gets its splashes.</summary>
    public IReadOnlyList<Igi2WaterSplashDrop> Splashes => _splashes;

    /// <summary>Gets the radius its object's sphere is registered with, its size in units.</summary>
    public double BoundingRadius => ((Fpu)_task.Size * 4096.0).Value;

    /// <summary>
    /// Reads its parameters and its layers' again, <c>Water_UpdateInternalDataHandler</c> and
    /// <c>WaterLayer_UpdateInternalDataHandler</c>: the fade per unit, the phase offset of twice its size, each layer's wave number,
    /// height and direction, and the step the surface's slope is sampled over (<c>Water_SetVertexFunctionParameters</c>).
    /// </summary>
    public void Refresh()
    {
        _scaledFadeOut = ((Fpu)_task.FadeOut * 0.000244140625).F;
        _offset = ((Fpu)_task.Size * 8192.0).F;
        List<(float, float, float, float, float)> layers = [];
        Fpu wavelengthTotal = (Fpu)0.0;
        foreach (Engine.Tasks.GameTask child in _task.Children)
        {
            if (child is not WaterLayerTask layer || layers.Count >= MaxLayers)
            {
                continue;
            }

            Fpu angle = (Fpu)layer.Angle * 0.01745329238474369;
            float sin = Fpu.Sin(angle).F;
            float cos = Fpu.Cos(angle).F;
            float frequency = ((Fpu)6.2831854820251465 / ((Fpu)layer.Frequency * 4096.0)).F;
            float amplitude = ((Fpu)layer.Amplitude * 4096.0).F;
            layers.Add((frequency, amplitude, layer.Speed, sin, cos));
            wavelengthTotal += layer.Frequency;
        }

        _layers = [.. layers];
        _sampleScale = ((wavelengthTotal / (double)_layers.Length) * 40.959999084472656).F;
    }

    /// <summary>
    /// The surface over a point of the body, <c>Water_VertexFunction</c>: each layer's sine train, faded with distance, plus every
    /// ripple whose ring holds the point; and the slope, sampled a step east and a step north.
    /// </summary>
    /// <param name="x">The point's X from the body's centre.</param>
    /// <param name="y">Its Y.</param>
    /// <param name="z">Its Z from the body's level.</param>
    /// <param name="distance">How far it is seen from, which fades the layers; nought for none.</param>
    /// <param name="normal">The surface's unit normal there.</param>
    /// <returns>How far the surface is above the point.</returns>
    public float Height(float x, float y, float z, float distance, out Vector3f normal)
    {
        Fpu fade = (Fpu)distance * _scaledFadeOut;
        if (fade.Value <= 1.0)
        {
            fade = (Fpu)1.0;
        }

        float attenuation = ((Fpu)1.0 / fade).F;
        float time = _module.Time;
        float sample = _sampleScale;
        Fpu here = (Fpu)0.0, east = (Fpu)0.0, north = (Fpu)0.0;
        foreach ((float frequency, float amplitude, float speed, float sin, float cos) in _layers)
        {
            Fpu across = (Fpu)x * sin;
            Fpu along = (Fpu)y * cos;
            float phase = ((Fpu)time * speed).F;
            here += (Fpu.Sin(((((along + across) + _offset) * frequency)) + phase) * amplitude) * attenuation;
            east += (Fpu.Sin((((along + (((Fpu)sample + x) * sin)) + _offset) * frequency) + phase) * amplitude) * attenuation;
            float northY = ((Fpu)sample + y).F;
            north += (Fpu.Sin((((across + ((Fpu)northY * cos)) + _offset) * frequency) + phase) * amplitude) * attenuation;
        }

        if (_waves.Count != 0 && x >= _minX)
        {
            float eastX = ((Fpu)sample + x).F;
            if (eastX <= _maxX && y >= _minY)
            {
                float northY = ((Fpu)sample + y).F;
                if (northY <= _maxY)
                {
                    foreach (Igi2WaterRipple wave in _waves)
                    {
                        float outer = wave.Outer;
                        if (!(((Fpu)wave.Local.X - outer).Value <= x && ((Fpu)outer + wave.Local.X).Value >= eastX
                            && ((Fpu)wave.Local.Y - outer).Value <= y && ((Fpu)outer + wave.Local.Y).Value >= northY))
                        {
                            continue;
                        }

                        float dx = ((Fpu)x - wave.Local.X).F;
                        Fpu dy = (Fpu)y - wave.Local.Y;
                        here += Ring(wave, Fpu.Sqrt(((Fpu)dx * dx) + (dy * dy)).F);
                        float eastDx = ((Fpu)eastX - wave.Local.X).F;
                        float eastDy = dy.F;
                        east += Ring(wave, Fpu.Sqrt(((Fpu)eastDx * eastDx) + ((Fpu)eastDy * eastDy)).F);
                        Fpu northDy = (Fpu)northY - wave.Local.Y;
                        north += Ring(wave, Fpu.Sqrt(((Fpu)dx * dx) + (northDy * northDy)).F);
                    }
                }
            }
        }

        float slopeX = (east - here).F;
        float slopeY = (north - here).F;
        float nx = (-((Fpu)sample * slopeX)).F;
        float ny = (-((Fpu)sample * slopeY)).F;
        float nz = ((Fpu)sample * sample).F;
        if (nx != 0f || ny != 0f || nz != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)nx * nx) + ((Fpu)ny * ny)) + ((Fpu)nz * nz));
            nx = ((Fpu)nx * inverse).F;
            ny = ((Fpu)ny * inverse).F;
            nz = ((Fpu)nz * inverse).F;
        }

        normal = new Vector3f(nx, ny, nz);
        return (here - z).F;
    }

    // A ring's lift at a distance from its centre: a cosine bump between its edges, beyond its dead centre.
    private static Fpu Ring(Igi2WaterRipple wave, float distance)
    {
        if (!(distance < wave.Outer && distance > wave.Inner && distance > wave.Dead))
        {
            return (Fpu)0.0;
        }

        Fpu turn = (((Fpu)distance - wave.Inner) * 6.2831854820251465) / ((Fpu)wave.HalfWidth + wave.HalfWidth);
        return ((Fpu)1.0 - Fpu.Cos(turn)) * wave.Height;
    }

    /// <summary>
    /// Starts a ripple, <c>Water_AddWave</c>: with five already spreading, the one started earliest goes first. It spreads for 150
    /// ticks from a fifth of its reach out to its reach, its height falling from its start to nothing.
    /// </summary>
    /// <param name="position">Where it starts.</param>
    /// <param name="height">Its height as it starts.</param>
    /// <param name="reach">How far it spreads.</param>
    /// <param name="dead">How near its centre it does not lift the surface.</param>
    /// <returns>How many ticks it spreads for.</returns>
    public int AddWave(Vector3d position, float height, float reach, float dead)
    {
        if (_waves.Count == MaxWaves)
        {
            Igi2WaterRipple oldest = _waves[0];
            for (int i = 1; i < _waves.Count; i++)
            {
                if (_waves[i].StartTick < oldest.StartTick)
                {
                    oldest = _waves[i];
                }
            }

            _waves.Remove(oldest);
            UpdateWaveBounds();
        }

        Vector3d at = _task.Position;
        Igi2WaterRipple wave = new()
        {
            StartTick = Tick,
            Duration = WaveDuration,
            Position = position,
            HalfWidth = ((Fpu)reach * 0.20000000298023224).F,
            StartHeight = height,
            Reach = reach,
            Dead = dead,
            Local = new Vector3f(((Fpu)position.X - at.X).F, ((Fpu)position.Y - at.Y).F, ((Fpu)position.Z - at.Z).F),
        };
        _waves.Add(wave);
        UpdateWave(wave);
        _maxX = Math.Max(((Fpu)wave.Reach + wave.Local.X).F, _maxX);
        _minX = Math.Min(((Fpu)wave.Local.X - wave.Reach).F, _minX);
        _maxY = Math.Max(((Fpu)wave.Reach + wave.Local.Y).F, _maxY);
        _minY = Math.Min(((Fpu)wave.Local.Y - wave.Reach).F, _minY);
        return wave.Duration;
    }

    /// <summary>Adds a splash, an element of <c>ptBulletSplashVector</c>.</summary>
    /// <returns>The splash, for its maker to fill.</returns>
    internal Igi2WaterSplashDrop AddSplash()
    {
        Igi2WaterSplashDrop drop = new();
        _splashes.Add(drop);
        return drop;
    }

    /// <summary>
    /// Runs the body a tick, <c>Water_RunHandler</c>: every ripple spreads (one that ends is taken off, and the one after it waits a
    /// tick, as retail's loop skips it), every splash that has started rises and falls and the spray fades, and one back under is
    /// taken off; then the tick counts.
    /// </summary>
    public void Run()
    {
        for (int i = 0; i < _waves.Count; i++)
        {
            UpdateWave(_waves[i]);
        }

        for (int j = 0; j < _splashes.Count; j++)
        {
            Igi2WaterSplashDrop drop = _splashes[j];
            float time = ((Fpu)(double)(Tick - drop.StartTick) * 0.06666666666666667).F;
            if (!(time > 0f))
            {
                continue;
            }

            drop.CurrentAcross = (((Fpu)time * drop.SpeedAcross) + drop.StartAcross).F;
            Fpu up = (((Fpu)time * drop.SpeedUp) - (((Fpu)time * time) * 4.90500020980835)) + drop.StartUp;
            drop.CurrentUp = up.F;
            if (drop.IsSpray)
            {
                float top = ((Fpu)drop.SpeedUp * 0.10193679481744766).F;
                Fpu past = (Fpu)time - top;
                Fpu falling = past.Value <= 0.0 ? (Fpu)0.0 : past;
                Fpu inverse = (Fpu)1.0 / top;
                drop.Alpha = ((Fpu)1.0 - ((inverse * falling) * 1.5)).Value <= 0.0
                    ? 0f
                    : ((Fpu)1.0 - ((falling * inverse) * 1.5)).F;
            }

            if (up.Value < 0.0)
            {
                _splashes.RemoveAt(j);
                j--;
            }
        }

        Tick++;
    }

    // Water_UpdateWave: a ripple that has spread its time is taken off; then its height and edges for this tick.
    private void UpdateWave(Igi2WaterRipple wave)
    {
        if (Tick >= wave.Duration + wave.StartTick)
        {
            _waves.Remove(wave);
            UpdateWaveBounds();
        }

        Fpu share = (Fpu)(double)(Tick - wave.StartTick) / (double)wave.Duration;
        wave.Height = (((Fpu)1.0 - share) * wave.StartHeight).F;
        Fpu outer = (share * ((Fpu)wave.Reach - wave.HalfWidth)) + wave.HalfWidth;
        wave.Outer = outer.F;
        wave.Inner = (outer - ((Fpu)wave.HalfWidth + wave.HalfWidth)).F;
    }

    // Water_UpdateWaveBounds: the rectangle every ripple's full reach covers.
    private void UpdateWaveBounds()
    {
        _maxX = float.MinValue;
        _minX = float.MaxValue;
        _maxY = float.MinValue;
        _minY = float.MaxValue;
        foreach (Igi2WaterRipple wave in _waves)
        {
            _maxX = Math.Max(((Fpu)wave.Local.X + wave.Reach).F, _maxX);
            _minX = Math.Min(((Fpu)wave.Local.X - wave.Reach).F, _minX);
            _maxY = Math.Max(((Fpu)wave.Reach + wave.Local.Y).F, _maxY);
            _minY = Math.Min(((Fpu)wave.Local.Y - wave.Reach).F, _minY);
        }
    }

    /// <summary>
    /// The surface lines strike, <c>Water_TraceLinesHandler</c>: a flat sheet at the body's level, inside its square; a line crossing
    /// it strikes it nearer than what it held, on the surface material, facing straight up.
    /// </summary>
    /// <param name="trace">The lines.</param>
    public void TraceLines(Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        Vector3d at = _task.Position;
        double level = at.Z;
        Fpu radius = (Fpu)_task.Size * 4096.0;
        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i], end = trace.Ends[i];
            if (!((start.Z >= level || end.Z >= level) && (start.Z <= level || end.Z <= level) && start.Z != end.Z))
            {
                continue;
            }

            Fpu rise = (Fpu)end.Z - start.Z;
            Fpu share = ((Fpu)level - start.Z) / rise;
            Vector3d hit = new(
                ((((Fpu)end.X - start.X) * share) + start.X).Value,
                ((((Fpu)end.Y - start.Y) * share) + start.Y).Value,
                ((share * rise) + start.Z).Value);
            if (!((radius + at.X).Value >= hit.X && ((Fpu)at.X - radius).Value <= hit.X
                && (radius + at.Y).Value >= hit.Y && ((Fpu)at.Y - radius).Value <= hit.Y))
            {
                continue;
            }

            Fpu dz = (Fpu)hit.Z - start.Z, dy = (Fpu)hit.Y - start.Y, dx = (Fpu)hit.X - start.X;
            double distance = Fpu.Sqrt(((dz * dz) + (dy * dy)) + (dx * dx)).Value;
            Igi2Intersection record = trace.Intersections[i];
            if (record.IsHit && !(distance < record.Distance))
            {
                continue;
            }

            record.HitObject = this;
            record.Distance = distance;
            record.Position = hit;
            record.IsDoubleFace = false;
            record.IsHit = true;
            record.EnterMaterial = SurfaceMaterial;
            record.LeaveMaterial = null;
            record.Normal = new Vector3f(0f, 0f, 1f);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Water answers no ground query; only line traces meet it.</remarks>
    public void NextZ(Igi2LineTrace trace)
    {
    }
}
