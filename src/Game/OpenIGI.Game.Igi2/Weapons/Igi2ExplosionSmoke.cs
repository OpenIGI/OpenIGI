using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// An explosion's smoke, <c>ExplosionSmoke</c> (<c>Explosion0_SmokeRunHandler</c>, <c>igi2 0x5095C0</c>): a white puff when it
/// starts and a black one late in its life.
/// </summary>
public sealed class Igi2ExplosionSmoke : IIgi2DynamicTask
{
    private readonly IgiRandom _random;
    private readonly List<Igi2SmokePuff> _puffs = [];
    private bool _isBlack;

    /// <summary>Initializes a new instance of the <see cref="Igi2ExplosionSmoke"/> class.</summary>
    /// <param name="random">The run stream.</param>
    /// <param name="position">Where.</param>
    /// <param name="radius">How big, <c>vRadius</c>.</param>
    /// <param name="lifetime">The ticks it lives.</param>
    public Igi2ExplosionSmoke(IgiRandom random, Vector3d position, float radius, int lifetime)
    {
        _random = random;
        Position = position;
        Radius = radius;
        TotalLifetime = lifetime;
        Lifetime = lifetime;
    }

    /// <summary>Gets where it is.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets how big its puffs start, <c>vRadius</c>.</summary>
    public float Radius { get; }

    /// <summary>Gets the ticks it lives, <c>nTotalLifetime</c>.</summary>
    public int TotalLifetime { get; }

    /// <summary>Gets the ticks it has left, <c>nLifetime</c>.</summary>
    public int Lifetime { get; private set; }

    /// <summary>Gets the puffs it has started.</summary>
    public IReadOnlyList<Igi2SmokePuff> Puffs => _puffs;

    /// <summary>Gets the black puff's brightness, set each tick once it has started.</summary>
    public float BlackIntensity { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// On its first tick the white puff starts: it lives a tenth of the smoke's life, fades over four fifths to all of it at
    /// random, and is a tenth bright and a tenth more at random. At nine tenths of its life the black puff starts, which brightens
    /// to three tenths at half the smoke's life and fades out by nine tenths.
    /// </remarks>
    public bool Run()
    {
        if (Lifetime <= 0)
        {
            return false;
        }

        int total = TotalLifetime;
        if (Lifetime == total)
        {
            int partLife = Fpu.ToInt32((Fpu)(double)total * 0.1f);
            Fpu fade = ((Draw() * 0.2f) + 0.8f) * (double)total;
            int partFade = Fpu.ToInt32(fade);
            float sizeDelta = ((Fpu)1f / (double)total).F;
            float intensity = ((Draw() + 1f) * 0.1f).F;
            _puffs.Add(Puff(0x1000008, sizeDelta, partLife, partFade, intensity));
        }

        if (Lifetime == Fpu.ToInt32((Fpu)0.9f * (double)total))
        {
            float sizeDelta = ((Fpu)1f / (double)total).F;
            _puffs.Add(Puff(0x1000010, sizeDelta, total, 0, 0.001f));
            _isBlack = true;
        }

        if (_isBlack)
        {
            Fpu life = (double)Lifetime, whole = (double)total;
            Fpu half = whole * 0.5f;
            Fpu share = life.Value <= half.Value ? life / half : (Fpu)1f - ((life - half) / ((whole * 0.9f) - half));
            BlackIntensity = (share * 0.3f).F;
        }

        Lifetime--;
        return true;
    }

    private Fpu Draw() => _random.NextDouble();

    /// <summary><c>Explosion0_AddSmoke</c>: a particle at the smoke, turned at random and turning at random.</summary>
    private Igi2SmokePuff Puff(int flags, float sizeDelta, int lifetime, int fadetime, float intensity)
    {
        float rotation = (Draw() * 6.2831855f).F;
        float rotationDelta = (((Fpu)0.5 - Draw()) * 0.018849557f).F;
        return new Igi2SmokePuff(flags, Position, Radius, sizeDelta, lifetime, fadetime, intensity, rotation, rotationDelta);
    }
}
