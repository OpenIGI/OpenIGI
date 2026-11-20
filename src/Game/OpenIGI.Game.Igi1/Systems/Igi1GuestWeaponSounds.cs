using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// The weapons' reports of Project I.G.I.'s humans standing in another game's level, played through that game's audio: the
/// player's in its own hands, and each soldier's where it stands, by the rule the game's own missions sound them by
/// (<see cref="WeaponSoundPlayer"/>).
/// </summary>
/// <param name="audio">The host's audio, playing the game's own sound definitions.</param>
/// <param name="listener">Where the listener stands, in world units: a sound in the player's own hands plays there.</param>
internal sealed class Igi1GuestWeaponSounds(ITitleAudio audio, Func<Vector3d> listener) : IWeaponSoundOutput
{
    private readonly Dictionary<int, WeaponSoundPlayer> _soldiers = [];
    private WeaponSoundPlayer? _player;

    /// <inheritdoc/>
    public IWeaponVoice? Play(string name, Vector3d? positionInUnits, bool looping) =>
        audio.Play(name, positionInUnits ?? listener(), looping ? true : null) is { } voice ? new Voice(voice, listener) : null;

    /// <summary>Sounds the weapon in the player's hands for a tick.</summary>
    /// <param name="player">The player.</param>
    public void TickPlayer(Igi1.Player.PlayerCamera player) => (_player ??= new WeaponSoundPlayer(this)).TickPlayer(player);

    /// <summary>Sounds a soldier's weapon for a tick, where the soldier stands (<c>SoldierSimulation.SetWeaponSound</c>).</summary>
    /// <param name="runtimeId">The soldier, by its runtime id.</param>
    /// <param name="fired">Whether it fired this tick.</param>
    /// <param name="weapon">The weapon in its hands, or <see langword="null"/> for none.</param>
    /// <param name="position">Where it stands, in world units.</param>
    public void TickSoldier(int runtimeId, bool fired, WeaponType? weapon, Vector3d position)
    {
        if (!_soldiers.TryGetValue(runtimeId, out WeaponSoundPlayer? player))
        {
            player = new WeaponSoundPlayer(this);
            _soldiers.Add(runtimeId, player);
        }

        player.Tick(fired, weapon, position);
    }

    /// <summary>Silences every weapon, as the level is left.</summary>
    public void Reset()
    {
        _player?.Reset();
        foreach (WeaponSoundPlayer soldier in _soldiers.Values)
        {
            soldier.Reset();
        }

        _soldiers.Clear();
    }

    // One of the host's voices as a weapon's sound.
    private sealed class Voice(ITitleVoice voice, Func<Vector3d> listener) : IWeaponVoice
    {
        public void Silence() => voice.Silence();

        public void Keep(string name, Vector3d? positionInUnits) => voice.MoveTo(positionInUnits ?? listener());
    }
}
