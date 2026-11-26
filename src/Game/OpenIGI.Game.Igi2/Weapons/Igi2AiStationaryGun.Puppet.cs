namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Where a gun points and how many rounds it has fired, as a co-op server has it.</summary>
/// <param name="Tilt">The barrel's tilt, radians.</param>
/// <param name="Turn">The head's turn, radians.</param>
/// <param name="Shots">The rounds it has fired, counted round in a byte.</param>
public readonly record struct Igi2GunAim(float Tilt, float Turn, byte Shots);

/// <content>A co-op client's copy of the gun: pointed as the server's points, firing only what the server's fired.</content>
public sealed partial class Igi2AiStationaryGun
{
    private Igi2RemoteGunfire? _gunfire;

    /// <summary>
    /// Gets or sets a value indicating whether it is a co-op client's copy of the server's: it looks for nobody and fires at nobody,
    /// but is pointed as the server's is (<see cref="Hold"/>), and its weapon fires again, harmless, what the server's fired.
    /// </summary>
    public bool IsPuppet
    {
        get => _firing.IsReplica;
        set => _firing.IsReplica = value;
    }

    /// <summary>Gets where it points and what it has fired now, as a server sends it.</summary>
    /// <returns>Its aim.</returns>
    public Igi2GunAim Aim() => new(_tilt, _turn, unchecked((byte)_firing.Inventory.ShotSequence));

    /// <summary>Points it as the server's points, on a co-op client, with the flash and report of each round the server's fired since.</summary>
    /// <param name="aim">The server's aim.</param>
    public void Hold(in Igi2GunAim aim)
    {
        _tilt = aim.Tilt;
        _turn = aim.Turn;
        if (_base is not null)
        {
            PlaceGun();
        }

        (_gunfire ??= new Igi2RemoteGunfire(_firing, this)).Take(aim.Shots);
    }

    // A puppet's tick: the rounds replayed into it traced, its weapon's state run on.
    private void RunPuppet()
    {
        _firing.RunGunShot();
        _firing.UpdateStatus();
        _firing.RunDelayed();
    }
}
