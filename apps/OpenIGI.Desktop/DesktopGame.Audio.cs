using System.Diagnostics;
using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Desktop.Audio;
using OpenIGI.Rendering;
using OpenIGI.UI;

namespace OpenIGI.Desktop;

/// <summary>The audio half of the composition root: opening the device and keeping the buses levelled.</summary>
internal sealed partial class DesktopGame
{
    /// <summary>The menu system's own sound archive, which the original mounts apart from any mission.</summary>
    private const string MenuSoundArchive = "LOCAL:menusystem/sound/sounds.res";

    /// <summary>Every sound the game can name. Rebuilt per mission, as the original rebuilds its table.</summary>
    private SoundBank? _soundBank;

    /// <summary>Plays sounds by name. Null until the device is open.</summary>
    private SoundSystem? _sounds;

    /// <summary>The score. Null until the device is open.</summary>
    private MusicPlayer? _music;

    /// <summary>Makes the player's weapon sound. Null until the device is open.</summary>
    private WeaponSoundPlayer? _weaponSounds;

    /// <summary>When the score's fade was last advanced, as a stopwatch timestamp.</summary>
    private long _audioTimestamp;

    /// <summary>Opens the audio backend and binds the volume variables to it.</summary>
    /// <remarks>
    /// <para>
    /// Called once, after the console has read <c>settings.cfg</c> and the command line's
    /// <c>+statements</c>, because the voice count and the output rate are fixed when the device opens.
    /// The volumes are not — those are pushed on change, so dragging a slider is heard immediately.
    /// </para>
    /// <para>
    /// A backend that cannot open is not an error. <see cref="OpenAlAudioDevice"/> degrades to silence
    /// on its own, so there is nothing here to handle: the game runs either way.
    /// </para>
    /// </remarks>
    private void OpenAudioDevice()
    {
        _audio.Dispose();

        _audio = AudioCVars.Enabled.Value
            ? new OpenAlAudioDevice(AudioDeviceOptions.FromCVars(), _logger)
            : new NullAudioDevice();

        _soundBank = new SoundBank(_fileSystem, _logger);
        _sounds?.Dispose();
        _sounds = new SoundSystem(_audio, _soundBank, _fileSystem, _logger);
        MountMenuSounds();

        // The original hard-cuts between menu and mission themes, so a fade is an addition and belongs
        // to the enhanced profile only. Under vanilla this stays zero and the cut is exact.
        _weaponSounds = new WeaponSoundPlayer(_sounds);

        // The cinematic shell's own feedback. The retail menu system reaches the same two sounds
        // through IMenuHost.PlaySound; the shell's widgets have no host, so they go through a sink.
        SoundSystem shellSounds = _sounds;
        UiSounds.Play = name => shellSounds.Play(
            name,
            VoiceParameters.Default with { Category = SoundCategory.Effects, Priority = 100 });
        _music = new MusicPlayer(_sounds, _logger)
        {
            FadeSeconds = AudioCVars.Spatial.Value ? 1.5f : 0f,
            Volume = 1f,
        };

        ApplyCategoryGains();

        // Only when the front end is where we are going. Launching straight into a mission would
        // otherwise start the menu theme and replace it a second later.
        if (_options.StartMission <= 0)
        {
            _music.Play(MusicPlayer.MenuMusic);
        }

        foreach (ICVar variable in (ICVar[])
                 [
                     AudioCVars.MasterVolume,
                     AudioCVars.EffectsVolume,
                     AudioCVars.MusicVolume,
                     AudioCVars.SpeechVolume,
                     AudioCVars.AmbienceVolume,
                 ])
        {
            variable.Changed += (_, _) => ApplyCategoryGains();
        }
    }

    /// <summary>Advances the device and the score, once a frame.</summary>
    /// <remarks>
    /// The fade is driven from the wall clock rather than from ticks. A fade is presentation, and tying
    /// it to the simulation would make it lurch whenever the loop catches up on a backlog of ticks.
    /// </remarks>
    private void AdvanceAudio()
    {
        UpdateListener();
        _audio.Update();

        long now = Stopwatch.GetTimestamp();
        float delta = _audioTimestamp == 0
            ? 0f
            : (float)Stopwatch.GetElapsedTime(_audioTimestamp, now).TotalSeconds;

        _audioTimestamp = now;
        _music?.Update(delta);
    }

    /// <summary>Puts the listener where the player's eye is.</summary>
    /// <remarks>
    /// <para>
    /// Every frame rather than every tick, and from the same blended pose the view model uses, so a
    /// source does not step across the stereo image at 30 Hz under a camera that glides. The camera
    /// already reports metres — it divides the body's engine units by 4096 — which is the unit the
    /// device works in, so nothing is converted here.
    /// </para>
    /// <para>
    /// Without this, every positional sound in the game would be placed relative to a listener sitting
    /// at the world origin: distance attenuation would key off the corner of the map and the stereo
    /// image would never move.
    /// </para>
    /// </remarks>
    private void UpdateListener()
    {
        if (_player is not { } player)
        {
            return;
        }

        // The same blend the presentation uses, so the ear and the eye agree about where the head is.
        float alpha = RenderCVars.SmoothPresentation.Value
            ? _loop.Clock.TickFraction(_loop.TimeSource.Milliseconds)
            : 1f;

        _audio.Listener = new AudioListenerState
        {
            Position = player.Position,
            Orientation = player.SmoothedLookRotation(alpha),
        };
    }

    /// <summary>Pushes the volume variables onto the device's buses.</summary>
    /// <remarks>
    /// The master gain is folded into each bus rather than applied separately, because the device model
    /// has one gain per bus and no master of its own — the original has no master either, only the three
    /// per-profile volumes.
    /// </remarks>
    private void ApplyCategoryGains()
    {
        float master = AudioCVars.MasterVolume.Value;

        _audio.SetCategoryGain(SoundCategory.Effects, master * AudioCVars.EffectsVolume.Value);
        _audio.SetCategoryGain(SoundCategory.Music, master * AudioCVars.MusicVolume.Value);
        _audio.SetCategoryGain(SoundCategory.Speech, master * AudioCVars.SpeechVolume.Value);
        _audio.SetCategoryGain(SoundCategory.Ambience, master * AudioCVars.AmbienceVolume.Value);
    }

    /// <summary>Empties the bank and re-mounts what does not belong to a mission.</summary>
    /// <remarks>
    /// Called before each mission load, so the mission's own directory is mounted into a bank holding
    /// only the menu sounds. The original rebuilds its table the same way; leaving the previous level's
    /// sounds in place would let a name resolve to a sound the new level never shipped.
    /// </remarks>
    private void ResetSoundsForMission()
    {
        _weaponSounds?.Reset();
        _sounds?.UnloadBuffers();
        _soundBank?.Clear();
        MountMenuSounds();
    }

    /// <summary>Mounts the sound directories a mission plays out of.</summary>
    /// <remarks>
    /// Called from <c>BringMissionUp</c>, which is the one place BOTH routes into a mission pass
    /// through. A cold start has already had these mounted by the mission loader and this adds nothing
    /// -- the first mount of a name wins. A mission PROMOTED from the menu's backdrop never went through
    /// that loader at all, so without this its sounds and its score are simply absent, which is exactly
    /// what pressing Play in the menu used to give: silence.
    /// </remarks>
    private void MountMissionSounds(LoadedMission loaded)
    {
        if (_soundBank is null)
        {
            return;
        }

        // Bind MISSION: first. A cold start had the mission loader do it; a PROMOTED backdrop was loaded
        // on a worker against its own file system, so this one has never been pointed at the level and
        // "MISSION:sounds" would not resolve at all.
        _fileSystem.SetAlias(VirtualFileSystem.MissionAlias, loaded.Descriptor.MissionAliasTarget);

        _soundBank.MountDirectory(MissionLoader.CommonSoundDirectory);
        _soundBank.MountDirectory(MissionLoader.MissionSoundDirectory);
    }

    /// <summary>Mounts the menu system's sound archive, which belongs to no mission.</summary>
    private void MountMenuSounds()
    {
        if (_soundBank is null)
        {
            return;
        }

        int mounted = _soundBank.MountArchive(MenuSoundArchive);
        if (mounted == 0)
        {
            _logger.Warning("Audio", $"No menu sounds in '{MenuSoundArchive}'.");
        }
    }
}
