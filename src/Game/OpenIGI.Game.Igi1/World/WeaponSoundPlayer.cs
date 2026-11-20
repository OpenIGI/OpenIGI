using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Makes a gun sound, on the two-sample rule the weapon records are built around.
/// </summary>
/// <remarks>
/// <para>
/// A weapon row carries two sound names, and which of them is used depends on a third field.
/// <c>Maximum Rounds/Burst</c> of one or less means a single sample per shot — a pistol, a rifle, the
/// shotgun. Above one and the gun starts its fire sound as a <b>loop</b> when it opens up and stops the
/// loop with a separate <b>loop-end</b> sample when it stops, which is what the second name is for and
/// why an automatic's fire sound in the data is named <c>ak47_loop</c> rather than <c>ak47_1</c>.
/// </para>
/// <para>
/// Playing an automatic as one sample per round instead would be both wrong and worse: the round rate
/// is faster than the sample is long, so the shots would overlap into mush and burn a voice each.
/// </para>
/// </remarks>
public sealed class WeaponSoundPlayer
{
    private readonly IWeaponSoundOutput _output;

    private IWeaponVoice? _loop;
    private WeaponType? _looping;
    private int _quietTicks;
    private Vector3d? _positionInUnits;

    /// <summary>Initializes a new instance of the <see cref="WeaponSoundPlayer"/> class.</summary>
    /// <param name="sounds">Where sounds are played.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sounds"/> is <see langword="null"/>.</exception>
    public WeaponSoundPlayer(SoundSystem sounds)
        : this(new SoundSystemOutput(sounds ?? throw new ArgumentNullException(nameof(sounds))))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WeaponSoundPlayer"/> class over whatever plays its sounds.</summary>
    /// <param name="output">What plays a weapon's sounds: the game's own sound system, or another game's audio in its level.</param>
    public WeaponSoundPlayer(IWeaponSoundOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Advances one tick of a weapon's fire sound.</summary>
    /// <param name="firedThisTick">Whether a round left the barrel this tick.</param>
    /// <param name="weapon">The weapon in hand, or <see langword="null"/> when there is none.</param>
    /// <param name="positionInUnits">The world emitter for an AI weapon; null for the player's gun.</param>
    /// <param name="triggerHeld">
    /// The gun's trigger as its fire handler gated it, where the caller knows it. An automatic's loop then runs while the
    /// trigger is held and ends, with its tail, on the first tick it is not -- <c>0x478BA0</c>'s rule -- rather than after a
    /// cadence's worth of quiet.
    /// </param>
    public void Tick(bool firedThisTick, WeaponType? weapon, Vector3d? positionInUnits = null, bool? triggerHeld = null)
    {
        _positionInUnits = positionInUnits;

        // A weapon change mid-burst has to take its loop with it, or an AK's loop keeps running under a
        // drawn knife.
        if (_looping is not null && !ReferenceEquals(_looping, weapon))
        {
            EndLoop();
        }

        if (weapon is null)
        {
            return;
        }

        if (weapon.MaximumRoundsPerBurst <= 1)
        {
            if (firedThisTick)
            {
                Play(weapon.FireSound);
            }

            return;
        }

        if (triggerHeld is bool trigger)
        {
            if (trigger && _looping is null)
            {
                _loop = Play(weapon.FireSound, looping: true);
                _looping = _loop is not null ? weapon : null;
            }
            else if (!trigger && _looping is not null)
            {
                EndLoop();
            }

            return;
        }

        if (firedThisTick)
        {
            _quietTicks = 0;
            if (_looping is null)
            {
                _loop = Play(weapon.FireSound, looping: true);
                // A full voice pool (or an audio device that is recovering) returns no handle. Do
                // not remember a failed start as a live loop: doing so makes every later round only
                // update handle zero and leaves an automatic weapon permanently silent. The next
                // fired tick must be allowed to retry; this matters most for the MIL, whose retail
                // loop is opened while a mission can still be filling the effects pool.
                if (_loop is not null)
                {
                    _looping = weapon;
                }
                else
                {
                    _looping = null;
                }
            }
            else
            {
                // Nothing to re-aim -- the gun is in the listener's own hands. A soldier's weapon will
                // want a positional voice that follows the shooter, and that is a different caller.
                _loop?.Keep(weapon.FireSound, _positionInUnits);
            }

            return;
        }

        // A loop must survive the weapon's own cadence gap. At 400 RPM that gap is four simulation
        // ticks, so the old fixed two-tick threshold stopped and restarted the sample between rounds.
        int endAfter = Math.Max(2, GunTiming.TicksPerRound(weapon.RoundsPerMinute) + 1);
        if (_looping is not null && ++_quietTicks >= endAfter)
        {
            EndLoop();
        }
    }

    /// <summary>
    /// Sounds the weapon in a player's hands for a tick: its report on the round it fired, or its loop while the trigger is held.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <remarks>
    /// <para>
    /// A medipack is not a firearm (<c>FUN_0040FB30</c>/<c>0x40FB90</c> drive its animation tasks directly), so its weapon record
    /// is never fed through: some live rows carry a legacy gun sound. Passing none also closes an automatic gun's loop when the
    /// player switches to the syringe.
    /// </para>
    /// <para>
    /// On a stationary gun the hand's trigger is not the one pulled: the gun's own weapon fires (<c>0x474360</c> hands it the fire
    /// button), so its report runs on the rounds it fires, as a soldier's does. An under-barrel launch is not the rifle's own
    /// report; whoever plays that one plays it apart.
    /// </para>
    /// </remarks>
    public void TickPlayer(Player.PlayerCamera player)
    {
        ArgumentNullException.ThrowIfNull(player);
        WeaponType? weapon = player.HeldWeaponType?.Category == WeaponCategory.Medipack ? null : player.HeldWeaponType;
        bool launched = player.M203LaunchedThisTick;
        bool? triggerHeld = player.IsMountedStationaryGun
            ? null
            : !launched && weapon is not null && player.Weapons?.TriggerHeldThisTick == true;
        Tick(player.FiredThisTick && !launched && weapon is not null, weapon, triggerHeld: triggerHeld);
    }

    /// <summary>Stops any loop, without playing the tail.</summary>
    /// <remarks>For a mission teardown, where nothing should be left sounding.</remarks>
    public void Reset()
    {
        _loop?.Silence();
        _loop = null;
        _looping = null;
        _quietTicks = 0;
        _positionInUnits = null;
    }

    private void EndLoop()
    {
        WeaponType? weapon = _looping;
        _loop?.Silence();
        _loop = null;
        _looping = null;
        _quietTicks = 0;

        if (weapon is not null)
        {
            Play(weapon.FireLoopEndSound);
        }
    }

    private IWeaponVoice? Play(string name, bool looping = false) =>
        string.IsNullOrEmpty(name) ? null : _output.Play(name, _positionInUnits, looping);

    // The game's own sound system as a weapon's sounds play through it.
    private sealed class SoundSystemOutput(SoundSystem sounds) : IWeaponSoundOutput
    {
        public IWeaponVoice? Play(string name, Vector3d? positionInUnits, bool looping)
        {
            // Weapon rows carry definition names, not guaranteed raw bank keys. This matters for the
            // MIL: IGI1 defines mil_loop in COMMON/SOUNDS/SOUNDS.QVM, and bypassing that definition can
            // hand the backend a non-playable/missing bank key and leave the weapon completely silent.
            if (sounds.Definitions?.TryResolve(name, out SoundDefinition? definition) == true
                && definition is { IsPlayable: true })
            {
                AudioVoiceHandle voice = sounds.PlayDefinition(
                    name,
                    positionInUnits,
                    priority: 200,
                    looping: looping);
                if (voice.IsValidHandle)
                {
                    return new Voice(this, voice);
                }
            }

            // Keep the raw-bank fallback for weapon records that are authored without a sound definition.
            AudioVoiceHandle raw = sounds.Play(BankKey(name), Parameters(name, positionInUnits, looping));
            return raw.IsValidHandle ? new Voice(this, raw) : null;
        }

        private SoundSystem Sounds => sounds;

        private string BankKey(string name) =>
            sounds.Definitions?.TryResolve(name, out SoundDefinition? definition) == true
                && definition is { IsPlayable: true }
                    ? definition.BankKey
                    : name;

        private VoiceParameters Parameters(string name, Vector3d? positionInUnits, bool looping)
        {
            float gain = 1f;
            float minimum = 1f;
            float maximum = 50f;
            if (sounds.Definitions?.TryResolve(name, out SoundDefinition? definition) == true
                && definition is { IsPlayable: true })
            {
                gain = definition.Volume;
                minimum = definition.MinimumDistanceMetres;
                maximum = definition.MaximumDistanceMetres;
                looping |= definition.Loop;
            }

            VoiceParameters parameters = VoiceParameters.Default with
            {
                Category = SoundCategory.Effects,
                Gain = gain,
                Looping = looping,

                // Well above ambience and footsteps. The player's own weapon going silent because a busy
                // level filled the pool is the single most noticeable way a mix can fail.
                Priority = 200,
            };

            return positionInUnits is { } position
                ? parameters.At(SoundSystem.ToMetres(position), minimum, maximum)
                : parameters with { IsPositional = false, Pan = 0f };
        }

        private sealed class Voice(SoundSystemOutput output, AudioVoiceHandle handle) : IWeaponVoice
        {
            public void Silence() => output.Sounds.Stop(handle);

            public void Keep(string name, Vector3d? positionInUnits) =>
                output.Sounds.SetVoiceParameters(handle, output.Parameters(name, positionInUnits, looping: true));
        }
    }
}

/// <summary>What plays a weapon's sounds for a <see cref="WeaponSoundPlayer"/>: a report, a loop, the loop's tail.</summary>
public interface IWeaponSoundOutput
{
    /// <summary>Plays a sound by the name a weapon's row gives it.</summary>
    /// <param name="name">The sound's definition, or its bank key where the row names none.</param>
    /// <param name="positionInUnits">Where it sounds, in world units; <see langword="null"/> for the listener's own hands.</param>
    /// <param name="looping">Whether it loops until it is stopped.</param>
    /// <returns>The voice, or <see langword="null"/> when nothing could play it: a full pool, a sound not found.</returns>
    IWeaponVoice? Play(string name, Vector3d? positionInUnits, bool looping);
}

/// <summary>A weapon's sound while it plays.</summary>
public interface IWeaponVoice
{
    /// <summary>Silences it.</summary>
    void Silence();

    /// <summary>Keeps a loop going through another round, where it stands now.</summary>
    /// <param name="name">The loop's sound.</param>
    /// <param name="positionInUnits">Where it sounds, or <see langword="null"/> for the listener's own hands.</param>
    void Keep(string name, Vector3d? positionInUnits);
}
