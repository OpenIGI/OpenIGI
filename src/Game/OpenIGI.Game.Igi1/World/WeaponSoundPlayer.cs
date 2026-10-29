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
    private readonly SoundSystem _sounds;

    private AudioVoiceHandle _loop;
    private WeaponType? _looping;
    private int _quietTicks;
    private Vector3d? _positionInUnits;

    /// <summary>Initializes a new instance of the <see cref="WeaponSoundPlayer"/> class.</summary>
    /// <param name="sounds">Where sounds are played.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sounds"/> is <see langword="null"/>.</exception>
    public WeaponSoundPlayer(SoundSystem sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        _sounds = sounds;
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
                _looping = _loop.IsValidHandle ? weapon : null;
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
                if (_loop.IsValidHandle)
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
                _sounds.SetVoiceParameters(_loop, Parameters(weapon.FireSound, looping: true));
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

    /// <summary>Stops any loop, without playing the tail.</summary>
    /// <remarks>For a mission teardown, where nothing should be left sounding.</remarks>
    public void Reset()
    {
        if (_loop.IsValidHandle)
        {
            _sounds.Stop(_loop);
        }

        _loop = AudioVoiceHandle.None;
        _looping = null;
        _quietTicks = 0;
        _positionInUnits = null;
    }

    private void EndLoop()
    {
        WeaponType? weapon = _looping;
        if (_loop.IsValidHandle)
        {
            _sounds.Stop(_loop);
        }

        _loop = AudioVoiceHandle.None;
        _looping = null;
        _quietTicks = 0;

        if (weapon is not null)
        {
            Play(weapon.FireLoopEndSound);
        }
    }

    private AudioVoiceHandle Play(string name, bool looping = false)
    {
        if (string.IsNullOrEmpty(name))
        {
            return AudioVoiceHandle.None;
        }

        // Weapon rows carry definition names, not guaranteed raw bank keys. This matters for the
        // MIL: IGI1 defines mil_loop in COMMON/SOUNDS/SOUNDS.QVM, and bypassing that definition can
        // hand the backend a non-playable/missing bank key and leave the weapon completely silent.
        if (_sounds.Definitions?.TryResolve(name, out SoundDefinition? definition) == true
            && definition is { IsPlayable: true })
        {
            AudioVoiceHandle voice = _sounds.PlayDefinition(
                name,
                _positionInUnits,
                priority: 200,
                looping: looping);
            if (voice.IsValidHandle)
            {
                return voice;
            }
        }

        // Keep the raw-bank fallback for weapon records that are authored without a sound definition.
        return _sounds.Play(BankKey(name), Parameters(name, looping));
    }

    private string BankKey(string name) =>
        _sounds.Definitions?.TryResolve(name, out SoundDefinition? definition) == true
            && definition is { IsPlayable: true }
                ? definition.BankKey
                : name;

    /// <summary>The voice a shot is played on.</summary>
    /// <remarks>
    /// <para>
    /// <b>The player's own weapon is NOT positional, and making it so was wrong.</b> The gun is held in
    /// the player's own hands: its position relative to the head does not change, however much the
    /// camera turns. Placing it in the world and letting the listener rotate around it made the shot
    /// slide across the stereo image as the mouse moved, which is exactly the thing that cannot happen
    /// in reality — the camera moved, the gun did not.
    /// </para>
    /// <para>
    /// It is also what the original does, though by accident rather than intent: it plays the shot at
    /// the player's own position, and its pan is the lateral offset from the listener, which for a
    /// source sitting on the listener is zero. A flat centred voice reaches the same place directly.
    /// </para>
    /// </remarks>
    private VoiceParameters Parameters(string name, bool looping)
    {
        float gain = 1f;
        float minimum = 1f;
        float maximum = 50f;
        if (_sounds.Definitions?.TryResolve(name, out SoundDefinition? definition) == true
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

        return _positionInUnits is { } position
            ? parameters.At(SoundSystem.ToMetres(position), minimum, maximum)
            : parameters with { IsPositional = false, Pan = 0f };
    }
}
