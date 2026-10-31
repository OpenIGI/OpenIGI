using System.Diagnostics;
using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Services;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;

namespace OpenIGI.Host;

/// <summary>The audio half of the composition root: opening the device and keeping the buses levelled.</summary>
public sealed partial class GameHost
{
    /// <summary>The menu system's own sound archive, which the original mounts apart from any mission.</summary>
    private const string MenuSoundArchive = "LOCAL:menusystem/sound/sounds.res";

    // The device, the bank, the sounds by name, their definitions and the score.
    private readonly AudioOutput _audioOut;

    // The voices the level's cutscenes start, which a skip silences.
    private readonly CutsceneSounds _cutsceneSounds;
    /// <summary>The weather, heard. Null until the device is open; silent until a level rains.</summary>
    private RainAmbience? _rainAmbience;

    /// <summary>When the rain mix was last reported to the log, as a stopwatch timestamp.</summary>
    private long _rainDebugTimestamp;

    /// <summary>Opens the audio output, and binds the volume variables to it.</summary>
    /// <remarks>
    /// Called once, after the console has read <c>settings.cfg</c> and the command line's
    /// <c>+statements</c>, because the voice count and the output rate are fixed when the device opens.
    /// The volumes are not: those are pushed on change, so dragging a slider is heard immediately.
    /// </remarks>
    private void OpenAudioDevice()
    {
        _audioOut.Open();
        LoadRainAmbience();
        _hosted.Audio.OnDeviceOpened();

        // Only when the front end is where we are going. Launching straight into a mission would
        // otherwise start the menu theme and replace it a second later.
        if (_options.StartMission <= 0)
        {
            _audioOut.Music?.Play(MusicPlayer.MenuMusic);
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
            WhenChanged(variable, _audioOut.ApplyCategoryGains);
        }
    }

    /// <summary>Places the ear, and advances the device, the score and the weather bed, once a frame.</summary>
    private void AdvanceAudio()
    {
        UpdateListener();
        float delta = _audioOut.Advance();
        UpdateRainAmbience(delta);
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
        if (TitlePlayer is { } title)
        {
            // A title's own player: its eyes as the last tick left them, in the camera's own row order.
            _audioOut.Device.Listener = AudioListenerState.FromViewRotation(title.EyePosition / TitleUnitsPerMetre, title.ViewOrientation);
            return;
        }

        if (HostedPlayer is not { } player)
        {
            return;
        }

        // The last frame's camera, whichever drew it; see ComposeFrameEye. The head below is for the frames before a
        // mission has drawn one.
        if (_earCamera is { } ear)
        {
            _audioOut.Device.Listener = AudioListenerState.FromViewRotation(ear.Position, ear.Orientation);
            return;
        }

        // The same blend the presentation uses, so the ear and the eye agree about where the head is.
        float alpha = FrameCVars.SmoothPresentation.Value
            ? _loop.Clock.TickFraction(_loop.Clock.FrameInstantMilliseconds)
            : 1f;

        // The EYE, not the feet: an ear 1.8 m below the head is a metre and a half of error against
        // sounds whose whole audible range is fifty. And through FromViewRotation, because what the
        // camera hands back is a world-to-view rotation with its axes in its rows -- the listener wants
        // the engine eye basis, which keeps them in its columns and in a different order.
        _audioOut.Device.Listener = AudioListenerState.FromViewRotation(
            player.SmoothedEyeMetres(alpha), player.SmoothedLookRotation(alpha));
    }

    /// <summary>Empties the bank and re-mounts what does not belong to a mission.</summary>
    /// <remarks>
    /// <para>
    /// Called before each mission load, so the mission's own directory is mounted into a bank holding
    /// only the menu sounds. The original rebuilds its table the same way; leaving the previous level's
    /// sounds in place would let a name resolve to a sound the new level never shipped.
    /// </para>
    /// <para>
    /// Clearing is the only thing that can retire a name, because the bank keeps the FIRST mount of one
    /// — faithfully, since the original's loader reports <c>"SOUND - File %s already loaded!"</c> rather
    /// than replacing. Every mission ships its own <c>sounds/game_music.wav</c>, and a mounted entry
    /// holds the path it was found at rather than the <c>MISSION:</c> alias it was found through, so a
    /// bank carried across a load answers <c>game_music</c> with the previous level's score forever.
    /// </para>
    /// </remarks>
    private void ResetSoundsForMission()
    {
        _hosted.Audio.ResetForMission();
        _rainAmbience?.Stop();
        _audioOut.ResetForMission();
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
        if (_audioOut.Bank is null)
        {
            return;
        }

        // Bind MISSION: first. A cold start had the mission loader do it; a PROMOTED backdrop was loaded
        // on a worker against its own file system, so this one has never been pointed at the level and
        // "MISSION:sounds" would not resolve at all.
        _fileSystem.SetAlias(VirtualFileSystem.MissionAlias, loaded.Descriptor.MissionAliasTarget);
        if (!ReferenceEquals(_resourceFileSystem, _fileSystem))
        {
            _resourceFileSystem.SetAlias(VirtualFileSystem.MissionAlias, loaded.Descriptor.MissionAliasTarget);
        }

        // And the definitions that give those banked sounds their names, against the namespace this
        // mission's scripts resolve in.
        _audioOut.MountMission(_resources, _loop.Natives);
    }

    /// <summary>Loads the rain recording the weather bed is built from.</summary>
    /// <remarks>
    /// A mono 16-bit PCM WAV under <c>Assets/sounds</c>, prepared as a seamless loop — see the notes
    /// beside the asset. Mono is a requirement rather than a preference: only a mono source is placed
    /// in three dimensions, and placing the rain is the whole point. A missing file is not a fault;
    /// the weather simply stays silent, exactly as it did before this existed.
    /// </remarks>
    private void LoadRainAmbience()
    {
        _rainAmbience?.Dispose();
        _rainAmbience = null;

        if (_audioOut.Sounds is null)
        {
            return;
        }

        RainAmbience rain = new(_audioOut.Sounds, _logger);
        if (rain.TryLoad(Path.Combine(AppContext.BaseDirectory, "Assets", "sounds", "rain-loop.wav")))
        {
            _rainAmbience = rain;
        }
    }

    /// <summary>Drives the weather bed from the level the player is standing in.</summary>
    /// <remarks>
    /// <para>
    /// After <see cref="UpdateListener"/> and from the listener's own position, so the ear the rain is
    /// measured around is the same ear everything else is heard with. Taking the eye from the camera a
    /// second time here would be one blend away from the first and would drift.
    /// </para>
    /// <para>
    /// <b>Whether it rains is the renderer's answer, not a second opinion.</b> The level's own
    /// <c>RainEffect</c> task decides, through the same <c>IsWeatherEnabled</c> the draw path reads,
    /// so forcing the weather on from the console or the campaign screen is heard as well as seen.
    /// What must NOT be consulted is the storm's <c>IsActive</c>: that goes false the moment the
    /// camera is under a roof, which is precisely the case this whole system exists to sound different
    /// in rather than to fall silent in.
    /// </para>
    /// </remarks>
    private void UpdateRainAmbience(float delta)
    {
        if (_rainAmbience is not { } rain)
        {
            return;
        }

        // No player is the menu, where there is no ear to place the weather around.
        bool raining = AudioCVars.Rain.Value && HostedPlayer is not null && (_rain?.IsWeatherEnabled ?? false);
        float intensity = raining
            ? (_storm is { Enabled: true } ? WeatherCVars.StormIntensity.Value : 1f)
            : 0f;

        rain.Update(
            _audioOut.Device.Listener.Position,
            _shelterField,
            HostedPlayer?.GrenadeWalls,
            WorldScene.UnitsPerMetre,
            intensity,
            AudioCVars.RainVolume.Value,
            delta);

        if (!AudioCVars.RainDebug.Value || !rain.IsSounding)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_rainDebugTimestamp != 0 && Stopwatch.GetElapsedTime(_rainDebugTimestamp, now).TotalSeconds < 1.0)
        {
            return;
        }

        _rainDebugTimestamp = now;
        _logger.Information("Audio", $"Rain: {rain.Describe()}");
    }

    /// <summary>Empties the bank and the score for a different game, and starts that game's theme.</summary>
    /// <remarks>
    /// <para>
    /// <b>The menu's sounds belong to the title, not to the session.</b> Everything under
    /// <c>menusystem/sound/</c> is read through <c>LOCAL:</c>, and a title switch re-points that device
    /// at the other installation — so a bank left standing across the switch answers every menu name
    /// with the outgoing game's sound. The bank keeps the FIRST mount of a name and reports a repeat
    /// rather than replacing it, faithfully to the original, which means re-mounting alone cannot
    /// retire anything: only clearing can.
    /// </para>
    /// <para>
    /// The score has to be told as well as silenced, and for two reasons. The player treats a repeat of
    /// the playing track as a no-op so that re-entering the menu does not stutter it, so a switch that
    /// merely asked for <c>menu_music</c> again would be declined; and a voice already streaming holds
    /// the handle it opened, so the outgoing game's theme would go on playing under the incoming game's
    /// menu whatever the bank now says. That is the bug this exists for.
    /// </para>
    /// <para>
    /// The definitions go too. They are loaded per mission from the title's own resources, and
    /// <c>MountMissionSounds</c> only builds a registry when there is none — so a registry carried
    /// across a switch would hand the incoming game the outgoing one's names.
    /// </para>
    /// </remarks>
    private void ResetSoundsForTitle()
    {
        ResetSoundsForMission();

        _audioOut.ForgetDefinitions();

        // A switch always lands in the front end: the played mission was released and the incoming
        // title has not been asked for one, so the menu theme is what should be sounding.
        _audioOut.Music?.Play(MusicPlayer.MenuMusic);
    }

    /// <summary>The cinematic shell's own feedback, played through the title's sound bank as the retail menus' is.</summary>
    /// <param name="host">The host, whose sound system is the one open at the time.</param>
    private sealed class ShellSounds(GameHost host) : IUiSoundSink
    {
        public void Play(string name) => host._audioOut.Sounds?.Play(
            name,
            VoiceParameters.Default with { Category = SoundCategory.Effects, Priority = 100 });
    }

}
