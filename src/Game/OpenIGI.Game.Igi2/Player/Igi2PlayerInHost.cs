using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// I.G.I. 2's player as another game's level sees the player it placed (<see cref="HumanPlayerTask"/>) once I.G.I. 2's stands in
/// its stead: where the body is and how it stands, and what that game's rounds and blasts do to it.
/// </summary>
/// <remarks>
/// The level's task stays the player its scripts, objectives and soldiers name; this is its body (<see cref="HumanTask.Body"/>)
/// and the handler of what strikes it (<see cref="HumanTask.Events"/>). A round or a blast another game sends is taken as one of
/// I.G.I. 2's own is (<see cref="Weapons.IIgi2WeaponHitTarget"/>, <see cref="Weapons.IIgi2ExplosionTarget"/>).
/// </remarks>
/// <param name="human">The player.</param>
public sealed class Igi2PlayerInHost(Igi2HumanPlayer human) : IHumanBodyState, IHumanEvents
{
    private readonly Igi2HumanPlayer _human = human ?? throw new ArgumentNullException(nameof(human));

    /// <summary>Gets the player.</summary>
    public Igi2HumanPlayer Human => _human;

    /// <inheritdoc/>
    public Vector3d Feet => _human.Position;

    /// <inheritdoc/>
    public Vector3d Eyes => _human.OriginalViewPosition;

    /// <inheritdoc/>
    public bool IsKeepingLow => _human.IsKeepingLow;

    /// <inheritdoc/>
    /// <remarks>A dead player is out of the fight.</remarks>
    public bool IsActive => !_human.IsDead;

    /// <inheritdoc/>
    /// <remarks>Every round may meet it: its own bones decide where, as I.G.I. 2's own rounds find them.</remarks>
    public bool EvaluateWeaponCollision(HumanTask human, in WeaponCollisionQuery query) => true;

    /// <inheritdoc/>
    public void WeaponHit(HumanTask human, in WeaponHit hit) => ((IWeaponHitTarget)_human).WeaponHit(hit);

    /// <inheritdoc/>
    public void ExplosionHit(HumanTask human, in ExplosionHit hit) => ((IExplosionHitTarget)_human).ExplosionHit(hit);
}
