namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// Another machine's rounds, shown on a body here: a co-op client's copy of a soldier or a partner, whose gun the server fires.
/// </summary>
/// <remarks>
/// <para>
/// The server's count of rounds fired comes with each snapshot. Each time it moves, the body here flashes as its own gun's shot
/// does (<see cref="Igi2GunFire.Flash"/>) and sounds as a pull does: once for a single shot or a burst, and for full automatic the
/// loop, cut off by its tail once the rounds stop for longer than the gun's own delay between them.
/// </para>
/// <para>
/// Nothing is fired here: no round, no event the soldiers hear, no casing, and no draw from the run stream. The server's rounds
/// are the ones that land.
/// </para>
/// </remarks>
/// <param name="weapon">The body's weapon.</param>
/// <param name="owner">The body.</param>
public sealed class Igi2RemoteGunfire(Igi2WeaponFiring weapon, IIgi2WeaponOwner owner)
{
    // A tick or two past the gun's delay, as rounds reach a client unevenly.
    private const int LoopSlackTicks = 3;

    private byte? _shots;
    private int _sinceShot;
    private Igi2GunTask? _looping;

    /// <summary>Takes the server's count of rounds fired for this tick.</summary>
    /// <param name="shots">The count, round in a byte.</param>
    public void Take(byte shots)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        bool fired = _shots is { } last && last != shots;
        _shots = shots;
        Igi2GunTask? gun = weapon.Inventory.Current?.WeaponType.FireType(0) as Igi2GunTask;
        if (!fired || gun is null)
        {
            _sinceShot++;
            if (_looping is { } loop && (gun != loop || _sinceShot > DelayTicks(loop) + LoopSlackTicks))
            {
                weapon.Effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, loop.FireSound2, looped: false);
                _looping = null;
            }

            return;
        }

        _sinceShot = 0;
        weapon.Inventory.CountFired(1);
        Igi2GunFire.Flash(gun, weapon, owner);
        int mode = weapon.Inventory.Current?.ActiveFireMode ?? 0;
        if (!gun.IsFireSoundLooped(mode))
        {
            weapon.Effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, gun.FireSound(mode), looped: false);
        }
        else if (_looping != gun)
        {
            weapon.Effects.PlaySound(Igi2WeaponFiring.FireSoundSlot, gun.FireSound(mode), looped: true);
            _looping = gun;
        }
    }

    // The ticks between two rounds of the gun, as its handler reports its delay.
    private static int DelayTicks(Igi2GunTask gun) => gun.RoundsPerMinute <= 0 ? 0 : (int)((60f / gun.RoundsPerMinute * 30f) + 0.5f);
}
