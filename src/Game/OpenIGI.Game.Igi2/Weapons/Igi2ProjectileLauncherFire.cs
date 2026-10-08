namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// A fire type that throws or launches something of its own, <c>ProjectileLauncher</c>: <c>ProjectileLauncher_WeaponFireHandler</c>
/// (<c>igi2 0x526B40</c>) and <c>ProjectileLauncher_WeaponReleaseHandler</c> (<c>0x526CD0</c>).
/// </summary>
/// <remarks>
/// <para>
/// One that does not create on release creates its projectile as it fires: from the fire's position and orientation, at
/// the fire type's speed, inheriting the thrower's, with the fuse of a pin already pulled or else the fire type's own
/// <c>Explode Time</c>, thirty ticks a second. The pin goes back and the fuse with it, one round is taken, the next waits
/// out the fire type's rate, and the fire sound plays in the weapon's second slot; the view takes a hundredth of a degree of
/// kick each way, damped by a tenth.
/// </para>
/// <para>
/// Letting go fires nothing for such a fire type. Not yet: one that creates on release, which no shipped grenade is.
/// </para>
/// </remarks>
internal sealed class Igi2ProjectileLauncherFire(Igi2WeaponTables tables) : IIgi2FireHandler
{
    /// <summary>The weapon sound slot the fire sound plays in.</summary>
    internal const int FireSoundSlot = 1;

    /// <inheritdoc/>
    public bool HasRelease => true;

    /// <inheritdoc/>
    public Igi2FireResult Fire(in Igi2FireRequest request)
    {
        Igi2ProjectileLauncherTask launcher = (Igi2ProjectileLauncherTask)request.FireTypeParameters;
        if (launcher.ProjectileQTaskType == Igi2WeaponConstants.NoTaskType || launcher.CreateOnRelease)
        {
            return Igi2FireResult.Nothing;
        }

        Igi2Inventory inventory = request.Weapon.Inventory;
        int explodeTime = inventory.IsPinPulled ? inventory.Timer : Fpu.ToInt32((Fpu)launcher.ExplodeTimeSeconds * 30.0);
        request.Weapon.CreateProjectile(
            new Igi2ProjectileLaunch(
                launcher.ProjectileQTaskType,
                request.Position,
                [.. request.Orientation],
                request.ParentSpeed,
                launcher.SpeedMps,
                launcher.EffectiveRangeMeters,
                request.HeldTime,
                request.WeaponType,
                tables.Ammo(launcher.AmmoType),
                explodeTime,
                request.Operator),
            launcher);
        inventory.IsPinPulled = false;
        inventory.Timer = 0;
        int delay = Fpu.ToInt32(((Fpu)60.0 / (double)launcher.RoundsPerMinute) * 30.0);
        request.Weapon.Effects.PlaySound(FireSoundSlot, launcher.FireSound, looped: false);
        return new Igi2FireResult(1, delay, Igi2PullPinFire.KickDamping, 1f, Igi2PullPinFire.Kick, Igi2PullPinFire.Kick);
    }

    /// <inheritdoc/>
    public Igi2FireResult Release(in Igi2FireRequest request) => Igi2FireResult.Nothing;
}
