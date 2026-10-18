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
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Host.Services;

namespace OpenIGI.Host;

/// <summary>The audio half of the composition root: opening the device and keeping the buses levelled.</summary>
public sealed partial class GameHost
{
    /// <summary>The menu system's own sound archive, which the original mounts apart from any mission.</summary>
    private const string MenuSoundArchive = "LOCAL:menusystem/sound/sounds.res";

    // The device, the bank, the sounds by name, their definitions and the score.
    private readonly AudioOutput _audioOut;

    /// <summary>The travelling voice of each door currently in motion.</summary>
    private readonly Dictionary<PlacedDoor, AudioVoiceHandle> _doorVoices = [];
    private readonly Dictionary<PlacedElevator, AudioVoiceHandle> _elevatorVoices =
        new(ReferenceEqualityComparer.Instance);

    // The voices the level's cutscenes start, which a skip silences.
    private readonly CutsceneSounds _cutsceneSounds;
    private AudioVoiceHandle _binocularZoomVoice;

    /// <summary>The bed each sounding ambient area is playing.</summary>
    private readonly Dictionary<PlacedAmbientArea, AudioVoiceHandle> _ambientVoices = [];

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
        if (_audioOut.Sounds is null || HostedPlayer is null || !HostedPlayer.IsChargingThrow)
        {
            StopGrenadeCook();
            return;
        }

        if (!_grenadeCookStarted)
        {
            _grenadeCookVoice = _audioOut.Sounds.PlayDefinition("grenade_tick_1", priority: 200);
            _grenadeCookStarted = true;
        }
    }

    private void StopGrenadeCook()
    {
        if (_grenadeCookVoice.IsValidHandle)
        {
            _audioOut.Sounds?.Stop(_grenadeCookVoice);
        }

        _grenadeCookVoice = AudioVoiceHandle.None;
        _grenadeCookStarted = false;
    }

    private void TickSoldierWeaponSound(int runtimeId, bool fired, WeaponType? weapon, Vector3d position)
    {
        if (_audioOut.Sounds is null)
        {
            return;
        }

        if (!_soldierWeaponSounds.TryGetValue(runtimeId, out WeaponSoundPlayer? player))
        {
            player = new WeaponSoundPlayer(_audioOut.Sounds);
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
        if (_audioOut.Sounds is null || _animationTriggers is null || HostedPlayer?.View is not { } view)
        {
            return;
        }

        foreach (AnimTriggerDefinition trigger in _animationTriggers.Definitions.Values)
        {
            if (view.CrossedEvent(trigger.EventId))
            {
                // The player's view rig is at the listener, so this is deliberately non-positional.
                _audioOut.Sounds.PlayDefinition(trigger.Resource, priority: 180);
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
        if (_audioOut.Sounds is null || HostedPlayer is not { } player)
        {
            return;
        }

        if (!player.IsBinocularsOpen || !player.BinocularZoomSteppingThisTick)
        {
            StopBinocularTransitionSound();
            return;
        }

        if (_binocularZoomVoice.IsValidHandle && _audioOut.Sounds.IsPlaying(_binocularZoomVoice))
        {
            return;
        }

        _binocularZoomVoice = _audioOut.Sounds.PlayDefinition("bin_zoom_1", priority: 180, looping: true);
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
            _audioOut.Sounds?.Stop(_binocularZoomVoice);
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
        if (HostedPlayer?.Weapons is { MedipackStartedThisTick: true } weapons)
        {
            // A medipack is an injection action, not a gun shot. Some weapon records carry a
            // legacy firearm FireSound field (which produces the shotgun/pistol report); the
            // retail medipack cue is the standalone syringe sound and is always used here.
            _audioOut.Sounds?.PlayDefinition("syringe", priority: 180);
        }
    }

    /// <summary>Plays the retail world-weapon cue after a <c>GunPickup</c> reaches the player.</summary>
    private void PlayGunPickupSound(GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        _audioOut.Sounds?.PlayDefinition("weaponpickup_1", priority: 180);
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
        if (HostedPlayer?.Weapons is { } weapons
            && _mission.AmmoTypes is { } ammoTypes
            && AmmoPickupCatalog.TryResolve(pickup, ammoTypes, out AmmoType ammo))
        {
            weapons.Ammunition.Add(ammo.AmmoId, AmmoPickupCatalog.Amount(pickup, ammo.AmmoId));
        }

        _audioOut.Sounds?.PlayDefinition("pickup", priority: 180);
    }

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
        _soldierWeaponSounds.Clear();
        LoadRainAmbience();
        _weaponSounds = new WeaponSoundPlayer(_audioOut.Sounds!);

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

        // The same blend the presentation uses, so the ear and the eye agree about where the head is.
        float alpha = RenderCVars.SmoothPresentation.Value
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
        StopBinocularTransitionSound();
        StopGrenadeCook();
        _weaponSounds?.Reset();
        foreach (WeaponSoundPlayer soldier in _soldierWeaponSounds.Values)
        {
            soldier.Reset();
        }
        _soldierWeaponSounds.Clear();
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
        if (_audioOut.Sounds is null)
        {
            return;
        }

        bool faithful = AudioCVars.Preset.Value == AudioProfile.Vanilla;
        string name = HumanVariantSound.Choose(prefix, variants, _mission.SoundRandom, faithful);

        // Above an ordinary effect but below the score: a firefight must not drop the death that ends it.
        _audioOut.Sounds.PlayDefinition(name, positionInUnits, priority: 300);
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
        if (_audioOut.Sounds is null)
        {
            return;
        }

        string name = HumanVariantSound.ChooseHit(prefix, variants, _mission.SoundRandom);
        _audioOut.Sounds.PlayDefinition(name, positionInUnits, priority: 250);
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
        if (_mission.Ambience is not { } ambience || _audioOut.Sounds is null)
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
                    _audioOut.Sounds.Stop(running);
                }

                area.IsSounding = false;
                continue;
            }

            if (area.PendingSound.Length > 0)
            {
                AudioVoiceHandle started = _audioOut.Sounds.PlayDefinition(
                    area.PendingSound, gain: (float)area.Fade, priority: 60);

                if (started != AudioVoiceHandle.None)
                {
                    _ambientVoices[area] = started;
                    area.IsSounding = true;
                }
            }
            else if (area.IsSounding && _ambientVoices.TryGetValue(area, out AudioVoiceHandle voice))
            {
                if (_audioOut.Sounds.IsPlaying(voice))
                {
                    _audioOut.Sounds.SetVoiceParameters(voice, VoiceParameters.Default with { Gain = (float)area.Fade });
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
        _audioOut.Sounds?.PlayDefinition(PlayerBody.JumpSoundName, priority: 200);
    }

    /// <summary>Plays a footstep where it landed.</summary>
    /// <param name="step">The step, already resolved against the ground it landed on.</param>
    /// <remarks>
    /// Pitched between 0.8 and 1.3, which is the whole of why two steps on the same surface are never
    /// quite the same sound. Low priority: a firefight is more worth a voice than a footfall.
    /// </remarks>
    private void PlayFootstep(in Footstep step)
    {
        if (_audioOut.Sounds is null || step.Sound.Length == 0)
        {
            return;
        }

        // One pitch for the whole step: 0x463920 computes it once and hands the same value to the
        // material voice and to the rain voice, so the two halves of a wet step stay together.
        float pitch = FootstepSounds.Pitch(_mission.SoundRandom);

        _audioOut.Sounds.PlayDefinition(
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
            _audioOut.Sounds.PlayDefinition(
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
        if (_audioOut.Sounds is null || landing.Sound.Length == 0)
        {
            return;
        }

        _audioOut.Sounds.PlayDefinition(landing.Sound, landing.PositionInUnits, priority: 60);
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
            ? (_storm is { Enabled: true } ? RenderCVars.StormIntensity.Value : 1f)
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
        if (HostedPlayer is null)
        {
            return;
        }

        foreach (GunImpact impact in _mission.Player!.Impacts)
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
        if (_audioOut.Sounds is null || _mission.GunPickups is not { } pickups)
        {
            return;
        }

        foreach (GunPickupImpact impact in pickups.Impacts)
        {
            string sound = _mission.SoundRandom.NextSingle() > 0.5f ? "weapondrop_01" : "weapondrop_02";
            _audioOut.Sounds.PlayDefinition(sound, impact.Position, priority: 50);
        }
    }

    /// <summary>Plays one player or AI bullet contact through the retail sound definitions.</summary>
    private void PlayBulletImpactSound(Vector3d position, bool struckHuman, int surfaceMaterial)
    {
        if (_audioOut.Sounds is null
            || _mission.Materials is null
            || !GameplayCVars.BallisticsImpactSounds.Value
            || _loop.Clock.TickCount < _impactSoundQuietUntil)
        {
            return;
        }

        string sound = BulletImpactSounds.PickForImpact(
            _mission.Materials,
            struckHuman,
            surfaceMaterial,
            _mission.SoundRandom);

        // The quiet window opens on the CONTACT, not on the sample. 0x47BABE's jump skips only the
        // playback call: 0x47BAE1 timestamps and 0x47BAFB re-rolls the interval on the merge path, so a
        // slot that names nothing still spends the window. Half the authored rows are silent for at
        // least half their slots -- Air, Water and Snow name a sound in one slot of six, and
        // HumanCollision, MetalLadder and MetalFence in none -- so returning early here would let a
        // burst into a ladder re-arm on every round and machine-gun the next surface it caught.
        _impactSoundQuietUntil = _loop.Clock.TickCount + BulletImpactSounds.QuietTicks(_mission.SoundRandom);

        if (sound.Length > 0)
        {
            _audioOut.Sounds.PlayDefinition(
                sound,
                position,
                pitch: FootstepSounds.Pitch(_mission.SoundRandom),
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
        if (_audioOut.Sounds is null || _loop.Clock.TickCount < _ricochetSoundQuietUntil)
        {
            return;
        }

        _ricochetSoundQuietUntil = _loop.Clock.TickCount + BulletImpactSounds.QuietTicks(_mission.SoundRandom);
        _audioOut.Sounds.PlayDefinition(
            BulletImpactSounds.PickRicochet(_mission.SoundRandom),
            position,
            pitch: FootstepSounds.Pitch(_mission.SoundRandom),
            priority: 45);
    }

    /// <summary>Spawns the particle bursts for whatever ballistic rounds landed this tick.</summary>
    /// <remarks>
    /// Beside the impact sounds because it reads the same impacts and resolves the same material; a
    /// human takes the flesh burst, with no surface normal to bias it.
    /// </remarks>
    private void SpawnBallisticImpactParticles()
    {
        if (_effects.Particles is null || HostedPlayer is null)
        {
            return;
        }

        float intensity = RenderCVars.ImpactParticles.Value;
        if (!(intensity > 0f))
        {
            return;
        }

        foreach (BallisticImpact impact in HostedPlayer.Ballistics.Impacts)
        {
            if (impact.StruckHuman)
            {
                _effects.Particles.SpawnBurst(impact.Position, default, ImpactSurfaceKind.Flesh, intensity);
            }
            else if (impact.StruckWall)
            {
                string material = _mission.Materials is { } table
                    && table.TryGet(impact.Wall.Material, out GameMaterial struck)
                    ? struck.Name
                    : string.Empty;
                _effects.Particles.SpawnBurst(
                    impact.Wall.Position,
                    impact.Wall.Normal,
                    ImpactParticleRenderer.KindOf(material),
                    intensity);
            }
        }
    }

    private void ApplyRoundContact(Vector3d impactUnits, GameTask? owner, WorldPlacement? placement, float destructibleDamage = 1f) =>
        _igi1.Blasts.ApplyRoundContact(impactUnits, owner, placement, destructibleDamage);

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
        if (_audioOut.Sounds is null || _mission.AlarmSpeakers is null)
        {
            return;
        }

        foreach (AlarmSpeaker speaker in _mission.AlarmSpeakers.Speakers)
        {
            bool on = _mission.AlarmSpeakers.IsOn(speaker) && speaker.SirenSound.Length > 0;
            bool sounding = _sirenVoices.TryGetValue(speaker, out AudioVoiceHandle voice);
            bool playing = sounding && _audioOut.Sounds.IsPlaying(voice);

            if (on && (!sounding || !playing))
            {
                if (sounding && _reportedSirenFailures.Add(speaker))
                {
                    _logger.Warning(
                        "Audio.Alarm",
                        $"voice {voice.Value} for {speaker.Task.Name} ended while its condition remained true; restarting.");
                }

                AudioVoiceHandle started = _audioOut.Sounds.PlayDefinition(
                    speaker.SirenSound,
                    speaker.PositionUnits,
                    priority: 70);
                _sirenVoices[speaker] = started;

                if (!sounding)
                {
                    _logger.Information(
                        "Audio.Alarm",
                        $"start {speaker.Task.Name}: sound='{speaker.SirenSound}', voice={started.Value}, "
                            + $"playing={_audioOut.Sounds.IsPlaying(started)}, position={speaker.PositionUnits}, "
                            + $"condition='{speaker.AlarmExpression}'.");
                }

                if ((!started.IsValidHandle || !_audioOut.Sounds.IsPlaying(started))
                    && _reportedSirenFailures.Add(speaker))
                {
                    _logger.Warning(
                        "Audio.Alarm",
                        $"'{speaker.SirenSound}' failed to produce a playing voice for {speaker.Task.Name}.");
                }
            }
            else if (!on && sounding)
            {
                _audioOut.Sounds.Stop(voice);
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
        if (_mission.SecurityCameras is not { } cameras || _audioOut.Sounds is null)
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
                    || !_audioOut.Sounds.IsPlaying(sounding.Voice)))
            {
                _audioOut.Sounds.Stop(sounding.Voice);
                _cameraBeepVoices.Remove(camera);
            }

            if (beep.Length > 0 && !_cameraBeepVoices.ContainsKey(camera))
            {
                AudioVoiceHandle voice = _audioOut.Sounds.PlayDefinition(beep, camera.HeadPositionUnits, priority: 72);
                _cameraBeepVoices[camera] = (beep, voice);
            }

            if (_cameraMoveVoices.TryGetValue(camera, out AudioVoiceHandle moving)
                && (!camera.IsMoving || !_audioOut.Sounds.IsPlaying(moving)))
            {
                _audioOut.Sounds.Stop(moving);
                _cameraMoveVoices.Remove(camera);
            }

            if (camera.IsMoving && !_cameraMoveVoices.ContainsKey(camera))
            {
                _cameraMoveVoices[camera] = _audioOut.Sounds.PlayDefinition(
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
        if (_audioOut.Sounds is null)
        {
            return;
        }

        if (what == DoorSoundEvent.StoppedMoving)
        {
            if (_doorVoices.Remove(door, out AudioVoiceHandle running))
            {
                _audioOut.Sounds.Stop(running);
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

        AudioVoiceHandle voice = _audioOut.Sounds.PlayDefinition(name, door.Position, priority: 120);

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
        if (_audioOut.Sounds is null)
        {
            return;
        }

        if (what == ElevatorSoundEvent.Stopped)
        {
            StopElevatorTravelVoice(elevator);

            if (elevator.StopSound.Length > 0)
            {
                _audioOut.Sounds.PlayDefinition(elevator.StopSound, elevator.State.Position, priority: 120);
            }

            return;
        }

        if (what == ElevatorSoundEvent.Started)
        {
            if (elevator.StartSound.Length > 0)
            {
                _audioOut.Sounds.PlayDefinition(elevator.StartSound, elevator.State.Position, priority: 120);
            }

            return;
        }

        if (elevator.MoveSound.Length > 0)
        {
            // A repeat start can happen after a scripted retarget. Retire the old voice first so a
            // replaced dictionary entry can never leave an untracked, permanently looping sample.
            StopElevatorTravelVoice(elevator);
            AudioVoiceHandle voice = _audioOut.Sounds.PlayDefinition(
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
        if (_audioOut.Sounds is not null && _elevatorVoices.Remove(elevator, out AudioVoiceHandle running))
        {
            _audioOut.Sounds.Stop(running);
        }
    }

    /// <summary>Keeps each retail elevator travel loop attached to the moving car.</summary>
    private void UpdateElevatorSoundPositions()
    {
        if (_audioOut.Sounds is null || _mission.Elevators is null)
        {
            return;
        }

        foreach (PlacedElevator elevator in _mission.Elevators.Elevators)
        {
            if (!_elevatorVoices.TryGetValue(elevator, out AudioVoiceHandle voice))
            {
                continue;
            }

            if (!elevator.State.HasMovementRequest)
            {
                // Retail owns this handle from request acceptance through arrival, including the
                // door-close gate before physical travel (IGI.exe 0x43D58E-0x43D5C8).
                _audioOut.Sounds.Stop(voice);
                _elevatorVoices.Remove(elevator);
                continue;
            }

            if (!_audioOut.Sounds.IsPlaying(voice))
            {
                _elevatorVoices.Remove(elevator);
                continue;
            }

            _audioOut.Sounds.SetDefinitionVoicePosition(
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

    /// <summary>Sounds every grenade that bounced this tick, and lets the AI hear it.</summary>
    /// <remarks>
    /// The retail grenade collision (<c>0x00476CC0</c>) plays <c>grenade_bnc_1</c> at the grenade and
    /// posts a noise the AI can investigate, both debounced so a rolling grenade sounds once. The
    /// simulation already applied the debounce; here each surviving bounce is heard.
    /// </remarks>
    private void PlayGrenadeBounces()
    {
        if (HostedPlayer is null)
        {
            return;
        }

        foreach (Vector3d at in HostedPlayer.Grenades.Bounces)
        {
            _audioOut.Sounds?.PlayDefinition("grenade_bnc_1", at, priority: 120);
        }
    }
}
