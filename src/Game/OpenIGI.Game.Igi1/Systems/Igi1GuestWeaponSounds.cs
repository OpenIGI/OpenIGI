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

    // The loops the player holds: a grenade cooking, and the lock it picks or the keys it types.
    private (string Name, ITitleVoice Voice)? _cook;
    private (string Name, ITitleVoice Voice)? _work;

    /// <inheritdoc/>
    public IWeaponVoice? Play(string name, Vector3d? positionInUnits, bool looping) =>
        audio.Play(name, positionInUnits ?? listener(), looping ? true : null) is { } voice ? new Voice(voice, listener) : null;

    /// <summary>
    /// Sounds the player for a tick as the game's own mission does (<c>Igi1MissionAudio.SoundPlayerWeaponAndBody</c>): the weapon
    /// in its hands, the launcher's report, a grenade cooking, what its weapon's clip says of itself -- a reload, a draw, a
    /// syringe -- its jump, and the lock it picks or the keys it types.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="triggers">The game's animation triggers, which name the sounds a clip's events make; <see langword="null"/> for none.</param>
    public void TickPlayer(Igi1.Player.PlayerCamera player, Igi1.Player.AnimTriggerRegistry? triggers)
    {
        ArgumentNullException.ThrowIfNull(player);
        (_player ??= new WeaponSoundPlayer(this)).TickPlayer(player);
        if (player.M203LaunchedThisTick)
        {
            audio.Play(TimedActionSounds.M203Launch, player.Weapons?.M203Shots is { Count: > 0 } shots ? shots[0].Origin : player.Body.Eye);
        }

        Hold(ref _cook, player.IsGrenadeTicking ? TimedActionSounds.GrenadeCook : null, listener());
        if (triggers is not null && player.View is { } view)
        {
            foreach (string sound in triggers.SoundsCrossed(view.CrossedEvent))
            {
                audio.Play(sound, listener());
            }
        }

        if (player.Body.TookOffThisTick)
        {
            audio.Play(OpenIGI.Game.Player.PlayerBody.JumpSoundName, listener());
        }

        int state = player.InteractionLocomotionState;
        Hold(ref _work, TimedActionSounds.LoopOf(state), player.Body.Position);
        if (TimedActionSounds.EndOf(player.TakeCompletedActionState()) is { } done)
        {
            audio.Play(done, player.Body.Position);
        }
    }

    // Keeps one looping sound going while it is named, where it is; another name, or none, ends it.
    private void Hold(ref (string Name, ITitleVoice Voice)? held, string? name, Vector3d at)
    {
        if (held is { } playing && playing.Name != name)
        {
            playing.Voice.Silence();
            held = null;
        }

        if (name is null)
        {
            return;
        }

        if (held is { } kept)
        {
            kept.Voice.MoveTo(at);
        }
        else if (audio.Play(name, at, looping: true) is { } voice)
        {
            held = (name, voice);
        }
    }

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
        Hold(ref _cook, null, default);
        Hold(ref _work, null, default);
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
