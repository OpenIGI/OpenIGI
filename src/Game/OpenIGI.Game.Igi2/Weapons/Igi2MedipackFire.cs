using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Using a medipack: <c>medipack.c Medipack_WeaponFireHandler</c>.</summary>
/// <remarks>
/// A hurt human holding it takes its <c>Factor of human max damage to add for each round</c> of its greatest damage off the damage
/// it has taken, never below nothing nor above that greatest; that uses a round, kicks the view a hundredth of a degree each way,
/// damped by a tenth. An unhurt human uses nothing and nothing kicks. Either way the next use waits a round's time, the fire type's
/// rounds per minute at thirty ticks a second, chopped.
/// </remarks>
internal sealed class Igi2MedipackFire : IIgi2FireHandler
{
    /// <summary>The kick each way when it heals, <c>0x3C23D70A</c>.</summary>
    internal const float Kick = 0.01f;

    /// <summary>The kick's damping when it heals, <c>0x3DCCCCCD</c>.</summary>
    internal const float KickDamping = 0.1f;

    /// <inheritdoc/>
    public bool HasRelease => true;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        Igi2MedipackTask medipack = (Igi2MedipackTask)request.FireTypeParameters;
        int used = 0;
        if (request.Operator is Igi2Human { Damage: { } damage } && damage.Damage != 0f)
        {
            float left = ((Fpu)damage.Damage - ((Fpu)medipack.FactorOfHumanMaxDamageToAddForEachRound * damage.MaxDamage)).F;
            damage.Damage = left >= 0f ? (left > damage.MaxDamage ? damage.MaxDamage : left) : 0f;
            used = 1;
        }

        int delay = Fpu.ToInt32(((Fpu)60.0 / (double)medipack.RoundsPerMinute) * 30.0);
        return used > 0
            ? new Igi2FireResult(used, delay, KickDamping, 1f, Kick, Kick)
            : new Igi2FireResult(0, delay, 0f, 1f, 0f, 0f);
    }

    /// <inheritdoc/>
    /// <remarks><c>MediPack_WeaponReleaseHandler</c> does nothing.</remarks>
    public Igi2FireResult Release(in Igi2FireRequest request) => Igi2FireResult.Nothing;
}
