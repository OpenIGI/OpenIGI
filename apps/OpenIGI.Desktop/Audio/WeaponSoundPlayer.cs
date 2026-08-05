using OpenIGI.Audio;
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
    public void Tick(bool firedThisTick, WeaponType? weapon)
    {
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

        if (firedThisTick)
        {
            _quietTicks = 0;
            if (_looping is null)
            {
                _loop = Play(weapon.FireSound, looping: true);
                _looping = weapon;
            }
            else
            {
                // Nothing to re-aim -- the gun is in the listener's own hands. A soldier's weapon will
                // want a positional voice that follows the shooter, and that is a different caller.
                _sounds.SetVoiceParameters(_loop, Parameters(looping: true));
            }

            return;
        }

        if (_looping is not null && ++_quietTicks >= TicksBeforeLoopEnds)
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

    private AudioVoiceHandle Play(string name, bool looping = false) =>
        string.IsNullOrEmpty(name)
            ? AudioVoiceHandle.None
            : _sounds.Play(name, Parameters(looping));

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
    private static VoiceParameters Parameters(bool looping) =>
        VoiceParameters.Default with
        {
            Category = SoundCategory.Effects,
            Looping = looping,
            IsPositional = false,
            Pan = 0f,

            // Well above ambience and footsteps. The player's own weapon going silent because a busy
            // level filled the pool is the single most noticeable way a mix can fail.
            Priority = 200,
        };
}
