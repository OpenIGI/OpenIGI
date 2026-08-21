using System.Diagnostics;
using OpenIGI.Audio;
using OpenIGI.Audio.OpenAL;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Weapons.Ballistics;
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

    /// <summary>
    /// What every name game code says resolves to. Built once the script namespace exists, and
    /// reloaded per mission because a mission ships its own <c>sounds/sounds.qvm</c> on top of the
    /// shared one.
    /// </summary>
    private SoundDefinitionRegistry? _soundDefinitions;

    /// <summary>The score. Null until the device is open.</summary>
    private MusicPlayer? _music;

    /// <summary>The travelling voice of each door currently in motion.</summary>
    private readonly Dictionary<PlacedDoor, AudioVoiceHandle> _doorVoices = [];
    private readonly Dictionary<PlacedElevator, AudioVoiceHandle> _elevatorVoices =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GameTask, AudioVoiceHandle> _cutsceneVoices = [];
    private readonly HashSet<AudioVoiceHandle> _cutsceneOneShotVoices = [];
    private AudioVoiceHandle _binocularZoomVoice;

    private void SetCutsceneSound(CutsceneSoundEvent sound)
    {
        if (_sounds is null || string.IsNullOrWhiteSpace(sound.Name))
        {
            return;
        }

        // Simple ConditionalSounds have no falling-edge event, but their voices still belong to the
        // cutscene while it is being skipped. Retire completed handles so this set only represents
        // one-shots that can still be stopped by the next cancellation.
        _cutsceneOneShotVoices.RemoveWhere(voice => !_sounds.IsPlaying(voice));

        if (!sound.Simple
            && _cutsceneVoices.Remove(sound.Task, out AudioVoiceHandle old)
            && old.IsValidHandle)
        {
            _sounds.Stop(old);
        }

        if (sound.Playing)
        {
            AudioVoiceHandle voice = _sounds.PlayDefinition(
                sound.Name,
                sound.Position,
                priority: 320,
                looping: sound.Simple ? false : null);
            if (!sound.Simple && voice.IsValidHandle)
            {
                _cutsceneVoices[sound.Task] = voice;
            }
            else if (sound.Simple && voice.IsValidHandle)
            {
                _cutsceneOneShotVoices.Add(voice);
            }
        }
    }

    private void StopCutsceneSounds()
    {
        if (_sounds is not null)
        {
            foreach (AudioVoiceHandle voice in _cutsceneVoices.Values)
            {
                if (voice.IsValidHandle)
                {
                    _sounds.Stop(voice);
                }
            }

            foreach (AudioVoiceHandle voice in _cutsceneOneShotVoices)
            {
                if (voice.IsValidHandle)
                {
                    _sounds.Stop(voice);
                }
            }
        }

        _cutsceneVoices.Clear();
        _cutsceneOneShotVoices.Clear();
    }

    /// <summary>The level's authored ambience boxes. Null until a mission is up.</summary>
    private AmbientAreaRegistry? _ambience;

    /// <summary>The bed each sounding ambient area is playing.</summary>
    private readonly Dictionary<PlacedAmbientArea, AudioVoiceHandle> _ambientVoices = [];

    private readonly List<PendingGenericTbaExplosion> _pendingGenericTbaExplosions = [];

    private sealed class PendingGenericTbaExplosion(
        Vector3d position,
        GenericTbaExplosionParameters parameters,
        int remainingTicks)
    {
        public Vector3d Position { get; } = position;
        public GenericTbaExplosionParameters Parameters { get; } = parameters;
        public int RemainingTicks { get; set; } = remainingTicks;
    }

    /// <summary>Makes the player's weapon sound. Null until the device is open.</summary>
    private WeaponSoundPlayer? _weaponSounds;

    /// <summary>One automatic-loop owner per AI human, so simultaneous rifles never share a voice.</summary>
    private readonly Dictionary<int, WeaponSoundPlayer> _soldierWeaponSounds = [];

    private void TickSoldierWeaponSound(int runtimeId, bool fired, WeaponType? weapon, Vector3d position)
    {
        if (_sounds is null)
        {
            return;
        }

        if (!_soldierWeaponSounds.TryGetValue(runtimeId, out WeaponSoundPlayer? player))
        {
            player = new WeaponSoundPlayer(_sounds);
            _soldierWeaponSounds.Add(runtimeId, player);
        }

        player.Tick(fired, weapon, position);
    }

    /// <summary>Dispatches the sounds authored as events in the player's current view-model clip.</summary>
    /// <remarks>
    /// Reload sounds live in <c>animtrigger/animtrigger.qvm</c>, keyed by BOED event ids in the
    /// first-person weapon animations. They are therefore neither firing sounds nor weapon-record
    /// fields, and have to be tested after <see cref="HumanView.Tick"/> advances the rig.
    /// </remarks>
    private void PlayPlayerAnimationSounds()
    {
        if (_sounds is null || _animationTriggers is null || _player?.View is not { } view)
        {
            return;
        }

        foreach (AnimTriggerDefinition trigger in _animationTriggers.Definitions.Values)
        {
            if (view.CrossedEvent(trigger.EventId))
            {
                // The player's view rig is at the listener, so this is deliberately non-positional.
                _sounds.PlayDefinition(trigger.Resource, priority: 180);
            }
        }
    }

    /// <summary>Maintains the retail binocular zoom motor cue while the operator is zooming.</summary>
    /// <remarks>
    /// <c>COMMON/SOUNDS/SOUNDS.RES</c> authors <c>bin_zoom_1</c> as a loop. The retail device only makes
    /// this sound while a zoom key is <b>actively</b> changing the magnification -- not when the view is
    /// opened, and not while it settles to the authored default -- so it is driven off the player
    /// camera's <c>BinocularZoomSteppingThisTick</c>, and stopped the moment the zoom stops or the view
    /// closes. Keying it to the open edge instead played the cue every time the binoculars were raised.
    /// </remarks>
    private void PlayBinocularTransitionSound()
    {
        if (_sounds is null || _player is not { } player)
        {
            return;
        }

        if (!player.IsBinocularsOpen || !player.BinocularZoomSteppingThisTick)
        {
            StopBinocularTransitionSound();
            return;
        }

        if (_binocularZoomVoice.IsValidHandle && _sounds.IsPlaying(_binocularZoomVoice))
        {
            return;
        }

        _binocularZoomVoice = _sounds.PlayDefinition("bin_zoom_1", priority: 180, looping: true);
    }

    /// <summary>Stops the looping binocular cue when player ticking is suspended.</summary>
    /// <remarks>
    /// Death-camera and mission teardown deliberately stop ticking the player. Those paths therefore
    /// cannot rely on <see cref="PlayBinocularTransitionSound"/> observing a closed-view edge on a
    /// later player tick; stop the voice at the lifecycle boundary instead.
    /// </remarks>
    private void StopBinocularTransitionSound()
    {
        if (_binocularZoomVoice.IsValidHandle)
        {
            _sounds?.Stop(_binocularZoomVoice);
            _binocularZoomVoice = AudioVoiceHandle.None;
        }
    }

    /// <summary>Plays the medipack's authored cue when its sustained syringe action starts.</summary>
    /// <remarks>
    /// IGI.EXE's medipack execution state consumes a charge and applies one heal after its damage and
    /// ammo checks, while the state keeps one syringe clip looping. The cue belongs to that action
    /// edge, not to every cooldown-eligible dose, so a fifty-charge pack cannot spam a sound each tick.
    /// </remarks>
    private void PlayMedipackSound()
    {
        if (_player?.Weapons is { MedipackStartedThisTick: true } weapons)
        {
            // A medipack is an injection action, not a gun shot. Some weapon records carry a
            // legacy firearm FireSound field (which produces the shotgun/pistol report); the
            // retail medipack cue is the standalone syringe sound and is always used here.
            _sounds?.PlayDefinition("syringe", priority: 180);
        }
    }

    /// <summary>Plays the retail world-weapon cue after a <c>GunPickup</c> reaches the player.</summary>
    private void PlayGunPickupSound(GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        _sounds?.PlayDefinition("weaponpickup_1", priority: 180);
    }

    /// <summary>Plays the fixed retail cue after an authored generic item reaches the player.</summary>
    /// <remarks>
    /// <c>GenericPickup::cleanup</c> calls <c>0x4E7200("pickup", 0, 0)</c>. The cue is shared by C4,
    /// keycards and every other <c>GenericPickup</c>; the model does not select a per-item sound.
    /// Like retail, it is played at the listener rather than at the item's former world position.
    /// </remarks>
    private void PlayGenericPickupSound(GenericPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);

        // Ammo pickups are authored as GenericPickup/AmmoPickup world objects. Resolve and award them
        // at the same collection edge as the retail pickup cue, so the object, inventory count and sound
        // cannot drift apart. An explicit task payload wins over the catalog fallback.
        if (_player?.Weapons is { } weapons
            && _ammoTypes is { } ammoTypes
            && AmmoPickupCatalog.TryResolve(pickup, ammoTypes, out AmmoType ammo))
        {
            weapons.Ammunition.Add(ammo.AmmoId, AmmoPickupCatalog.Amount(pickup, ammo.AmmoId));
        }

        _sounds?.PlayDefinition("pickup", priority: 180);
    }

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
        _soldierWeaponSounds.Clear();
        MountMenuSounds();
        LoadExternalImpactSound();

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
            ? _loop.Clock.TickFraction(_loop.TimeSource.PreciseMilliseconds)
            : 1f;

        // The EYE, not the feet: an ear 1.8 m below the head is a metre and a half of error against
        // sounds whose whole audible range is fifty. And through FromViewRotation, because what the
        // camera hands back is a world-to-view rotation with its axes in its rows -- the listener wants
        // the engine eye basis, which keeps them in its columns and in a different order.
        _audio.Listener = AudioListenerState.FromViewRotation(
            player.SmoothedEyeMetres(alpha), player.SmoothedLookRotation(alpha));
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
        StopBinocularTransitionSound();
        _weaponSounds?.Reset();
        foreach (WeaponSoundPlayer soldier in _soldierWeaponSounds.Values)
        {
            soldier.Reset();
        }
        _soldierWeaponSounds.Clear();
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
        if (!ReferenceEquals(_resourceFileSystem, _fileSystem))
        {
            _resourceFileSystem.SetAlias(VirtualFileSystem.MissionAlias, loaded.Descriptor.MissionAliasTarget);
        }

        _soundBank.MountDirectory(MissionLoader.CommonSoundDirectory);
        _soundBank.MountDirectory(MissionLoader.MissionSoundDirectory);

        // And the definitions that give those banked sounds their names. Game code says `ai_death_1`,
        // which is a definition; the bank holds `AI_DEATH_01`. Without this every game-side sound
        // resolves to nothing at all.
        //
        // Registered here, per mission, and not once at start-up: a second GameLoop is built for every
        // mission the menu previews and one of them takes the session over, so `_loop.Natives` at
        // start-up is NOT the namespace a promoted mission's scripts resolve against. The weapon,
        // ammo and material tables are registered per mission for exactly this reason; re-registering
        // a name rebinds it rather than failing.
        _soundDefinitions ??= new SoundDefinitionRegistry(_logger);
        _soundDefinitions.Register(_loop.Natives);
        _soundDefinitions.LoadForMission(_resources, _loop.Natives);

        if (_sounds is not null)
        {
            _sounds.Definitions = _soundDefinitions;
        }
    }

    /// <summary>
    /// Plays one of a human's numbered death variants, the way <c>0x4638A0</c> names them.
    /// </summary>
    /// <param name="prefix">The prefix the death state passes.</param>
    /// <param name="variants">How many variants that state draws between.</param>
    /// <param name="positionInUnits">
    /// Where the body is, in engine units, or <see langword="null"/> for the player's own death.
    /// </param>
    /// <remarks>
    /// The player's own death is deliberately <b>not</b> positional. The original attaches the sound to
    /// the human it belongs to, and for the player that is the listener itself — distance zero, so full
    /// gain and no pan. Placing it in the world instead would swing the player's own dying breath
    /// around the stereo image as the death camera turned. Same reasoning as the player's weapon.
    /// </remarks>
    private void PlayDeathSound(string prefix, int variants, Vector3d? positionInUnits)
    {
        if (_sounds is null)
        {
            return;
        }

        bool faithful = AudioCVars.Preset.Value == AudioProfile.Vanilla;
        string name = HumanVariantSound.Choose(prefix, variants, _soundRandom, faithful);

        // Above an ordinary effect but below the score: a firefight must not drop the death that ends it.
        _sounds.PlayDefinition(name, positionInUnits, priority: 300);
    }

    /// <summary>Plays a human's grunt at being hit — the naming half of <c>0x4637C0</c>.</summary>
    /// <param name="prefix">The prefix the damage handler passes.</param>
    /// <param name="variants">How many variants it draws between.</param>
    /// <param name="positionInUnits">Where the body is, or <see langword="null"/> for the player's own.</param>
    /// <remarks>
    /// Unlike the death cry this needs no vanilla gate: <c>0x4637C0</c> formats <c>n + 1</c>, so
    /// <c>ai_hit_</c> builds <c>ai_hit_1</c>…<c>ai_hit_3</c> and matches its definitions exactly.
    /// </remarks>
    private void PlayHitSound(string prefix, int variants, Vector3d? positionInUnits)
    {
        if (_sounds is null)
        {
            return;
        }

        string name = HumanVariantSound.ChooseHit(prefix, variants, _soundRandom);
        _sounds.PlayDefinition(name, positionInUnits, priority: 250);
    }

    /// <summary>Advances the level's authored ambience and keeps its beds sounding.</summary>
    /// <param name="listenerInUnits">Where the ear is, in engine units.</param>
    /// <remarks>
    /// The beds are played <b>flat</b>, not positionally, because that is what the original does with
    /// them: it sets the sound's position to the listener's every tick, which is a wash around the
    /// player rather than a source in the world. Gain is the area's fade, so walking into a machine
    /// room brings its hum up over twenty ticks instead of switching it on at the doorway.
    /// </remarks>
    private void TickAmbience(Vector3d listenerInUnits)
    {
        if (_ambience is not { } ambience || _sounds is null)
        {
            return;
        }

        ambience.Tick(listenerInUnits);

        foreach (PlacedAmbientArea area in ambience.Areas)
        {
            if (area.WantsStop)
            {
                if (_ambientVoices.Remove(area, out AudioVoiceHandle running))
                {
                    _sounds.Stop(running);
                }

                area.IsSounding = false;
                continue;
            }

            if (area.PendingSound.Length > 0)
            {
                AudioVoiceHandle started = _sounds.PlayDefinition(
                    area.PendingSound, gain: (float)area.Fade, priority: 60);

                if (started != AudioVoiceHandle.None)
                {
                    _ambientVoices[area] = started;
                    area.IsSounding = true;
                }
            }
            else if (area.IsSounding && _ambientVoices.TryGetValue(area, out AudioVoiceHandle voice))
            {
                if (_sounds.IsPlaying(voice))
                {
                    _sounds.SetVoiceParameters(voice, VoiceParameters.Default with { Gain = (float)area.Fade });
                }
                else
                {
                    // A one-shot bed that has run out. Letting go of it here is what allows the gap to
                    // rearm and the area to speak again.
                    _ambientVoices.Remove(area);
                    area.IsSounding = false;
                }
            }
        }
    }

    /// <summary>Plays the retail jump cue on the take-off tick.</summary>
    /// <remarks>
    /// Locomotion state 28's entry at <c>0x40BBF7</c> pushes <c>"jump_1"</c> (<c>0x00536DC4</c>)
    /// into <c>0x4DD750</c>. Like the player's fall and death cues it belongs to the listener, so
    /// it is not world-panned.
    /// </remarks>
    private void PlayJumpSound()
    {
        _sounds?.PlayDefinition(PlayerBody.JumpSoundName, priority: 200);
    }

    /// <summary>Plays a footstep where it landed.</summary>
    /// <param name="step">The step, already resolved against the ground it landed on.</param>
    /// <remarks>
    /// Pitched between 0.8 and 1.3, which is the whole of why two steps on the same surface are never
    /// quite the same sound. Low priority: a firefight is more worth a voice than a footfall.
    /// </remarks>
    private void PlayFootstep(in Footstep step)
    {
        if (_sounds is null || step.Sound.Length == 0)
        {
            return;
        }

        _sounds.PlayDefinition(
            step.Sound,
            step.PositionInUnits,
            pitch: FootstepSounds.Pitch(_soundRandom),
            priority: 40);
    }

    /// <summary>The name the remaster's own impact sound is registered and played under.</summary>
    private const string CustomImpactSoundName = "remaster_bullet_impact";

    /// <summary>The most custom impact sounds one tick starts.</summary>
    /// <remarks>
    /// The custom sound deliberately skips retail's global rand(6)+6-tick quiet spell — an automatic
    /// rifle lands a round every couple of ticks, and a limit built for one impact a fifth of a
    /// second swallowed most of a burst. What still needs a bound is the shotgun: thirty pellets
    /// arrive on ONE tick, and thirty simultaneous copies of one sample is a 30× volume spike, not
    /// thirty impacts. Six covers every automatic weapon's densest tick with room to spare.
    /// </remarks>
    private const int MaximumCustomImpactSoundsPerTick = 6;

    /// <summary>Loads the port's own bullet-impact sound, when the asset ships beside the game.</summary>
    /// <remarks>
    /// A mono 16-bit PCM WAV under <c>Assets/sounds</c> — mono deliberately, because only a mono
    /// source is positioned in 3D; a stereo buffer plays flat on the listener. Registered through
    /// <see cref="SoundSystem.RegisterExternal"/> so the retail bank stays exactly what the game
    /// data says. Missing file simply means the material table's own sounds carry on.
    /// </remarks>
    private void LoadExternalImpactSound()
    {
        if (_sounds is null)
        {
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "sounds", "bullet-impact.wav");
        if (!File.Exists(path))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException error)
        {
            _logger.Warning("Audio", $"Impact sound would not read: {error.Message}");
            return;
        }

        if (!WavPcm.TryRead(bytes, out AudioFormat format, out byte[] pcm))
        {
            _logger.Warning("Audio", $"'{path}' is not an uncompressed PCM WAV; the material sounds stand.");
            return;
        }

        _sounds.RegisterExternal(CustomImpactSoundName, format, pcm);
        _logger.Information(
            "Audio",
            $"Impact sound: {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample}-bit, {pcm.Length} bytes.");
    }

    /// <summary>Plays what this tick's arrived ballistic rounds struck.</summary>
    /// <remarks>
    /// The retail rules through the retail data: the struck material's own six sounds, one picked at
    /// random, and the whole set rate-limited globally so a burst into a wall does not stack six
    /// samples — see <see cref="BulletImpactSounds"/>. A human resolves to the <c>Flesh</c> row; a
    /// surface whose row names no sounds falls back to <c>Ground</c>, the same row a step on bare
    /// terrain takes, because a terrain face names no material row of its own.
    /// </remarks>
    private void PlayBallisticImpactSounds()
    {
        if (_sounds is null
            || _player is null
            || _worldMaterials is null
            || !GameplayCVars.BallisticsImpactSounds.Value)
        {
            return;
        }

        int customPlayedThisTick = 0;
        foreach (BallisticImpact impact in _player.Ballistics.Impacts)
        {
            if (!impact.StruckHuman && !impact.StruckWall)
            {
                continue;
            }

            // The port's own impact sound, when its asset ships, takes every material for now --
            // positional at the impact, pitch-jittered so a burst is not one sample five times.
            // EVERY round of an automatic burst plays; only a same-tick pellet storm is capped.
            // Per-material variants can hang off the same seam later.
            if (_sounds.HasExternal(CustomImpactSoundName))
            {
                if (++customPlayedThisTick > MaximumCustomImpactSoundsPerTick)
                {
                    continue;
                }

                _sounds.PlayExternal(
                    CustomImpactSoundName,
                    impact.Position,
                    gain: 0.85f,
                    priority: 45,
                    pitch: FootstepSounds.Pitch(_soundRandom),
                    minimumDistanceInMetres: 2f,
                    maximumDistanceInMetres: 70f);
                continue;
            }

            // The retail limit is GLOBAL -- one timestamp for every impact of every round -- so the
            // first strike of a burst speaks for all of it. It stays on the retail sounds only: it
            // is the original's own restraint, not the added asset's.
            if (_loop.Clock.TickCount < _impactSoundQuietUntil)
            {
                return;
            }

            string sound = string.Empty;
            if (impact.StruckHuman)
            {
                if (_worldMaterials.TryGetByName(BulletImpactSounds.FleshMaterialName, out GameMaterial flesh))
                {
                    sound = BulletImpactSounds.Pick(flesh, _soundRandom);
                }
            }
            else
            {
                if (_worldMaterials.TryGet(impact.Wall.Material, out GameMaterial struck))
                {
                    sound = BulletImpactSounds.Pick(struck, _soundRandom);
                }

                if (sound.Length == 0
                    && _worldMaterials.TryGetByName(FootstepSounds.TerrainMaterialName, out GameMaterial ground))
                {
                    sound = BulletImpactSounds.Pick(ground, _soundRandom);
                }
            }

            if (sound.Length == 0)
            {
                continue;
            }

            _impactSoundQuietUntil = _loop.Clock.TickCount + BulletImpactSounds.QuietTicks(_soundRandom);
            _sounds.PlayDefinition(
                sound,
                impact.Position,
                pitch: FootstepSounds.Pitch(_soundRandom),
                priority: 45);
        }
    }

    /// <summary>Spawns the particle bursts for whatever ballistic rounds landed this tick.</summary>
    /// <remarks>
    /// Beside the impact sounds because it reads the same impacts and resolves the same material; a
    /// human takes the flesh burst, with no surface normal to bias it.
    /// </remarks>
    private void SpawnBallisticImpactParticles()
    {
        if (_impactParticles is null || _player is null)
        {
            return;
        }

        float intensity = RenderCVars.ImpactParticles.Value;
        if (!(intensity > 0f))
        {
            return;
        }

        foreach (BallisticImpact impact in _player.Ballistics.Impacts)
        {
            if (impact.StruckHuman)
            {
                _impactParticles.SpawnBurst(impact.Position, default, ImpactSurfaceKind.Flesh, intensity);
            }
            else if (impact.StruckWall)
            {
                string material = _worldMaterials is { } table
                    && table.TryGet(impact.Wall.Material, out GameMaterial struck)
                    ? struck.Name
                    : string.Empty;
                _impactParticles.SpawnBurst(
                    impact.Wall.Position,
                    impact.Wall.Normal,
                    ImpactParticleRenderer.KindOf(material),
                    intensity);
            }
        }
    }

    /// <summary>Applies and shows whatever grenades went off this tick.</summary>
    /// <remarks>
    /// <para>
    /// The retail detonation, joined up from the reads: the blast damages every human by
    /// <see cref="ExplosionDamage.Dose"/> on <b>zone 13</b> — soldiers through the renderer's
    /// explosion path, the player through its own health — plays <c>explo_02_m</c> where it went
    /// off, and wakes the neighbourhood's AI. A flashbang damages nobody; only the player is
    /// affected (<see cref="Flashbang"/>), measured to the <b>eye</b>: any flash inside 25 m
    /// registers, one inside its own radius blinds outright.
    /// </para>
    /// <para>
    /// The visible fireball is this port's particle burst rather than the retail <c>1009_01_1</c>
    /// fireball model — a recorded stand-in, the numbers around it retail.
    /// </para>
    /// </remarks>
    private void ResolveGrenadeDetonations()
    {
        if (_player is null)
        {
            return;
        }

        foreach (GrenadeDetonation blast in _player.Grenades.Detonations)
        {
            // 122880 units -- the 30 m HUMANAI_DETECTIONEVENT_GUNSHOT_RANGE an unsuppressed shot
            // carries; an explosion is at least as loud as the rifle that could have caused it.
            _sounds?.PlayDefinition("explo_02_m", blast.Position, priority: 90);
            _soldiers?.NotifyGunshot(blast.Position, 122880f);
            _impactParticles?.SpawnExplosion(blast.Position, Math.Max(1f, RenderCVars.ImpactParticles.Value));

            if (blast.Type == GrenadeExplosionType.Flashbang)
            {
                float eyeDistance = (float)(_player.EyeUnits - blast.Position).Length;
                (float visible, float close) = Flashbang.Effect(eyeDistance, blast.RadiusUnits);
                _flashbangStrength = Math.Max(_flashbangStrength, Math.Max(visible, close));
                continue;
            }

            _soldiers?.ApplyExplosion(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);

            // A blast in reach of a speaker takes it off the wall too.
            DamageAlarmSpeakers(blast.Position, blast.RadiusUnits);

            float playerDistance = (float)(_player.Body.Position - blast.Position).Length;
            float dose = ExplosionDamage.Dose(
                playerDistance,
                ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
                blast.RadiusUnits,
                blast.FalloffUnits,
                blast.DamageFactor);
            if (dose > 0f && !_playerHealth.IsDead)
            {
                _playerHealth.Apply(ExplosionDamage.Zone, dose);
                _damageFlash.Hit(Vector3f.Zero, _soundRandom.NextSingle());

                // A blast that zeroes the health kills exactly the way a bullet does -- without
                // this the player stood at zero for ever.
                if (_playerHealth.IsDead)
                {
                    BeginPlayerDeath(blast.Position, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
                }
            }
        }
    }

    /// <summary>Consumes and presents authored GenericTBA explosions.</summary>
    /// <remarks>
    /// GenericTBA uses the same world blast paths as a grenade, but its radius, falloff, damage scale,
    /// delay, sound and particle counts come from the task. The interaction registry emits one edge;
    /// this method owns the optional delayed presentation without re-reading the script state.
    /// </remarks>
    private void ResolveGenericTbaExplosions()
    {
        if (_interactions is { } interactions)
        {
            foreach ((WorldInteraction interaction, GenericTbaExplosionParameters parameters)
                in interactions.ConsumeExplosions())
            {
                int delayTicks = Math.Max(
                    0,
                    (int)MathF.Round(Math.Max(0f, parameters.ExplosionDelaySeconds) * GameClock.DefaultTickRate));
                _pendingGenericTbaExplosions.Add(
                    new PendingGenericTbaExplosion(interaction.Anchor, parameters, delayTicks));
            }
        }

        for (int i = _pendingGenericTbaExplosions.Count - 1; i >= 0; i--)
        {
            PendingGenericTbaExplosion pending = _pendingGenericTbaExplosions[i];
            if (pending.RemainingTicks > 0)
            {
                pending.RemainingTicks--;
                continue;
            }

            PresentGenericTbaExplosion(pending.Position, pending.Parameters);
            _pendingGenericTbaExplosions.RemoveAt(i);
        }
    }

    private void PresentGenericTbaExplosion(
        Vector3d position,
        GenericTbaExplosionParameters parameters)
    {
        string sound = parameters.ExplosionSound.Length > 0 ? parameters.ExplosionSound : "explo_02_m";
        _sounds?.PlayDefinition(sound, position, priority: 90);
        _soldiers?.NotifyGunshot(position, 122880f);
        _impactParticles?.SpawnExplosion(
            position,
            Math.Max(1f, RenderCVars.ImpactParticles.Value),
            parameters.FragmentCount,
            parameters.FireballCount);

        if (parameters.ExplosionExpression.Length > 0)
        {
            _ = new LevelExpressionRuntime(_loop.Variables).Execute(parameters.ExplosionExpression);
        }

        float damageFactor = parameters.DamageScale * parameters.ExplosionDamageScale;
        _soldiers?.ApplyExplosion(
            position,
            parameters.ExplosionRadiusUnits,
            parameters.ExplosionFalloffUnits,
            damageFactor);
        DamageAlarmSpeakers(position, parameters.ExplosionRadiusUnits);

        if (_player is null)
        {
            return;
        }

        float playerDistance = (float)(_player.Body.Position - position).Length;
        float dose = ExplosionDamage.Dose(
            playerDistance,
            ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
            parameters.ExplosionRadiusUnits,
            parameters.ExplosionFalloffUnits,
            damageFactor);
        if (dose > 0f && !_playerHealth.IsDead)
        {
            _playerHealth.Apply(ExplosionDamage.Zone, dose);
            _damageFlash.Hit(Vector3f.Zero, _soundRandom.NextSingle());
            if (_playerHealth.IsDead)
            {
                BeginPlayerDeath(position, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
            }
        }
    }

    /// <summary>Shoots out whatever alarm speaker an impact landed on.</summary>
    /// <param name="impactUnits">Where the round or blast hit, in world units.</param>
    /// <param name="radiusUnits">The destruction's reach — a bullet's touch by default.</param>
    /// <param name="owner">The exact placed task struck by a rich trace, when available.</param>
    /// <remarks>
    /// Retail routes a weapon hit to the exact task. AlarmLight_Explode (<c>0x43A5F0</c>) and the
    /// SCameraLens destruction branch (<c>0x441770</c>) permanently silence it, play its report,
    /// spawn the <c>1009_01_1</c> fireball and swap in the destroyed model. A rich trace preserves
    /// that identity; explosion radii and legacy projectile contacts use the nearest placed head.
    /// </remarks>
    private void DamageAlarmSpeakers(
        Vector3d impactUnits,
        float radiusUnits = AlarmSpeakerRegistry.HitRadiusUnits,
        GameTask? owner = null)
    {
        if (_alarmSpeakers is not { } speakers)
        {
            return;
        }

        AlarmSpeaker? destroyed = owner is not null
            ? speakers.TryDestroy(owner)
            : speakers.TryDestroyAt(impactUnits, radiusUnits);
        if (destroyed is null)
        {
            return;
        }

        // Silenced on the spot, not on the next tick's sweep -- a shot speaker cuts mid-wail.
        if (_sirenVoices.Remove(destroyed, out AudioVoiceHandle siren))
        {
            _sounds?.Stop(siren);
        }

        if (destroyed.DestructionSound.Length > 0)
        {
            _sounds?.PlayDefinition(destroyed.DestructionSound, destroyed.PositionUnits, priority: 60);
        }

        if (_securityCameras?.Destroy(destroyed.Task) == true
            && _securityCameras.TryGetCamera(destroyed.Task, out SecurityCamera? camera)
            && camera is not null)
        {
            if (_cameraBeepVoices.Remove(camera, out var beep))
            {
                _sounds?.Stop(beep.Voice);
            }
            if (_cameraMoveVoices.Remove(camera, out AudioVoiceHandle motor))
            {
                _sounds?.Stop(motor);
            }
        }

        // SCameraLens uses the same 1009_01_1 fireball as the grenade path and plays explo_camera
        // (0x441770). AlarmLight_Explode uses that fireball too, at a slightly smaller 2048-unit size.
        float intensity = string.Equals(destroyed.Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal)
            ? 1f
            : 0.6f;
        _impactParticles?.SpawnExplosion(destroyed.PositionUnits, intensity);
        _logger.Information("Ai", $"{destroyed.Task.Name} was shot out.");
    }

    /// <summary>Keeps every alarm speaker's siren matched to its own expression, positionally.</summary>
    /// <remarks>
    /// <para>
    /// The retail speaker spawns a looping sound child at its own position the tick its expression
    /// turns true and destroys it the tick it turns false — so every siren in the level starts on
    /// the SAME tick and each is heard from the wall it hangs on. This is that behaviour through
    /// the port's voice pool: one held voice per sounding speaker, restarted if the bank's
    /// definition happens not to loop, stopped on the falling edge.
    /// </para>
    /// <para>A destroyed task is removed immediately and its renderer selects the authored wreck model.</para>
    /// </remarks>
    private void TickAlarmSpeakers()
    {
        if (_sounds is null || _alarmSpeakers is null)
        {
            return;
        }

        foreach (AlarmSpeaker speaker in _alarmSpeakers.Speakers)
        {
            bool on = _alarmSpeakers.IsOn(speaker) && speaker.SirenSound.Length > 0;
            bool sounding = _sirenVoices.TryGetValue(speaker, out AudioVoiceHandle voice);
            bool playing = sounding && _sounds.IsPlaying(voice);

            if (on && (!sounding || !playing))
            {
                if (sounding && _reportedSirenFailures.Add(speaker))
                {
                    _logger.Warning(
                        "Audio",
                        $"[alarm] voice {voice.Value} for {speaker.Task.Name} ended while its condition remained true; restarting.");
                }

                AudioVoiceHandle started = _sounds.PlayDefinition(
                    speaker.SirenSound,
                    speaker.PositionUnits,
                    priority: 70);
                _sirenVoices[speaker] = started;

                if (!sounding)
                {
                    _logger.Information(
                        "Audio",
                        $"[alarm] start {speaker.Task.Name}: sound='{speaker.SirenSound}', voice={started.Value}, "
                            + $"playing={_sounds.IsPlaying(started)}, position={speaker.PositionUnits}, "
                            + $"condition='{speaker.AlarmExpression}'.");
                }

                if ((!started.IsValidHandle || !_sounds.IsPlaying(started))
                    && _reportedSirenFailures.Add(speaker))
                {
                    _logger.Warning(
                        "Audio",
                        $"[alarm] '{speaker.SirenSound}' failed to produce a playing voice for {speaker.Task.Name}.");
                }
            }
            else if (!on && sounding)
            {
                _sounds.Stop(voice);
                _sirenVoices.Remove(speaker);
                _reportedSirenFailures.Remove(speaker);
                _logger.Information(
                    "Audio",
                    $"[alarm] stop {speaker.Task.Name}: voice={voice.Value}, condition='{speaker.AlarmExpression}'.");
            }
        }
    }

    /// <summary>Keeps the detection and motor loops owned by each <c>SCameraLens</c> in sync.</summary>
    private void TickSecurityCameraSounds()
    {
        if (_securityCameras is not { } cameras || _sounds is null)
        {
            return;
        }

        foreach (SecurityCamera camera in cameras.Cameras)
        {
            string beep = camera.Beep switch
            {
                SecurityCameraBeep.Suspicious => "camera_beep_s",
                SecurityCameraBeep.Detected => "camera_beep_l",
                _ => string.Empty,
            };

            if (_cameraBeepVoices.TryGetValue(camera, out var sounding)
                && (beep.Length == 0
                    || !string.Equals(sounding.Sound, beep, StringComparison.Ordinal)
                    || !_sounds.IsPlaying(sounding.Voice)))
            {
                _sounds.Stop(sounding.Voice);
                _cameraBeepVoices.Remove(camera);
            }

            if (beep.Length > 0 && !_cameraBeepVoices.ContainsKey(camera))
            {
                AudioVoiceHandle voice = _sounds.PlayDefinition(beep, camera.HeadPositionUnits, priority: 72);
                _cameraBeepVoices[camera] = (beep, voice);
            }

            if (_cameraMoveVoices.TryGetValue(camera, out AudioVoiceHandle moving)
                && (!camera.IsMoving || !_sounds.IsPlaying(moving)))
            {
                _sounds.Stop(moving);
                _cameraMoveVoices.Remove(camera);
            }

            if (camera.IsMoving && !_cameraMoveVoices.ContainsKey(camera))
            {
                _cameraMoveVoices[camera] = _sounds.PlayDefinition(
                    "camera_move",
                    camera.HeadPositionUnits,
                    priority: 55);
            }
        }
    }

    /// <summary>Plays a door's sounds, and owns the one voice that has to be held and stopped.</summary>
    /// <param name="door">The door that made the noise.</param>
    /// <param name="what">Which of its four moments this is.</param>
    private void PlayDoorSound(PlacedDoor door, DoorSoundEvent what)
    {
        if (_sounds is null)
        {
            return;
        }

        if (what == DoorSoundEvent.StoppedMoving)
        {
            if (_doorVoices.Remove(door, out AudioVoiceHandle running))
            {
                _sounds.Stop(running);
            }

            return;
        }

        string name = what switch
        {
            DoorSoundEvent.Opened => door.OpenSound,
            DoorSoundEvent.Closed => door.CloseSound,
            _ => door.MoveSound,
        };

        if (name.Length == 0)
        {
            return;
        }

        AudioVoiceHandle voice = _sounds.PlayDefinition(name, door.Position, priority: 120);

        if (what == DoorSoundEvent.StartedMoving && voice != AudioVoiceHandle.None)
        {
            _doorVoices[door] = voice;
        }
    }

    /// <summary>Plays an elevator's start, travel and stop definitions.</summary>
    /// <remarks>
    /// The travel definition owns its loop flag.  Elevator::Run starts it through the ordinary
    /// definition path (IGI.exe 0x43D590), so forcing a loop at this call site can turn an authored
    /// one-shot into a permanent sound even after its car has stopped.
    /// </remarks>
    private void PlayElevatorSound(PlacedElevator elevator, ElevatorSoundEvent what)
    {
        if (_sounds is null)
        {
            return;
        }

        if (what == ElevatorSoundEvent.Stopped)
        {
            StopElevatorTravelVoice(elevator);

            if (elevator.StopSound.Length > 0)
            {
                _sounds.PlayDefinition(elevator.StopSound, elevator.State.Position, priority: 120);
            }

            return;
        }

        if (what == ElevatorSoundEvent.Started)
        {
            if (elevator.StartSound.Length > 0)
            {
                _sounds.PlayDefinition(elevator.StartSound, elevator.State.Position, priority: 120);
            }

            return;
        }

        if (elevator.MoveSound.Length > 0)
        {
            // A repeat start can happen after a scripted retarget. Retire the old voice first so a
            // replaced dictionary entry can never leave an untracked, permanently looping sample.
            StopElevatorTravelVoice(elevator);
            AudioVoiceHandle voice = _sounds.PlayDefinition(
                elevator.MoveSound,
                elevator.State.Position,
                priority: 120);
            if (voice.IsValidHandle)
            {
                _elevatorVoices[elevator] = voice;
            }
        }
    }

    /// <summary>Stops and forgets the one travel voice currently owned by an elevator.</summary>
    private void StopElevatorTravelVoice(PlacedElevator elevator)
    {
        if (_sounds is not null && _elevatorVoices.Remove(elevator, out AudioVoiceHandle running))
        {
            _sounds.Stop(running);
        }
    }

    /// <summary>Keeps each retail elevator travel loop attached to the moving car.</summary>
    private void UpdateElevatorSoundPositions()
    {
        if (_sounds is null || _elevators is null)
        {
            return;
        }

        foreach (PlacedElevator elevator in _elevators.Elevators)
        {
            if (!_elevatorVoices.TryGetValue(elevator, out AudioVoiceHandle voice))
            {
                continue;
            }

            if (!elevator.State.HasMovementRequest)
            {
                // Retail owns this handle from request acceptance through arrival, including the
                // door-close gate before physical travel (IGI.exe 0x43D58E-0x43D5C8).
                _sounds.Stop(voice);
                _elevatorVoices.Remove(elevator);
                continue;
            }

            if (!_sounds.IsPlaying(voice))
            {
                _elevatorVoices.Remove(elevator);
                continue;
            }

            _sounds.SetDefinitionVoicePosition(
                elevator.MoveSound,
                voice,
                elevator.State.Position,
                priority: 120);
        }
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
