namespace OpenIGI.Game.Igi2.Weapons;

/// <content>A co-op client's copy of the mounted gun: pointed as the server's points, firing only what the server's fired.</content>
public sealed partial class Igi2StationaryGunTask
{
    private Igi2RemoteGunfire? _gunfire;

    /// <summary>
    /// Gets or sets a value indicating whether it is a co-op client's copy of the server's: whoever works it here, it is pointed as
    /// the server's is (<see cref="Hold"/>), and its weapon fires again, harmless, what the server's fired.
    /// </summary>
    public bool IsPuppet { get; set; }

    /// <summary>Gets where it points and what it has fired now, as a server sends it.</summary>
    /// <returns>Its aim.</returns>
    public Igi2GunAim Aim() => new(_finalTilt, _finalTurn, unchecked((byte)(_firing?.Inventory.TotalBulletsFired ?? 0)));

    /// <summary>Points it as the server's points, on a co-op client, with the flash and report of each round the server's fired since.</summary>
    /// <param name="aim">The server's aim.</param>
    public void Hold(in Igi2GunAim aim)
    {
        IsPuppet = true;
        _firing?.IsReplica = true;
        _tilt = aim.Tilt;
        _turn = aim.Turn;
        _finalTilt = aim.Tilt;
        _finalTurn = aim.Turn;
        _tiltOffset = 0f;
        _turnOffset = 0f;
        if (_firing is { } firing)
        {
            (_gunfire ??= new Igi2RemoteGunfire(firing, this)).Take(aim.Shots);
        }
    }

    // A puppet's tick, once its replayed rounds are traced: placed as it is held, its weapon's state run on.
    private void RunPuppet(Flow.Igi2LevelRun level, Igi2WeaponFiring firing, int tick)
    {
        PositionWeapon(tick);
        firing.UpdateStatus();
        level.RunChildren(this);
        firing.RunDelayed();
    }
}
