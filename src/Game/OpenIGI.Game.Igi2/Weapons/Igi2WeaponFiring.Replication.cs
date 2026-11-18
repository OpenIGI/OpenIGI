using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A projectile a weapon launched, as a co-op server tells it: the launch, and which of its weapon's fire types made it.</summary>
/// <param name="Launch">The launch.</param>
/// <param name="FireType">The fire type's slot on the weapon, nought or one.</param>
public readonly record struct Igi2LaunchNote(Igi2ProjectileLaunch Launch, int FireType);

/// <content>What a weapon fires, told to a co-op session's other machines, and fired there again without harm.</content>
public sealed partial class Igi2WeaponFiring
{
    private bool _isReplica;

    /// <summary>Gets or sets what hears of each projectile this weapon launches: a co-op server, to tell its clients.</summary>
    public Action<Igi2LaunchNote>? Launched { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this weapon is a co-op client's copy of another machine's: what it fires does no
    /// harm (<see cref="Igi2GunShot.IsHarmless"/>), and its grenades and rockets fly but never go off, the server's blasts being
    /// shown in their stead.
    /// </summary>
    public bool IsReplica
    {
        get => _isReplica;
        set
        {
            _isReplica = value;
            GunShot.IsHarmless = value;
        }
    }

    /// <summary>Launches what another machine's copy of this weapon launched: its rounds, grenade or rocket, harmless.</summary>
    /// <param name="launch">The launch, its thrower to be this weapon's holder.</param>
    /// <param name="fireType">The fire type's slot.</param>
    /// <remarks>Only a replica weapon replays: its rounds are traced with its own later in the tick, harmless as it is.</remarks>
    public void Replay(in Igi2ProjectileLaunch launch, int fireType)
    {
        if (!IsReplica || launch.WeaponType.FireType(fireType) is not { } parameters)
        {
            return;
        }

        Igi2Grenades? grenades = Grenades as Igi2Grenades;
        grenades?.LaunchingReplicas = true;
        try
        {
            CreateProjectile(launch with { Thrower = Owner }, parameters);
        }
        finally
        {
            grenades?.LaunchingReplicas = false;
        }
    }

    // The launch told on, unless it is itself a replay.
    private void NoteLaunch(in Igi2ProjectileLaunch launch, GameTask fireTypeParameters)
    {
        if (Launched is not { } told || IsReplica)
        {
            return;
        }

        for (int slot = 0; slot < Igi2WeaponTypeTask.FireTypeSlots; slot++)
        {
            if (ReferenceEquals(launch.WeaponType.FireType(slot), fireTypeParameters))
            {
                told(new Igi2LaunchNote(launch, slot));
                return;
            }
        }
    }
}
