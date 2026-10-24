using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Host.Audio;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The remaster's weapon wheel: held open on its key, steered by the mouse, and the world slowed while it is.</summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The wheel belongs to the remastered display (<see cref="HudCVars.RemasteredHud"/>) and to whichever player
/// stands in the mission, the game's own or a title's (<see cref="IWeaponWheelPlayer"/>). It opens only where the player would
/// take the look this frame -- not in a menu, the console, a computer, a cutscene, on a ladder or dead -- and any of those taking
/// the frame shuts it, choosing nothing.
/// </para>
/// <para>
/// While it is open the player is held still: the mouse steers the wheel and the keys do nothing. Time slows to
/// <see cref="WheelTimeScale"/> as it opens and comes back as it shuts, and every sound with the music is pitched down and muffled
/// with it (<see cref="OpenIGI.Audio.IAudioDevice.SetTimeWarp"/>), by pushing the clock's time base on by the share of each
/// frame not to be counted (<see cref="Engine.Time.GameClock.TimeAdjustMilliseconds"/>): the schedule stretches for both games
/// at once and nothing in either's tick changes. Not under lockstep, whose synthetic clock moves only as frames render, and
/// they render only as ticks fall due: pushing the schedule there would hold both still for ever.
/// </para>
/// </remarks>
public sealed partial class GameHost
{
    /// <summary>How fast the world runs while the wheel is fully open, against real time.</summary>
    public const float WheelTimeScale = 0.2f;

    /// <summary>The rate every sound plays at while the wheel is fully open: a little under an octave down.</summary>
    public const float WheelAudioPitch = 0.6f;

    private float _wheelTimeCarry;
    private bool _wheelSteered;

    /// <summary>Gets the player whose weapons the wheel offers, the title's own or the game's.</summary>
    private IWeaponWheelPlayer? WheelPlayer => (TitlePlayer as IWeaponWheelPlayer) ?? (HostedPlayer as IWeaponWheelPlayer);

    /// <summary>Gets a value indicating whether the wheel holds the player's input this frame.</summary>
    private bool WheelHoldsInput => WheelPlayer?.Wheel.IsOpen == true;

    /// <summary>Opens, steers and closes the wheel, where the player would otherwise take the look this frame.</summary>
    /// <param name="rawX">The mouse's travel across this frame.</param>
    /// <param name="rawY">The mouse's travel down this frame.</param>
    /// <returns>Whether the wheel is open and has the mouse; the player then takes no look.</returns>
    private bool SteerWeaponWheel(float rawX, float rawY)
    {
        if (!HudCVars.RemasteredHud.Value || _mission.IsPlayerDead || WheelPlayer is not { } player)
        {
            return false;
        }

        WeaponWheel wheel = player.Wheel;
        bool held = PlayerProfileInputBindings.IsDown(
            _configuration.Active, _window.Input, PlayerInputBindings.ConfigName(PlayerAction.WeaponWheel));
        if (!wheel.IsOpen)
        {
            if (!held)
            {
                return false;
            }

            (IReadOnlyList<string> offered, int current) = player.WheelChoices();
            if (offered.Count == 0)
            {
                return false;
            }

            wheel.Open(offered.Count, current);
        }
        else if (!held)
        {
            int pick = wheel.Close();
            (IReadOnlyList<string> choices, _) = player.WheelChoices();
            if (pick >= 0 && pick < choices.Count)
            {
                player.ChooseFromWheel(choices[pick]);
                PlayWheelSound(WeaponWheelSounds.ChooseSoundName, 0.8f);
            }

            return false;
        }

        _wheelSteered = true;
        if (wheel.Steer(rawX, rawY))
        {
            PlayWheelSound(WeaponWheelSounds.TickSoundName, 0.55f);
        }

        return true;
    }

    /// <summary>Ends the wheel's frame: shuts it if nothing steered it, eases it open or shut, and slows the clock by how open it is.</summary>
    /// <param name="elapsed">The real time since the last frame, in seconds.</param>
    private void FinishWeaponWheel(float elapsed)
    {
        WeaponWheel? wheel = WheelPlayer?.Wheel;
        if (wheel is { IsOpen: true } && !_wheelSteered)
        {
            wheel.Cancel();
        }

        _wheelSteered = false;
        wheel?.Animate(elapsed);
        float openness = _options.Lockstep ? 0f : wheel?.Openness ?? 0f;
        float scale = 1f + ((WheelTimeScale - 1f) * openness);
        float stretch = (elapsed * 1000f * (1f - scale)) + _wheelTimeCarry;
        int whole = (int)stretch;
        _wheelTimeCarry = openness > 0f ? stretch - whole : 0f;
        _loop.Clock.TimeAdjustMilliseconds = whole;

        // And the world is heard slowed: every voice and the music pitched down and muffled as the wheel opens.
        _audioOut.Device.SetTimeWarp(1f - ((1f - WheelAudioPitch) * openness), openness);
    }

    private void PlayWheelSound(string name, float gain) => _audioOut.Sounds?.PlayExternal(name, gain: gain, priority: 180);
}
