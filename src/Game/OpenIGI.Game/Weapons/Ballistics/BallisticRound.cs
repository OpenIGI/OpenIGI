using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons.Ballistics;

/// <summary>One round in flight under the tactical-ballistics option, and its visible trail.</summary>
/// <remarks>
/// <para>
/// A mutable class rather than a struct because <see cref="BallisticSimulation"/> pools and reworks
/// these in place every tick, the way <c>Physics.RagdollBody</c> is owned by its world. Positions are
/// double precision for the same reason every world-space thing here is: a level sits tens of
/// kilometres from the origin, where a float resolves to millimetres.
/// </para>
/// <para>
/// The trail is part of the round rather than the renderer so that what is drawn is what was
/// simulated — the renderer reads positions, it does not integrate anything. A dead round lingers
/// for <see cref="BallisticSimulation.FadeLifetimeTicks"/> so its trail fades out instead of
/// vanishing on the impact frame.
/// </para>
/// </remarks>
public sealed class BallisticRound
{
    private readonly Vector3d[] _trail = new Vector3d[BallisticSimulation.TrailCapacity];
    private int _trailHead;

    /// <summary>Initializes a new instance of the <see cref="BallisticRound"/> class.</summary>
    /// <param name="origin">The muzzle, in world units.</param>
    /// <param name="velocity">The initial velocity, in world units per second.</param>
    /// <param name="profile">How this round flies.</param>
    /// <param name="weapon">The weapon that fired it.</param>
    /// <param name="ammo">The ammunition record, which colours the tracer, when there is one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    public BallisticRound(
        Vector3d origin,
        Vector3d velocity,
        BallisticProfile profile,
        WeaponType weapon,
        AmmoType? ammo)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        Origin = origin;
        Position = origin;
        PreviousPosition = origin;
        Velocity = velocity;
        Profile = profile;
        Weapon = weapon;
        Ammo = ammo;
    }

    /// <summary>Gets where the round left the muzzle.</summary>
    public Vector3d Origin { get; }

    /// <summary>Gets or sets where the round's head is now.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets where the head was at the previous tick, for frame interpolation.</summary>
    public Vector3d PreviousPosition { get; set; }

    /// <summary>Gets or sets the velocity, in world units per second.</summary>
    public Vector3d Velocity { get; set; }

    /// <summary>Gets how this round flies.</summary>
    public BallisticProfile Profile { get; }

    /// <summary>Gets the weapon that fired it.</summary>
    public WeaponType Weapon { get; }

    /// <summary>Gets the ammunition record, or <see langword="null"/> when the weapon names none.</summary>
    public AmmoType? Ammo { get; }

    /// <summary>Gets or sets the damage the round carries to whatever human it meets.</summary>
    public float DamageFactor { get; set; }

    /// <summary>Gets or sets the firing weapon's category, which travels with the round.</summary>
    /// <remarks>Same reason it travels with the hit-scan ray: it decides how a killed body falls.</remarks>
    public WeaponCategory Category { get; set; }

    /// <summary>Gets or sets how much range is left, in world units.</summary>
    public float RemainingRange { get; set; }

    /// <summary>Gets or sets where the round is DRAWN from, when that differs from where it flies.</summary>
    /// <remarks>
    /// Purely presentation, in world units, and the simulation never reads it. The retail muzzle is
    /// the eye — dead centre of the screen, where the shooter's own receding round projects to a
    /// point — so the player's rounds carry the gun's offset here and the renderer converges it onto
    /// the true trajectory over the first metres of flight. <see langword="null"/> draws the flight
    /// as flown.
    /// </remarks>
    public Vector3d? VisualOffset { get; set; }

    /// <summary>Gets or sets whether this round is a sight, not a shot.</summary>
    /// <remarks>
    /// An AI streak: its ray was already resolved by the AI's own combat code, so a visual round
    /// never casts and never damages — it flies its resolved line and dies at
    /// <see cref="VisualEndDistance"/>.
    /// </remarks>
    public bool IsVisualOnly { get; set; }

    /// <summary>Gets or sets how far from the origin a visual-only round dies, in world units.</summary>
    public double VisualEndDistance { get; set; }

    /// <summary>Gets or sets how far this round has flown, in world units.</summary>
    public double TravelledUnits { get; set; }

    /// <summary>Gets or sets whether the round is still flying.</summary>
    public bool Alive { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the round has come out of something it went into.</summary>
    /// <remarks>
    /// The latch <c>0x47B750</c> keeps per round (its <c>v83</c>): the next surface or body the round enters
    /// takes the hit and stops it.
    /// </remarks>
    public bool HasLeftASurface { get; set; }

    /// <summary>Gets or sets the ticks of trail fade left once the round is dead.</summary>
    public int FadeTicks { get; set; } = BallisticSimulation.FadeLifetimeTicks;

    /// <summary>Gets how many trail positions have been recorded, newest first.</summary>
    public int TrailCount { get; private set; }

    /// <summary>Records the head's position at the end of a tick.</summary>
    /// <param name="position">Where the head is.</param>
    public void PushTrail(Vector3d position)
    {
        _trailHead = (_trailHead + 1) % _trail.Length;
        _trail[_trailHead] = position;
        if (TrailCount < _trail.Length)
        {
            TrailCount++;
        }
    }

    /// <summary>Reads a trail position.</summary>
    /// <param name="age">How many ticks back: 0 is the newest recorded position.</param>
    /// <returns>The position at that age.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="age"/> is outside the recorded trail.</exception>
    public Vector3d TrailAt(int age)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(age);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(age, TrailCount);
        return _trail[(_trailHead - age + _trail.Length) % _trail.Length];
    }

    /// <summary>Drops the oldest trail position, which is how a dead round's trail shrinks away.</summary>
    public void ShrinkTrail()
    {
        if (TrailCount > 0)
        {
            TrailCount--;
        }
    }
}
