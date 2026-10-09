using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using System.Diagnostics;

namespace OpenIGI.Host;

/// <summary>The audio half of the composition root: opening the device and keeping the buses levelled.</summary>
internal sealed partial class GameHost
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
        WorldInteraction interaction, Vector3d position, GenericTbaExplosionParameters parameters, int remainingTicks)
    {
        public WorldInteraction Interaction { get; } = interaction;
        public Vector3d Position { get; } = position;
        public GenericTbaExplosionParameters Parameters { get; } = parameters;
        public int RemainingTicks { get; set; } = remainingTicks;
    }

    /// <summary>Makes the player's weapon sound. Null until the device is open.</summary>
    private WeaponSoundPlayer? _weaponSounds;

    /// <summary>The cook-tick voice held while a grenade throw charges.</summary>
    private AudioVoiceHandle _grenadeCookVoice = AudioVoiceHandle.None;

    /// <summary>Whether the cook tick has already been started for the current charge.</summary>
    private bool _grenadeCookStarted;

    /// <summary>One automatic-loop owner per AI human, so simultaneous rifles never share a voice.</summary>
    private readonly Dictionary<int, WeaponSoundPlayer> _soldierWeaponSounds = [];

    /// <summary>Holds the grenade cook tick for as long as the throw is charged.</summary>
    /// <remarks>
    /// The retail throw plays <c>grenade_tick_1</c> <b>once</b> as it enters the throw state
    /// (<c>0x40F590</c> calls <c>sub_4E6B00</c> a single time) and stops it on release (<c>0x40F640</c>) —
    /// the audible cook while holding the pin. It is started once per charge and never re-triggered, and
    /// whether it loops is the sound's own to decide (<c>sub_4E6B00</c> does not force it), not this
    /// caller's, non-positional like the rest of the player's own weapon sound.
    /// </remarks>
    private void TickGrenadeCook()
    {
        if (_sounds is null || _player is null || !_player.IsChargingThrow)
        {
            StopGrenadeCook();
            return;
        }

        if (!_grenadeCookStarted)
        {
            _grenadeCookVoice = _sounds.PlayDefinition("grenade_tick_1", priority: 200);
            _grenadeCookStarted = true;
        }
    }

    private void StopGrenadeCook()
    {
        if (_grenadeCookVoice.IsValidHandle)
        {
            _sounds?.Stop(_grenadeCookVoice);
        }

        _grenadeCookVoice = AudioVoiceHandle.None;
        _grenadeCookStarted = false;
    }

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

    /// <summary>The weather, heard. Null until the device is open; silent until a level rains.</summary>
    private RainAmbience? _rainAmbience;

    /// <summary>When the rain mix was last reported to the log, as a stopwatch timestamp.</summary>
    private long _rainDebugTimestamp;

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
    /// A backend that cannot open is not an error. The factory returns a silent device rather than
    /// throwing, so there is nothing here to handle: the game runs either way.
    /// </para>
    /// </remarks>
    private void OpenAudioDevice()
    {
        _audio.Dispose();

        _audio = AudioCVars.Enabled.Value
            ? _audioFactory.Open(AudioDeviceOptions.FromCVars(), _logger)
            : new NullAudioDevice();

        _soundBank = new SoundBank(_fileSystem, _logger);
        _sounds?.Dispose();
        _sounds = new SoundSystem(_audio, _soundBank, _fileSystem, _logger);

        // The remaster's flashlight switch, which the game's banks have no sound for.
        _sounds.RegisterExternal(FlashlightClick.OnSoundName, FlashlightClick.Format, FlashlightClick.OnPcm);
        _sounds.RegisterExternal(FlashlightClick.OffSoundName, FlashlightClick.Format, FlashlightClick.OffPcm);

        // And the lamps' starters, ignitors and ballasts, which the original's never needed: they never change.
        foreach ((string name, byte[] pcm) in LampSounds.All)
        {
            _sounds.RegisterExternal(name, LampSounds.Format, pcm);
        }

        _soldierWeaponSounds.Clear();
        MountMenuSounds();
        LoadRainAmbience();

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
            WhenChanged(variable, ApplyCategoryGains);
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
        if (_titlePlayer is { } title)
        {
            // A title's own player: its eyes as the last tick left them, in the camera's own row order.
            _audio.Listener = AudioListenerState.FromViewRotation(title.EyePosition / TitleUnitsPerMetre, title.ViewOrientation);
            return;
        }

        if (_player is not { } player)
        {
            return;
        }

        // The same blend the presentation uses, so the ear and the eye agree about where the head is.
        float alpha = RenderCVars.SmoothPresentation.Value
            ? _loop.Clock.TickFraction(_loop.Clock.FrameInstantMilliseconds)
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
        StopBinocularTransitionSound();
        StopGrenadeCook();
        _weaponSounds?.Reset();
        foreach (WeaponSoundPlayer soldier in _soldierWeaponSounds.Values)
        {
            soldier.Reset();
        }
        _soldierWeaponSounds.Clear();
        _rainAmbience?.Stop();

        // Before the buffers, and explicitly rather than through the device-wide stop UnloadBuffers
        // does: the score has to be told, not merely silenced. It treats a repeat of the playing track
        // as a no-op so that re-entering the mission state does not stutter the music, and a track it
        // still believes is playing is a track the next Play(game_music) would decline to start.
        _music?.Stop();

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

        // One pitch for the whole step: 0x463920 computes it once and hands the same value to the
        // material voice and to the rain voice, so the two halves of a wet step stay together.
        float pitch = FootstepSounds.Pitch(_soundRandom);

        _sounds.PlayDefinition(
            step.Sound,
            step.PositionInUnits,
            gain: step.VolumeScale,
            pitch: pitch,
            priority: 40);

        // The wet layer is played AS WELL AS the material's own sound, never instead of it. Its gate
        // is 0x416FB0, a single global byte -- the level's weather flag rather than a shelter test --
        // which is what the rain ambience already reads through the RainEffect task.
        if (_rain?.IsWeatherEnabled == true)
        {
            _sounds.PlayDefinition(
                FootstepSounds.RainSoundName(step.CycleSlot),
                step.PositionInUnits,
                gain: step.VolumeScale,
                pitch: pitch,
                priority: 40);
        }
    }

    /// <summary>Plays the sound the ground itself makes when the player lands on it.</summary>
    /// <param name="landing">The definition name and where the feet touched down.</param>
    /// <remarks>
    /// Flat: <c>0x40BE40</c> and <c>0x40BBD0</c> both start this voice and then leave it alone -- no
    /// pitch variation and no volume change, unlike a footstep. Priority sits above a footfall because
    /// a landing is a single deliberate event rather than one of a stream.
    /// </remarks>
    private void PlayLandingSound((string Sound, Vector3d PositionInUnits) landing)
    {
        if (_sounds is null || landing.Sound.Length == 0)
        {
            return;
        }

        _sounds.PlayDefinition(landing.Sound, landing.PositionInUnits, priority: 60);
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

        if (_sounds is null)
        {
            return;
        }

        RainAmbience rain = new(_sounds, _logger);
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
        bool raining = AudioCVars.Rain.Value && _player is not null && (_rain?.IsWeatherEnabled ?? false);
        float intensity = raining
            ? (_storm is { Enabled: true } ? RenderCVars.StormIntensity.Value : 1f)
            : 0f;

        rain.Update(
            _audio.Listener.Position,
            _shelterField,
            _player?.GrenadeWalls,
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

    /// <summary>Plays what this tick's bullet impacts struck.</summary>
    /// <remarks>
    /// Hit-scan and ballistic contacts share the retail material table, exactly as the original's shot
    /// resolution does at <c>0x47BA95</c>: the struck material's own six sounds at row <c>+340</c>, one
    /// picked by <c>rand() % 6</c>, and the whole set rate-limited by ONE global timestamp — see
    /// <see cref="BulletImpactSounds"/>. A human resolves to the <c>Flesh</c> row, which is the only
    /// row naming the <c>bul_flesh_*</c> samples at all.
    /// </remarks>
    private void PlayBulletImpactSounds()
    {
        if (_player is null)
        {
            return;
        }

        foreach (GunImpact impact in _player.Impacts)
        {
            // A knife records no impact (0x46DFB0), so it never reaches the material's sounds.
            if (!impact.HitSomething || impact.Melee)
            {
                continue;
            }

            PlayBulletImpactSound(impact.Position, impact.StruckHuman, impact.Wall.Material);
        }
    }

    /// <summary>Plays the clatter of every dropped gun that landed this tick.</summary>
    /// <remarks>
    /// <c>GunPickup</c>'s tick (<c>0x46CEA0</c>) plays <c>weapondrop_01</c> or <c>weapondrop_02</c>, a coin
    /// toss between them, at every bounce. The realistic drop reports a landing once per contact, so a gun
    /// that tumbles and bounces twice clatters twice.
    /// </remarks>
    private void PlayWeaponDropSounds()
    {
        if (_sounds is null || _gunPickups is not { } pickups)
        {
            return;
        }

        foreach (GunPickupImpact impact in pickups.Impacts)
        {
            string sound = _soundRandom.NextSingle() > 0.5f ? "weapondrop_01" : "weapondrop_02";
            _sounds.PlayDefinition(sound, impact.Position, priority: 50);
        }
    }

    /// <summary>Plays one player or AI bullet contact through the retail sound definitions.</summary>
    private void PlayBulletImpactSound(Vector3d position, bool struckHuman, int surfaceMaterial)
    {
        if (_sounds is null
            || _worldMaterials is null
            || !GameplayCVars.BallisticsImpactSounds.Value
            || _loop.Clock.TickCount < _impactSoundQuietUntil)
        {
            return;
        }

        string sound = BulletImpactSounds.PickForImpact(
            _worldMaterials,
            struckHuman,
            surfaceMaterial,
            _soundRandom);

        // The quiet window opens on the CONTACT, not on the sample. 0x47BABE's jump skips only the
        // playback call: 0x47BAE1 timestamps and 0x47BAFB re-rolls the interval on the merge path, so a
        // slot that names nothing still spends the window. Half the authored rows are silent for at
        // least half their slots -- Air, Water and Snow name a sound in one slot of six, and
        // HumanCollision, MetalLadder and MetalFence in none -- so returning early here would let a
        // burst into a ladder re-arm on every round and machine-gun the next surface it caught.
        _impactSoundQuietUntil = _loop.Clock.TickCount + BulletImpactSounds.QuietTicks(_soundRandom);

        if (sound.Length > 0)
        {
            _sounds.PlayDefinition(
                sound,
                position,
                pitch: FootstepSounds.Pitch(_soundRandom),
                priority: 45);
        }

        // The second half of a retail impact. 0x47C330 runs on the merge path inside the material
        // window, keeps a SEPARATE timestamp and rand(6)+6 interval of its own, and picks one of four
        // riccochet_* samples without reading the material at all -- which is why a burst into a fence
        // is the fence's own ping AND a whizz, arriving on their own two rhythms rather than together.
        PlayRicochetSound(position);
    }

    /// <summary>Plays the whizz a round makes coming off a surface, on its own retail limit.</summary>
    /// <remarks>
    /// Separate from the material's sample in every way that matters: its own global timestamp, its own
    /// <c>rand(6)+6</c> interval, and a fixed table of four it reads without consulting the surface.
    /// A round that glances off calls it directly (<c>0x47BD7B</c>) rather than through the material
    /// window, so a ricochet always gets its turn.
    /// </remarks>
    private void PlayRicochetSound(Vector3d position)
    {
        if (_sounds is null || _loop.Clock.TickCount < _ricochetSoundQuietUntil)
        {
            return;
        }

        _ricochetSoundQuietUntil = _loop.Clock.TickCount + BulletImpactSounds.QuietTicks(_soundRandom);
        _sounds.PlayDefinition(
            BulletImpactSounds.PickRicochet(_soundRandom),
            position,
            pitch: FootstepSounds.Pitch(_soundRandom),
            priority: 45);
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

    /// <summary>Delivers native grenade/explosion AI events before the soldier tick.</summary>
    /// <remarks>
    /// IGI1 broadcasts these from the task update itself: type 17 when a grenade is created, type 18 on
    /// the debounced landing sound, type 19 for a flashbang, and type 21 for an explosion. They are
    /// deliberately separate from gunshot/walk noise. The physics lists stay available until the effect
    /// pass below, so this method only consumes the one-shot throw list.
    /// </remarks>
    private void NotifyGrenadeAiEvents()
    {
        if (_player is null || _soldiers is null)
        {
            return;
        }

        foreach (Vector3d origin in _player.Grenades.Thrown)
        {
            _soldiers.NotifyGrenadeThrown(origin);
        }

        _player.Grenades.ClearThrownEvents();

        foreach (Vector3d origin in _player.Grenades.Bounces)
        {
            _soldiers.NotifyGrenadeLand(origin);
        }

        // IGI1's proximity task emits proxy_arm on the first human entering its activation radius.
        // This is a one-shot rising-edge cue, not the mine's placement/arming sound.
        foreach (Vector3d origin in _player.Grenades.MineTriggers)
        {
            _sounds?.PlayDefinition("proxy_arm", origin, priority: 180);
        }

        _player.Grenades.ClearMineTriggerEvents();

        foreach (GrenadeDetonation blast in _player.Grenades.Detonations)
        {
            if (blast.Type == GrenadeExplosionType.Flashbang)
            {
                _soldiers.NotifyFlashbang(blast.Position, blast.RadiusUnits);
            }
            else
            {
                // The AI hears a grenade over 10 m and a mine over 25 m, however big the blast.
                _soldiers.NotifyExplosion(
                    blast.Position,
                    blast.IsProximityMine
                        ? AiSoldier.LargeExplosionEventRadiusInUnits
                        : AiSoldier.GrenadeExplosionEventRadiusInUnits);
            }
        }

        foreach (MissileDetonation blast in _player.Missiles.Detonations)
        {
            _soldiers.NotifyExplosion(blast.Position, AiSoldier.LargeExplosionEventRadiusInUnits);
        }
        foreach (MissileDetonation blast in _vehicleMissiles.Detonations)
        {
            _soldiers.NotifyExplosion(blast.Position, AiSoldier.LargeExplosionEventRadiusInUnits);
        }

        foreach (M203Detonation blast in _player.M203.Detonations)
        {
            _soldiers.NotifyExplosion(blast.Position, blast.RadiusUnits);
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
    /// <para>The visible fireball is resolved by the retail effect assets loaded by the impact renderer.</para>
    /// </remarks>
    private void ResolveGrenadeDetonations()
    {
        if (_player is null)
        {
            return;
        }

        foreach (GrenadeDetonation blast in _player.Grenades.Detonations)
        {
            if (blast.Type == GrenadeExplosionType.Flashbang)
            {
                // 0x00476AC0's type-1 branch spawns only the flash (0x0047E9A0) and plays explo_flash
                // (0x0047E820) -- no fireball and no explo_02_m. The world residue is a smoke task
                // (smoke2.spr), while the player handler at 0x00413090 writes the distance-based
                // whiteout fields from the player's eye. That handler does not perform a wall trace.
                _sounds?.PlayDefinition("explo_flash", blast.Position, priority: 90);
                SpawnFlashbangSmoke(blast.Position);
                float eyeDistance = (float)(_player.EyeUnits - blast.Position).Length;
                (float visible, float close) = Flashbang.Effect(eyeDistance, blast.RadiusUnits);
                // 0x00413090 writes the player fields only when one of its two distance tests passes.
                // The world smoke belongs to 0x47E9A0 and is deliberately independent, so a distant
                // flash event never turns into a zero-strength (but still scheduled) screen flash.
                if (visible > 0f || close > 0f)
                {
                    _flashbangScreenFlash.Apply(visible, close);
                }
                continue;
            }

            // The frag (type 0): the 1009_01_1 / explo_02_m fireball and the blast's casualties.
            _sounds?.PlayDefinition("explo_02_m", blast.Position, priority: 90);
            SpawnBlast(blast.Position, Math.Max(1f, RenderCVars.ImpactParticles.Value));
            _soldiers?.ApplyExplosion(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
            // AIEVENT_EXPLOSION was already posted by NotifyGrenadeAiEvents before the soldier tick.
            // Do not post it again here: leaving a second record behind makes a guard react twice to
            // one retail blast, with the duplicate consumed on the following tick.

            // Explosion0 dispatches its collision callbacks to every nearby authored glass pane and
            // explosive magic object. Do this before the speaker pass so a barrel-to-barrel chain is
            // resolved in the same simulation tick as the grenade that started it.
            HandleDestructibleBlast(
                source: null,
                originUnits: blast.Position,
                radiusUnits: blast.RadiusUnits,
                falloffUnits: blast.FalloffUnits,
                damageFactor: blast.DamageFactor);

            // A blast in reach of a speaker takes it off the wall too.
            DamageAlarmSpeakers(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);

            ReachPlayerWithBlast(blast.Position, blast.RadiusUnits, blast.FalloffUnits);
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
                FlashPlayerForBlast();

                // A blast that zeroes the health kills exactly the way a bullet does -- without
                // this the player stood at zero for ever.
                if (_playerHealth.IsDead)
                {
                    BeginPlayerDeath(blast.Position, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
                }
            }
        }
    }

    /// <summary>Applies and shows launcher missile impacts produced by the missile task.</summary>
    private void ResolveMissileDetonations()
    {
        if (_player is null)
        {
            return;
        }

        foreach (MissileDetonation blast in _player.Missiles.Detonations)
        {
            ResolveMissileDetonation(blast);
        }
        foreach (MissileDetonation blast in _vehicleMissiles.Detonations)
        {
            ResolveMissileDetonation(blast);
        }
    }

    private void ResolveMissileDetonation(MissileDetonation blast)
    {
        if (_player is null)
        {
            return;
        }

        // The missile's own impact (0x428390, at 0x42854D) sets off an Explosion0 with explo_03_l -- the launcher's
        // rocket and a vehicle's alike, since both are the same Missile task.
        _sounds?.PlayDefinition("explo_03_l", blast.Position, priority: 90);
        SpawnBlast(blast.Position, Math.Max(1f, RenderCVars.ImpactParticles.Value));
        ScorchSurface(blast.Position, blast.Normal, MissileScorchHalfWidthInUnits);
        _soldiers?.ApplyExplosion(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
        // Launcher impacts use Explosion0's full collision envelope, then the speaker pass for alarms.
        // The blast carries the firing weapon's record (0x428390 hands Explosion0 the missile's +0x894),
        // which is what lets a LAW rocket -- and nothing else -- blow a car up.
        HandleDestructibleBlast(
            source: null,
            originUnits: blast.Position,
            radiusUnits: blast.RadiusUnits,
            falloffUnits: blast.FalloffUnits,
            damageFactor: blast.DamageFactor,
            sourceWeaponId: blast.SourceWeaponId);
        DamageAlarmSpeakers(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);

        ReachPlayerWithBlast(blast.Position, blast.RadiusUnits, blast.FalloffUnits);
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
            FlashPlayerForBlast();
            if (_playerHealth.IsDead)
            {
                BeginPlayerDeath(blast.Position, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
            }
        }
    }

    /// <summary>Applies M203 impacts emitted by the native-style projectile task.</summary>
    /// <summary>What any blast does to the player before its damage is counted — the head of <c>0x412990</c>.</summary>
    /// <param name="blastPosition">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The blast radius, in units.</param>
    /// <param name="falloffUnits">The blast's falloff, in units.</param>
    /// <remarks>
    /// <para>
    /// Explosion0 (<c>0x47F200</c>) hands its message to every human with no range test of its own, and the
    /// player's handler acts on distance from the eye before, and whatever, the dose: inside one and a half
    /// times radius plus falloff the body is shoved away from the blast by 758.5 units a tick (into the same
    /// slide the bullet shove uses), and the view shakes by <c>k</c> of a hit's shake — all of it inside three
    /// radii, fading to nothing at nine — for the explosion's 90 ticks.
    /// </para>
    /// <para>
    /// The same handler raises <c>Human+3528</c> within 25 m, which plays the <c>earcandy</c> loop; no shipped
    /// sound set defines it, so the original is silent there too.
    /// </para>
    /// </remarks>
    private void ReachPlayerWithBlast(Vector3d blastPosition, float radiusUnits, float falloffUnits)
    {
        if (_player is null || _playerHealth.IsDead || radiusUnits <= 0f)
        {
            return;
        }

        Vector3d away = _player.EyeUnits - blastPosition;
        double distance = away.Length;
        if (distance < (radiusUnits + falloffUnits) * 1.5 && distance > 0.0)
        {
            _player.Body.AddHitShove(
                (float)(away.X / distance * BlastShoveUnitsPerTick),
                (float)(away.Y / distance * BlastShoveUnitsPerTick));
        }

        double beyond = distance - (radiusUnits * 3.0);
        double share = beyond < 0.0 ? 1.0 : 1.0 - (beyond / (radiusUnits * 6.0));
        if (share > 0.0)
        {
            _player.InstallViewShake(
                (float)(HumanViewShake.HitPositionUnits * share),
                (float)(HumanViewShake.HitAngleRadians * share),
                ExplosionShakeTicks);
        }
    }

    // 0x412990's shove, units a tick into the body's slide -- 758.51849.
    private const float BlastShoveUnitsPerTick = 758.51849f;

    /// <summary>The flash a damaging blast raises — <c>0x412990</c>'s four independent corner draws.</summary>
    private void FlashPlayerForBlast() =>
        _damageFlash.HitEveryCorner(
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle(),
            _soundRandom.NextSingle());

    // The explosion message's duration (+20), which 0x47F200 fills with 90 ticks.
    private const int ExplosionShakeTicks = 90;

    private void ResolveM203Detonations()
    {
        if (_player is null)
        {
            return;
        }

        foreach (M203Detonation blast in _player.M203.Detonations)
        {
            // GunM203's native projectile callback (0x476340) sends the proxy explosion tag, not the
            // hand-grenade fireball. The distinct sound/effect is audible proof that the M203 task
            // reached its own detonation path.
            _sounds?.PlayDefinition("explo_proxy", blast.Position, priority: 90);
            SpawnBlast(blast.Position, Math.Max(1f, RenderCVars.ImpactParticles.Value));
            ScorchSurface(blast.Position, blast.Normal, M203ScorchHalfWidthInUnits);
            _soldiers?.ApplyExplosion(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
            // Match the native explosion callback: visit every visible destructible in the blast
            // envelope, then handle alarms separately.
            HandleDestructibleBlast(
                source: null,
                originUnits: blast.Position,
                radiusUnits: blast.RadiusUnits,
                falloffUnits: blast.FalloffUnits,
                damageFactor: blast.DamageFactor);
            DamageAlarmSpeakers(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);

            ReachPlayerWithBlast(blast.Position, blast.RadiusUnits, blast.FalloffUnits);
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
                FlashPlayerForBlast();
                if (_playerHealth.IsDead)
                {
                    BeginPlayerDeath(blast.Position, ExplosionDamage.Zone, -1, WeaponCategory.Gun);
                }
            }
        }
    }

    /// <summary>Consumes and presents authored GenericTBA explosions once per completed interaction.</summary>
    private void ResolveGenericTbaExplosions()
    {
        if (_interactions is { } interactions)
        {
            foreach ((WorldInteraction interaction, GenericTbaExplosionParameters parameters) in interactions.ConsumeExplosions())
            {
                int delayTicks = Math.Max(0, (int)MathF.Round(
                    Math.Max(0f, parameters.ExplosionDelaySeconds) * GameClock.DefaultTickRate));
                _pendingGenericTbaExplosions.Add(
                    new PendingGenericTbaExplosion(interaction, interaction.Anchor, parameters, delayTicks));
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

            pending.Interaction.MarkExplosionPresented(_worldSolids);
            PresentGenericTbaExplosion(pending.Position, pending.Parameters);
            _pendingGenericTbaExplosions.RemoveAt(i);
        }
    }

    private void PresentGenericTbaExplosion(Vector3d position, GenericTbaExplosionParameters parameters)
    {
        string sound = parameters.ExplosionSound.Length > 0 ? parameters.ExplosionSound : "explo_02_m";
        _sounds?.PlayDefinition(sound, position, priority: 90);
        // GenericTBA is an authored Explosion0 path, not a gunshot proxy. Post the same AI event
        // at presentation time as the other world blasts, including after an authored delay.
        _soldiers?.NotifyExplosion(position, parameters.ExplosionRadiusUnits);
        _impactParticles?.SpawnRetailExplosion(
            position,
            new RetailExplosionShape(
                parameters.ExplosionRadiusUnits,
                parameters.FireballCount,
                parameters.FragmentCount));

        if (parameters.ExplosionExpression.Length > 0)
        {
            _ = new LevelExpressionRuntime(_loop.Variables).Execute(parameters.ExplosionExpression);
        }

        float damageFactor = parameters.DamageScale * parameters.ExplosionDamageScale;
        _soldiers?.ApplyExplosion(position, parameters.ExplosionRadiusUnits, parameters.ExplosionFalloffUnits, damageFactor);
        HandleDestructibleBlast(
            source: null,
            originUnits: position,
            radiusUnits: parameters.ExplosionRadiusUnits,
            falloffUnits: parameters.ExplosionFalloffUnits,
            damageFactor: damageFactor);
        DamageAlarmSpeakers(position, parameters.ExplosionRadiusUnits, parameters.ExplosionFalloffUnits, damageFactor);

        if (_player is null)
        {
            return;
        }

        ReachPlayerWithBlast(position, parameters.ExplosionRadiusUnits, parameters.ExplosionFalloffUnits);
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
            FlashPlayerForBlast();
            if (_playerHealth.IsDead)
            {
                BeginPlayerDeath(position, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
            }
        }
    }

    /// <summary>Hands one round's contact to the exact object it struck, the way retail does.</summary>
    /// <param name="impactUnits">Where the round landed, in world units.</param>
    /// <param name="owner">The exact placed task struck by a rich trace, when available.</param>
    /// <param name="placement">The exact rendered placement struck, including an attached magic object.</param>
    /// <param name="destructibleDamage">The firing weapon row's native damage factor.</param>
    /// <remarks>
    /// The round's trace (<c>0x47B750</c>) sends its damage message to the struck task's own type
    /// handler and searches nothing around the contact. A round that named no object -- one that hit a
    /// guard or the terrain -- therefore breaks no pane, sets off no barrel and silences no speaker,
    /// however close one stands. The placement is essential for windows and barrels: their Owner is the
    /// enclosing Building task, while retail destroys only the exact attached magic object touched.
    /// </remarks>
    private void ApplyRoundContact(
        Vector3d impactUnits,
        GameTask? owner,
        WorldPlacement? placement,
        float destructibleDamage = 1f)
    {
        if (_destructibles?.TryDestroy(owner, placement, destructibleDamage) is { } destructible)
        {
            HandleDestructibleImpact(destructible, impactUnits);
        }

        if (_alarmSpeakers?.TryDestroy(owner, destructibleDamage) is { } speaker)
        {
            ShootOutSpeaker(speaker);
        }
    }

    /// <summary>Hands a blast to the alarm emitters: cameras add its dose, a siren or lamp inside breaks.</summary>
    /// <param name="impactUnits">Where the blast went off, in world units.</param>
    /// <param name="radiusUnits">The blast's radius.</param>
    /// <param name="falloffUnits">How far past the radius its damage takes to reach zero.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    private void DamageAlarmSpeakers(Vector3d impactUnits, float radiusUnits, float falloffUnits, float damageFactor)
    {
        if (_alarmSpeakers is null)
        {
            return;
        }

        foreach (AlarmSpeaker speaker in _alarmSpeakers.ApplyExplosion(impactUnits, radiusUnits, falloffUnits, damageFactor))
        {
            ShootOutSpeaker(speaker);
        }
    }

    /// <summary>Silences a destroyed alarm speaker or camera and plays its destruction.</summary>
    /// <remarks>
    /// AlarmLight_Explode (<c>0x43A5F0</c>) and the SCameraLens destruction branch (<c>0x441770</c>)
    /// permanently silence it, play its report, spawn the <c>1009_01_1</c> fireball and swap in the
    /// destroyed model.
    /// </remarks>
    private void ShootOutSpeaker(AlarmSpeaker destroyed)
    {
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

        // The SCameraLens branch (0x441770) and AlarmLight_Explode (0x43A5F0) each start their own
        // Explosion0 with fixed arguments: the lens a 2048 range, three FireBalls and seven fragments,
        // the light a 4096 range, two FireBalls and five.
        _impactParticles?.SpawnRetailExplosion(
            destroyed.PositionUnits,
            string.Equals(destroyed.Task.Type.Name, WorldScene.SecurityCameraType, StringComparison.Ordinal)
                ? RetailExplosionShape.CameraLens
                : RetailExplosionShape.AlarmLight);
        _logger.Information("Ai", $"{destroyed.Task.Name} was shot out.");
    }

    /// <summary>Runs the native explosion-hit callbacks for every authored object in a blast.</summary>
    /// <remarks>
    /// Nothing is traced to. Cars (<c>0x42E160</c>), ExplodeObjects (<c>0x481780</c>) and glass
    /// (<c>0x4497F0</c>) all join Explosion0's listener list (<c>0x4C1790</c> on <c>dword_57BAA8</c>), and
    /// the fireball messages every entry on it (<c>0x480003</c>). Each handler weighs the blast by distance
    /// alone (<c>0x416C90</c>), so a wall -- or the ground over Mission 7's buried Generator_99 -- stops
    /// nothing.
    /// </remarks>
    private void HandleDestructibleBlast(
        DestructibleObject? source,
        Vector3d originUnits,
        float radiusUnits,
        float falloffUnits,
        float damageFactor = 1f,
        int sourceWeaponId = 0)
    {
        if (_destructibles is not { } registry)
        {
            return;
        }

        foreach (DestructibleObject target in registry.ApplyExplosionHits(
            source,
            originUnits,
            radiusUnits,
            falloffUnits,
            damageFactor,
            target => ConditionalContainerGate.IsTaskActive(target.Owner),
            sourceWeaponId))
        {
            // Passing the blast origin is deliberate. GlassShatter uses the incoming contact to pick
            // nearby collision faces, just as Glass_ExplosionHitHandler hands the native shatter task
            // the fireball's contact point rather than the pane's origin.
            HandleDestructibleImpact(target, originUnits);
        }
    }

    /// <summary>Applies the retail presentation and blast side effects of one destroyed world object.</summary>
    private void HandleDestructibleImpact(DestructibleObject item, Vector3d? impactUnits = null)
    {
        if (item.Kind == DestructibleObjectKind.Glass)
        {
            // magicobj.qvm registers three retail glass-crush variants. Retail chooses one randomly
            // at shatter time and creates GlassShatter shards from the pane collision faces.
            int variant = (int)_soundRandom.NextBounded(3u) + 1;
            _sounds?.PlayDefinition($"glass_crush_{variant}", item.PositionUnits, priority: 55);
            if (item.Placement is { } placement)
            {
                _impactParticles?.SpawnGlassShatter(placement, impactUnits ?? item.PositionUnits);
            }

            // The holes in it were a child of the pane (0x47D438), so they go with it -- which is why a
            // round that breaks the glass leaves no hole hanging in the empty frame.
            if (BulletMarks.SurfaceOf(item.Owner, item.Placement) is { } shattered)
            {
                _player?.Marks.Forget(shattered);
            }
            // GlassShatter_Sound is a world noise. The existing retail ground-impact route is the
            // matching AI event used for non-human world contacts; it also keeps a pane behind a wall
            // from becoming a global alert.
            _soldiers?.NotifyGroundImpact(
                impactUnits ?? item.PositionUnits,
                _player?.Body.Position ?? item.PositionUnits);
            _logger.Information("World", $"{item.TypeName} at {item.PositionUnits} shattered.");
            return;
        }

        if (_destructibles is not { } registry || !registry.ScheduleExplosion(item))
        {
            return;
        }

        HandleDestructibleExplosion(item);
    }

    /// <summary>Takes every mark off a car that has just left the world.</summary>
    /// <remarks>
    /// A scorch or bullet hole is a child of the object it was stuck to (<c>0x4811C9</c> sends the decal to
    /// the struck object), and a car that has blown up deletes its children (<c>0x401780</c>) as it leaves
    /// the world cube — so the LAW's own burn mark goes with it instead of hanging where the body was.
    /// </remarks>
    private void ForgetVehicleMarks(DestructibleObjectRegistry destructibles, DestructibleObject car)
    {
        if (_player is null)
        {
            return;
        }

        HashSet<object> surfaces = new(ReferenceEqualityComparer.Instance) { car.Owner };
        foreach (WorldPlacement placement in destructibles.PlacementsOf(car))
        {
            surfaces.Add(placement);
            surfaces.Add(placement.Owner);
        }

        foreach (object surface in surfaces)
        {
            _player.Marks.Forget(surface);
        }

        _logger.Information("World", $"{car.Owner.Name} exploded and left the world.");
    }

    /// <summary>Sets off one blast of a dying car (<c>0x42E5A0</c>) once its fuse has run.</summary>
    /// <remarks>
    /// An ordinary <c>Explosion0</c>: <c>explo_03_l</c>, the 25 m explosion event, and the same message to
    /// every human, pane, barrel and speaker any other blast sends. It hands over no source record, so it
    /// sets no other car off.
    /// </remarks>
    private void PresentVehicleBlast(VehicleBlast blast)
    {
        Vector3d position = blast.PositionUnits;
        _sounds?.PlayDefinition(VehicleBlast.Sound, position, priority: 90);
        _impactParticles?.SpawnRetailExplosion(
            position,
            new RetailExplosionShape(blast.FullRangeUnits, blast.Fireballs, blast.Fragments));
        _soldiers?.NotifyExplosion(position, AiSoldier.LargeExplosionEventRadiusInUnits);
        _soldiers?.ApplyExplosion(position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
        HandleDestructibleBlast(blast.Source, position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);
        DamageAlarmSpeakers(position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor);

        if (_player is not null && !_playerHealth.IsDead)
        {
            ReachPlayerWithBlast(position, blast.RadiusUnits, blast.FalloffUnits);
            float dose = ExplosionDamage.Dose(
                (float)(_player.Body.Position - position).Length,
                ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
                blast.RadiusUnits,
                blast.FalloffUnits,
                blast.DamageFactor);
            if (dose > 0f)
            {
                _playerHealth.Apply(ExplosionDamage.Zone, dose);
                FlashPlayerForBlast();
                if (_playerHealth.IsDead)
                {
                    BeginPlayerDeath(position, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
                }
            }
        }

        _logger.Information("World", $"{blast.Source.Owner.Name} blast at {position}: radius={blast.RadiusUnits}.");
    }

    /// <summary>Applies the blast side effects after an authored explosive object's delay expires.</summary>
    private void HandleDestructibleExplosion(DestructibleObject item)
    {

        float radius = item.ExplosionRadiusUnits > 0f
            ? item.ExplosionRadiusUnits
            : ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);
        float falloff = Math.Max(0f, item.ExplosionFalloffUnits);
        float damageFactor = Math.Max(0f, item.ExplosionDamageFactor);

        if (item.ExplosionSound.Length > 0)
        {
            _sounds?.PlayDefinition(item.ExplosionSound, item.PositionUnits, priority: 90);
        }

        // ExplodeMagicObj (0x43C0B0) and ExplodeObject (0x481A60) hand Explosion0 their authored full
        // range, fragment count and FireBall count; the look follows from those alone.
        _impactParticles?.SpawnRetailExplosion(
            item.PositionUnits,
            new RetailExplosionShape(radius, (int)item.ExplosionFireballs, item.ExplosionFragments));
        _soldiers?.NotifyExplosion(item.PositionUnits, AiSoldier.LargeExplosionEventRadiusInUnits);

        // Explosion0 (0x47F200) hands its message -- the damage, the shove and the shake -- to every human
        // only when its damage is not 0. A wooden window authors 0: it splinters, and nobody is hurt.
        if (damageFactor == 0f)
        {
            _logger.Information("World", $"{item.TypeName} at {item.PositionUnits} broke without a blast.");
            return;
        }

        HandleDestructibleBlast(item, item.PositionUnits, radius, falloff, damageFactor);
        _soldiers?.ApplyExplosion(item.PositionUnits, radius, falloff, damageFactor);

        if (_player is not null && !_playerHealth.IsDead)
        {
            float distance = (float)(_player.Body.Position - item.PositionUnits).Length;
            ReachPlayerWithBlast(item.PositionUnits, radius, falloff);
            float dose = ExplosionDamage.Dose(
                distance,
                ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
                radius,
                falloff,
                damageFactor);
            if (dose > 0f)
            {
                _playerHealth.Apply(ExplosionDamage.Zone, dose);
                FlashPlayerForBlast();
                if (_playerHealth.IsDead)
                {
                    BeginPlayerDeath(item.PositionUnits, ExplosionDamage.Zone, -1, WeaponCategory.Grenade);
                }
            }
        }

        _logger.Information(
            "World",
            $"{item.TypeName} at {item.PositionUnits} exploded: radius={radius}, falloff={falloff}, damage={damageFactor}.");
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
                        "Audio.Alarm",
                        $"voice {voice.Value} for {speaker.Task.Name} ended while its condition remained true; restarting.");
                }

                AudioVoiceHandle started = _sounds.PlayDefinition(
                    speaker.SirenSound,
                    speaker.PositionUnits,
                    priority: 70);
                _sirenVoices[speaker] = started;

                if (!sounding)
                {
                    _logger.Information(
                        "Audio.Alarm",
                        $"start {speaker.Task.Name}: sound='{speaker.SirenSound}', voice={started.Value}, "
                            + $"playing={_sounds.IsPlaying(started)}, position={speaker.PositionUnits}, "
                            + $"condition='{speaker.AlarmExpression}'.");
                }

                if ((!started.IsValidHandle || !_sounds.IsPlaying(started))
                    && _reportedSirenFailures.Add(speaker))
                {
                    _logger.Warning(
                        "Audio.Alarm",
                        $"'{speaker.SirenSound}' failed to produce a playing voice for {speaker.Task.Name}.");
                }
            }
            else if (!on && sounding)
            {
                _sounds.Stop(voice);
                _sirenVoices.Remove(speaker);
                _reportedSirenFailures.Remove(speaker);
                _logger.Information(
                    "Audio.Alarm",
                    $"stop {speaker.Task.Name}: voice={voice.Value}, condition='{speaker.AlarmExpression}'.");
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
                SecurityCameraBeep.BodyDetected => "camera_beep_al",
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

        _soundDefinitions = null;
        if (_sounds is not null)
        {
            _sounds.Definitions = null;
        }

        // A switch always lands in the front end: the played mission was released and the incoming
        // title has not been asked for one, so the menu theme is what should be sounding.
        _music?.Play(MusicPlayer.MenuMusic);
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
