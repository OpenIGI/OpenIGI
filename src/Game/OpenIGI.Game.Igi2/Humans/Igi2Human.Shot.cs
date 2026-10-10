using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>Whether a bullet may strike the body.</content>
public abstract partial class Igi2Human : IIgi2WeaponCollisionTarget
{
    /// <summary>Gets a value indicating whether this human is the player, <c>Human_IsPlayer</c> (<c>igi2 0x501F30</c>).</summary>
    protected virtual bool IsThePlayer => false;

    /// <summary>
    /// Whether a bullet may strike the human, <c>Human_WeaponCollisionEvaluateHandler</c> (<c>igi2 0x4FF4E0</c>) in single player: never
    /// one its own weapon fired or one that ignores it. A human other than the player is never struck by its own side's bullet; the
    /// player is struck when a draw of the run stream is no more than the weapon's hit probability. Struck or not, the player then
    /// hears the bullet go by (<see cref="HearBulletPass"/>).
    /// </summary>
    /// <param name="query">The trace's context.</param>
    /// <returns>Whether it may.</returns>
    public bool EvaluateWeaponCollision(in Igi2WeaponCollisionQuery query)
    {
        bool hit;
        if (ReferenceEquals(query.Ignore, this) || ReferenceEquals(query.Shooter, this))
        {
            hit = false;
        }
        else if (!IsThePlayer)
        {
            if (query.Shooter is Igi2Human shooter && shooter.Team == Team)
            {
                return false;
            }

            hit = true;
        }
        else
        {
            hit = !(Random.NextDouble() > query.HitProbability);
        }

        HearBulletPass(query);
        return hit;
    }

    /// <summary>A bullet the trace asked about passes by: only the player hears it.</summary>
    /// <param name="query">The trace's context.</param>
    protected virtual void HearBulletPass(in Igi2WeaponCollisionQuery query)
    {
    }
}
