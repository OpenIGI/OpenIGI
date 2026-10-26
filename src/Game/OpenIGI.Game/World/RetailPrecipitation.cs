using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One drop or flake of <see cref="RetailPrecipitation"/>: where it is in its tile and layer, and how it drifts.</summary>
/// <param name="X">Across the tile, in world units from its corner.</param>
/// <param name="Y">Along the tile, in world units from its corner.</param>
/// <param name="Z">Up its layer, in world units from the layer's floor.</param>
/// <param name="Timer">Ticks until a flake picks a new drift.</param>
/// <param name="DriftX">How far a flake drifts across each tick.</param>
/// <param name="DriftY">How far a flake drifts along each tick.</param>
/// <param name="Group">Which of the four tile groups draws it; a flake changes group as it drifts over a tile's edge.</param>
public record struct WeatherDrop(float X, float Y, float Z, int Timer, float DriftX, float DriftY, int Group);

/// <summary>
/// Both games' weather as the originals simulate it: eighty drops or flakes in one tile of the ground, falling through a layer and
/// coming round to its top again, which the drawing repeats over every tile and layer around the eye.
/// </summary>
/// <remarks>
/// <para>
/// Project I.G.I.'s <c>Rain_Update</c> (<c>0x499CF0</c>) and I.G.I. 2's <c>Rain_UpdateParticles</c> are the same: the weather fades
/// in or out by a sixtieth a tick as it is told to fall or not; while any of it shows, every drop falls (rain 2048 units a tick, snow a
/// tenth of that) and wraps round its layer; a flake also drifts, picking a new drift every thirty to fifty-nine ticks, and crosses
/// into the next tile's group when it leaves its own; and new drops are let in at the layer's top, one per 307.2 units fallen, until
/// there are eighty.
/// </para>
/// <para>
/// The two draw from their own stream, never the level's, which only places drops in the picture; both are this generator, seeded
/// as <see cref="IgiRandom.Seeded"/> is.
/// </para>
/// </remarks>
public sealed class RetailPrecipitation
{
    /// <summary>The most drops there are.</summary>
    public const int MaximumDrops = 80;

    /// <summary>The side of the square tile the drops fill, in world units.</summary>
    public const float TileUnits = 10895.36f;

    /// <summary>The height of a layer the drops fall through, in world units.</summary>
    public const float LayerUnits = 24576f;

    /// <summary>How far rain falls each tick, in world units.</summary>
    public const float RainFallUnits = 2048f;

    /// <summary>How far snow falls each tick, in world units.</summary>
    public const float SnowFallUnits = 204.8f;

    /// <summary>How far the weather falls between one new drop and the next, in world units.</summary>
    public const float SpawnUnits = 307.2f;

    /// <summary>The widest drift a flake takes each tick, in world units.</summary>
    public const float SwayUnits = 136.53334f;

    /// <summary>How much the weather fades in or out each tick.</summary>
    public const float FadeStep = 0.016666668f;

    private readonly WeatherDrop[] _drops = new WeatherDrop[MaximumDrops];
    private readonly IgiRandom _random;
    private float _spawn;

    /// <summary>Initializes a new instance of the <see cref="RetailPrecipitation"/> class, faded out and with no drops yet.</summary>
    /// <param name="snow">Whether it is snow rather than rain.</param>
    /// <param name="random">The weather's own stream; a freshly seeded one without it.</param>
    public RetailPrecipitation(bool snow, IgiRandom? random = null)
    {
        IsSnow = snow;
        _random = random ?? IgiRandom.Seeded();
    }

    /// <summary>Gets a value indicating whether it is snow rather than rain.</summary>
    public bool IsSnow { get; }

    /// <summary>Gets or sets a value indicating whether it is told to fall (<c>Rain_SetFalling</c>, <c>Rain_UpdateStatus</c>).</summary>
    public bool IsFalling { get; set; }

    /// <summary>Gets how far it has faded in, from nought to one.</summary>
    public float Fade { get; private set; }

    /// <summary>Gets how many drops there are.</summary>
    public int Count { get; private set; }

    /// <summary>Gets how far it falls each tick, in world units.</summary>
    public float FallUnits => IsSnow ? SnowFallUnits : RainFallUnits;

    /// <summary>Gets the drops, in the order they were let in.</summary>
    public ReadOnlySpan<WeatherDrop> Drops => _drops.AsSpan(0, Count);

    /// <summary>Advances the weather one tick.</summary>
    public void Tick()
    {
        float fall = FallUnits;
        Fade = IsFalling ? MathF.Min(Fade + FadeStep, 1f) : MathF.Max(Fade - FadeStep, 0f);
        if (Fade == 0f)
        {
            return;
        }

        for (int i = 0; i < Count; i++)
        {
            ref WeatherDrop drop = ref _drops[i];
            drop.Z -= fall;
            if (drop.Z < 0f)
            {
                drop.Z += LayerUnits;
            }

            if (IsSnow)
            {
                Drift(ref drop);
            }
        }

        _spawn += fall;
        while (_spawn > SpawnUnits)
        {
            if (Count == MaximumDrops)
            {
                return;
            }

            // Across first, then along, then which group: the order the original draws them in.
            float x = _random.NextSingle() * TileUnits;
            float y = _random.NextSingle() * TileUnits;
            _drops[Count++] = new WeatherDrop(x, y, LayerUnits - fall, 0, 0f, 0f, (int)_random.NextBounded(4));
            _spawn -= SpawnUnits;
        }
    }

    // A flake's drift: a new one when its timer has run out, then a step of it, crossing into the neighbouring tile's group at an edge.
    private void Drift(ref WeatherDrop flake)
    {
        if (flake.Timer-- < 0)
        {
            flake.Timer += (int)_random.NextBounded(30) + 30;
            flake.DriftX = (_random.NextSingle() - 0.5f) * SwayUnits;
            flake.DriftY = (_random.NextSingle() - 0.5f) * SwayUnits;
        }

        flake.X += flake.DriftX;
        flake.Y += flake.DriftY;
        if (flake.X < 0f)
        {
            flake.X += TileUnits;
            flake.Group ^= 1;
        }
        else if (flake.X > TileUnits)
        {
            flake.X -= TileUnits;
            flake.Group ^= 1;
        }

        if (flake.Y < 0f)
        {
            flake.Y += TileUnits;
            flake.Group ^= 2;
        }
        else if (flake.Y > TileUnits)
        {
            flake.Y -= TileUnits;
            flake.Group ^= 2;
        }
    }
}
