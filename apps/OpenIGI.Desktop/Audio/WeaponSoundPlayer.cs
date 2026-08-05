using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Desktop.Audio;

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
internal sealed class WeaponSoundPlayer
{
    /// <summary>How many ticks of not firing end an automatic's loop.</summary>
    /// <remarks>
    /// One tick of silence is not enough: a burst weapon has gaps between rounds by construction, and
    /// cutting the loop in one of them would stutter. Two ticks is longer than any shipped weapon's
    /// round interval and shorter than a deliberate release.
    /// </remarks>
    private const int TicksBeforeLoopEnds = 2;

    private readonly SoundSystem _sounds;

    private AudioVoiceHandle _loop;
    private WeaponType? _looping;
    private int _quietTicks;

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
    /// <param name="positionInUnits">Where the shot came from, in engine units.</param>
    public void Tick(bool firedThisTick, WeaponType? weapon, Vector3d positionInUnits)
    {
        // A weapon change mid-burst has to take its loop with it, or an AK's loop keeps running under a
        // drawn knife.
        if (_looping is not null && !ReferenceEquals(_looping, weapon))
        {
            EndLoop(positionInUnits);
        }

        if (weapon is null)
        {
            return;
        }

        if (weapon.MaximumRoundsPerBurst <= 1)
        {
            if (firedThisTick)
            {
                Play(weapon.FireSound, positionInUnits);
            }

            return;
        }

        if (firedThisTick)
        {
            _quietTicks = 0;
            if (_looping is null)
            {
                _loop = Play(weapon.FireSound, positionInUnits, looping: true);
                _looping = weapon;
            }
            else
            {
                // Follow the shooter: the player's own gun is at the player's own ear, but the same code
                // serves a soldier's, and a firing soldier walks.
                _sounds.SetVoiceParameters(_loop, Parameters(positionInUnits, looping: true));
            }

            return;
        }

        if (_looping is not null && ++_quietTicks >= TicksBeforeLoopEnds)
        {
            EndLoop(positionInUnits);
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
    }

    private void EndLoop(Vector3d positionInUnits)
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
            Play(weapon.FireLoopEndSound, positionInUnits);
        }
    }

    private AudioVoiceHandle Play(string name, Vector3d positionInUnits, bool looping = false) =>
        string.IsNullOrEmpty(name)
            ? AudioVoiceHandle.None
            : _sounds.Play(name, Parameters(positionInUnits, looping));

    private static VoiceParameters Parameters(Vector3d positionInUnits, bool looping) =>
        (VoiceParameters.Default with
        {
            Category = SoundCategory.Effects,
            Looping = looping,

            // Well above ambience and footsteps. The player's own weapon going silent because a busy
            // level filled the pool is the single most noticeable way a mix can fail.
            Priority = 200,
        }).At(SoundSystem.ToMetres(positionInUnits), 4f, 120f);
}
