using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Scripting.Runtime;
using System.Diagnostics;

namespace OpenIGI.Host.Services;

/// <summary>
/// The game's audio output: the device, the bank of every sound the game can name, the system that plays them, the
/// definitions game code names them by, and the score.
/// </summary>
/// <param name="factory">Opens the audio backend.</param>
/// <param name="fileSystem">The game's files, which the bank mounts sounds from.</param>
/// <param name="logger">Where the device and the bank report.</param>
/// <param name="menuSoundArchive">The menu system's own sound archive, which the original mounts apart from any mission.</param>
internal sealed class AudioOutput(
    IAudioDeviceFactory factory,
    VirtualFileSystem fileSystem,
    IOpenIgiLogger logger,
    string menuSoundArchive) : IHostService
{
    // When the score's fade was last advanced, as a stopwatch timestamp.
    private long _timestamp;

    /// <summary>
    /// What every name game code says resolves to. Built once the script namespace exists, and reloaded per mission
    /// because a mission ships its own <c>sounds/sounds.qvm</c> on top of the shared one.
    /// </summary>
    private SoundDefinitionRegistry? _definitions;

    /// <summary>Gets the device. Silent until it is opened, and silent for good where no backend opens.</summary>
    public IAudioDevice Device { get; private set; } = new NullAudioDevice();

    /// <summary>Gets every sound the game can name. Rebuilt per mission, as the original rebuilds its table.</summary>
    public SoundBank? Bank { get; private set; }

    /// <summary>Gets what plays sounds by name. Null until the device is open.</summary>
    public SoundSystem? Sounds { get; private set; }

    /// <summary>Gets the score. Null until the device is open.</summary>
    public MusicPlayer? Music { get; private set; }

    /// <inheritdoc/>
    public string Name => "Audio output";

    /// <summary>Opens the audio backend, the bank and the score, and levels the buses.</summary>
    /// <remarks>
    /// <para>
    /// Called once, after the console has read <c>settings.cfg</c> and the command line's <c>+statements</c>, because
    /// the voice count and the output rate are fixed when the device opens. The volumes are not -- those are pushed
    /// on change through <see cref="ApplyCategoryGains"/>.
    /// </para>
    /// <para>
    /// A backend that cannot open is not an error. The factory returns a silent device rather than throwing, so
    /// there is nothing here to handle: the game runs either way.
    /// </para>
    /// </remarks>
    public void Open()
    {
        Device.Dispose();

        Device = AudioCVars.Enabled.Value
            ? factory.Open(AudioDeviceOptions.FromCVars(), logger)
            : new NullAudioDevice();

        Bank = new SoundBank(fileSystem, logger);
        Sounds?.Dispose();
        Sounds = new SoundSystem(Device, Bank, fileSystem, logger);

        // The remaster's flashlight switch, which the game's banks have no sound for.
        Sounds.RegisterExternal(FlashlightClick.OnSoundName, FlashlightClick.Format, FlashlightClick.OnPcm);
        Sounds.RegisterExternal(FlashlightClick.OffSoundName, FlashlightClick.Format, FlashlightClick.OffPcm);

        // And the lamps' starters, ignitors and ballasts, which the original's never needed: they never change.
        foreach ((string name, byte[] pcm) in LampSounds.All)
        {
            Sounds.RegisterExternal(name, LampSounds.Format, pcm);
        }

        // And the remaster's weapon wheel and crosshair.
        foreach ((string name, byte[] pcm) in WeaponWheelSounds.All)
        {
            Sounds.RegisterExternal(name, WeaponWheelSounds.Format, pcm);
        }

        foreach ((string name, AudioFormat format, byte[] pcm) in HitSounds.Current.Sounds)
        {
            Sounds.RegisterExternal(name, format, pcm);
        }

        MountMenuSounds();

        // The original hard-cuts between menu and mission themes, so a fade is an addition and belongs to the
        // enhanced profile only. Under vanilla this stays zero and the cut is exact.
        Music = new MusicPlayer(Sounds, logger)
        {
            FadeSeconds = AudioCVars.Spatial.Value ? 1.5f : 0f,
            Volume = 1f,
        };

        ApplyCategoryGains();
    }

    /// <summary>Pushes the volume variables onto the device's buses.</summary>
    /// <remarks>
    /// The master gain is folded into each bus rather than applied separately, because the device model has one gain
    /// per bus and no master of its own -- the original has no master either, only the three per-profile volumes.
    /// </remarks>
    public void ApplyCategoryGains()
    {
        float master = AudioCVars.MasterVolume.Value;

        Device.SetCategoryGain(SoundCategory.Effects, master * AudioCVars.EffectsVolume.Value);
        Device.SetCategoryGain(SoundCategory.Music, master * AudioCVars.MusicVolume.Value);
        Device.SetCategoryGain(SoundCategory.Speech, master * AudioCVars.SpeechVolume.Value);
        Device.SetCategoryGain(SoundCategory.Ambience, master * AudioCVars.AmbienceVolume.Value);
    }

    /// <summary>Advances the device and the score, once a frame.</summary>
    /// <returns>The wall-clock seconds since the last advance; zero on the first.</returns>
    /// <remarks>
    /// The fade is driven from the wall clock rather than from ticks. A fade is presentation, and tying it to the
    /// simulation would make it lurch whenever the loop catches up on a backlog of ticks.
    /// </remarks>
    public float Advance()
    {
        Device.Update();

        long now = Stopwatch.GetTimestamp();
        float delta = _timestamp == 0
            ? 0f
            : (float)Stopwatch.GetElapsedTime(_timestamp, now).TotalSeconds;

        _timestamp = now;
        Music?.Update(delta);
        return delta;
    }

    /// <summary>Stops the score, frees the buffers, and empties the bank down to the menu's sounds.</summary>
    /// <remarks>
    /// <para>
    /// Called before each mission load, so the mission's own directory is mounted into a bank holding only the menu
    /// sounds. The original rebuilds its table the same way; leaving the previous level's sounds in place would let a
    /// name resolve to a sound the new level never shipped.
    /// </para>
    /// <para>
    /// Clearing is the only thing that can retire a name, because the bank keeps the FIRST mount of one --
    /// faithfully, since the original's loader reports <c>"SOUND - File %s already loaded!"</c> rather than
    /// replacing. Every mission ships its own <c>sounds/game_music.wav</c>, and a mounted entry holds the path it was
    /// found at rather than the mission alias it was found through, so a bank carried across a load answers
    /// <c>game_music</c> with the previous level's score forever.
    /// </para>
    /// </remarks>
    public void ResetForMission()
    {
        // Before the buffers, and explicitly rather than through the device-wide stop UnloadBuffers does: the score
        // has to be told, not merely silenced. It treats a repeat of the playing track as a no-op so that re-entering
        // the mission state does not stutter the music, and a track it still believes is playing is a track the next
        // Play(game_music) would decline to start.
        Music?.Stop();

        Sounds?.UnloadBuffers();
        Bank?.Clear();
        MountMenuSounds();
    }

    /// <summary>Forgets the definitions, as the title changes.</summary>
    /// <remarks>
    /// They are loaded per mission from the title's own resources, and <see cref="MountMission"/> only builds a
    /// registry when there is none -- so a registry carried across a switch would hand the incoming game the outgoing
    /// one's names.
    /// </remarks>
    public void ForgetDefinitions()
    {
        _definitions = null;
        if (Sounds is not null)
        {
            Sounds.Definitions = null;
        }
    }

    /// <summary>Mounts the sound directories a mission plays out of, and the definitions that name them.</summary>
    /// <param name="resources">The mission's resources, which hold its definitions.</param>
    /// <param name="natives">The script namespace the mission's scripts resolve against.</param>
    /// <remarks>
    /// The mission's alias must already point at the level. A cold start has already had these mounted by the
    /// mission loader and this adds nothing -- the first mount of a name wins.
    /// </remarks>
    public void MountMission(ResourceRegistry resources, NativeRegistry natives)
    {
        if (Bank is null)
        {
            return;
        }

        Bank.MountDirectory(MissionLoader.CommonSoundDirectory);
        Bank.MountDirectory(MissionLoader.MissionSoundDirectory);

        // And the definitions that give those banked sounds their names. Game code says `ai_death_1`, which is a
        // definition; the bank holds `AI_DEATH_01`. Without this every game-side sound resolves to nothing at all.
        //
        // Registered per mission, and not once at start-up: a second GameLoop is built for every mission the menu
        // previews and one of them takes the session over, so the start-up namespace is NOT the one a promoted
        // mission's scripts resolve against. Re-registering a name rebinds it rather than failing.
        _definitions ??= new SoundDefinitionRegistry(logger);
        _definitions.Register(natives);
        _definitions.LoadForMission(resources, natives);

        if (Sounds is not null)
        {
            Sounds.Definitions = _definitions;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Device.Dispose();

    // Mounts the menu system's sound archive, which belongs to no mission.
    private void MountMenuSounds()
    {
        if (Bank is null)
        {
            return;
        }

        int mounted = Bank.MountArchive(menuSoundArchive);
        if (mounted == 0)
        {
            logger.Warning("Audio", $"No menu sounds in '{menuSoundArchive}'.");
        }
    }
}
