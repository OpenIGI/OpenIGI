namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Pulling a thrown weapon's pin: <c>PullPin_WeaponFireHandler</c> (<c>igi2 0x4C8060</c>).</summary>
/// <remarks>
/// The pin is out and the fuse starts at the fire type's <c>Explode Time</c>, thirty ticks a second; the weapon's delayed
/// pass counts it down and, at nothing, lets the weapon go off in hand (<see cref="Igi2WeaponFiring.RunDelayed"/>). No
/// round is taken and nothing waits, but the view takes a hundredth of a degree of kick each way, damped by a tenth.
/// </remarks>
internal sealed class Igi2PullPinFire : IIgi2FireHandler
{
    /// <summary>The kick each way, <c>0x3C23D70A</c>.</summary>
    internal const float Kick = 0.01f;

    /// <summary>The kick's damping, <c>0x3DCCCCCD</c>.</summary>
    internal const float KickDamping = 0.1f;

    /// <inheritdoc/>
    public bool HasRelease => false;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        Igi2PullPinTask pin = (Igi2PullPinTask)request.FireTypeParameters;
        Igi2Inventory inventory = request.Weapon.Inventory;
        inventory.IsPinPulled = true;
        inventory.Timer = Fpu.ToInt32((Fpu)pin.ExplodeTimeSeconds * 30f);
        return new Igi2FireResult(0, 0, KickDamping, 1f, Kick, Kick);
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request) => Igi2FireResult.Nothing;
}
