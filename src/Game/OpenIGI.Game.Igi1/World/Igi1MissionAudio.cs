using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Project I.G.I.'s mission heard: the player's weapon and body, the soldiers' guns and feet, the doors and lifts,
/// the sirens and cameras, and the level's ambience.</summary>
/// <remarks>Every voice the mission holds is held here, so the host only opens the device and places the ear.</remarks>
/// <param name="mission">The mission whose things make the sounds, and whose stream picks among them.</param>
/// <param name="host">Where they play.</param>
public sealed class Igi1MissionAudio(MissionWorld mission, IMissionSoundHost host) : IMissionAudio
{
    /// <summary>The travelling voice of each door currently in motion.</summary>
    private readonly Dictionary<PlacedDoor, AudioVoiceHandle> _doorVoices = [];

    private readonly Dictionary<PlacedElevator, AudioVoiceHandle> _elevatorVoices =
        new(ReferenceEqualityComparer.Instance);

    private AudioVoiceHandle _binocularZoomVoice;

    /// <summary>The bed each sounding ambient area is playing.</summary>
    private readonly Dictionary<PlacedAmbientArea, AudioVoiceHandle> _ambientVoices = [];
    private readonly Dictionary<GameTask, AudioVoiceHandle> _taskLoops = [];
    private readonly Dictionary<GameTask, PlacedAmbientArea> _taskAreas = [];

    /// <summary>Makes the player's weapon sound. Null until the device is open.</summary>
    private WeaponSoundPlayer? _weaponSounds;

    /// <summary>The cook-tick voice held while a grenade throw charges.</summary>
    private AudioVoiceHandle _grenadeCookVoice = AudioVoiceHandle.None;

    /// <summary>Whether the cook tick has already been started for the current charge.</summary>
    private bool _grenadeCookStarted;

    /// <summary>One automatic-loop owner per AI human, so simultaneous rifles never share a voice.</summary>
    private readonly Dictionary<int, WeaponSoundPlayer> _soldierWeaponSounds = [];

    private readonly Dictionary<AlarmSpeaker, AudioVoiceHandle> _sirenVoices = [];

    // Which loop each sounding speaker's voice is playing: a siren shot out mid-alarm swaps its working for its faulty one.
    private readonly Dictionary<AlarmSpeaker, string> _sirenSounds = [];

    private readonly HashSet<AlarmSpeaker> _reportedSirenFailures = [];

    private readonly Dictionary<SecurityCamera, (string Sound, AudioVoiceHandle Voice)> _cameraBeepVoices = [];

    private readonly Dictionary<SecurityCamera, AudioVoiceHandle> _cameraMoveVoices = [];

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
        if (host.SoundOutput is null || mission.Player is null || !mission.Player.IsChargingThrow)
        {
            StopGrenadeCook();
            return;
        }

        if (!_grenadeCookStarted)
        {
            _grenadeCookVoice = host.SoundOutput.PlayDefinition("grenade_tick_1", priority: 200);
            _grenadeCookStarted = true;
        }
    }

    private void StopGrenadeCook()
    {
        if (_grenadeCookVoice.IsValidHandle)
        {
            host.SoundOutput?.Stop(_grenadeCookVoice);
        }

        _grenadeCookVoice = AudioVoiceHandle.None;
        _grenadeCookStarted = false;
    }

    /// <summary>Drives one soldier's weapon loop for the tick, each weapon on a voice of its own.</summary>
    /// <param name="runtimeId">The soldier's weapon's runtime id, which keys its voice.</param>
    /// <param name="fired">Whether it fired this tick.</param>
    /// <param name="weapon">What it carries, or <see langword="null"/> for nothing.</param>
    /// <param name="position">Where it is, in world units.</param>
    public void TickSoldierWeaponSound(int runtimeId, bool fired, WeaponType? weapon, Vector3d position)
    {
        if (host.SoundOutput is null)
        {
            return;
        }

        if (!_soldierWeaponSounds.TryGetValue(runtimeId, out WeaponSoundPlayer? player))
        {
            player = new WeaponSoundPlayer(host.SoundOutput);
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
        if (host.SoundOutput is null || mission.AnimationTriggers is null || mission.Player?.View is not { } view)
        {
            return;
        }

        foreach (AnimTriggerDefinition trigger in mission.AnimationTriggers.Definitions.Values)
        {
            if (view.CrossedEvent(trigger.EventId))
            {
                // The player's view rig is at the listener, so this is deliberately non-positional.
                host.SoundOutput.PlayDefinition(trigger.Resource, priority: 180);
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
        if (host.SoundOutput is null || mission.Player is not { } player)
        {
            return;
        }

        if (!player.IsBinocularsOpen || !player.BinocularZoomSteppingThisTick)
        {
            StopBinocularTransitionSound();
            return;
        }

        if (_binocularZoomVoice.IsValidHandle && host.SoundOutput.IsPlaying(_binocularZoomVoice))
        {
            return;
        }

        _binocularZoomVoice = host.SoundOutput.PlayDefinition("bin_zoom_1", priority: 180, looping: true);
    }

    /// <summary>Stops the looping binocular cue when player ticking is suspended.</summary>
    /// <remarks>
    /// Death-camera and mission teardown deliberately stop ticking the player. Those paths therefore
    /// cannot rely on <see cref="PlayBinocularTransitionSound"/> observing a closed-view edge on a
    /// later player tick; stop the voice at the lifecycle boundary instead.
    /// </remarks>
    public void StopBinocularTransitionSound()
    {
        if (_binocularZoomVoice.IsValidHandle)
        {
            host.SoundOutput?.Stop(_binocularZoomVoice);
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
        if (mission.Player?.Weapons is { MedipackStartedThisTick: true } weapons)
        {
            // A medipack is an injection action, not a gun shot. Some weapon records carry a
            // legacy firearm FireSound field (which produces the shotgun/pistol report); the
            // retail medipack cue is the standalone syringe sound and is always used here.
            host.SoundOutput?.PlayDefinition("syringe", priority: 180);
        }
    }

    /// <summary>Plays the retail world-weapon cue after a <c>GunPickup</c> reaches the player.</summary>
    public void PlayGunPickupSound(GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        host.SoundOutput?.PlayDefinition("weaponpickup_1", priority: 180);
    }

    /// <summary>Plays the fixed retail cue after an authored generic item reaches the player.</summary>
    /// <remarks>
    /// <c>GenericPickup::cleanup</c> calls <c>0x4E7200("pickup", 0, 0)</c>. The cue is shared by C4,
    /// keycards and every other <c>GenericPickup</c>; the model does not select a per-item sound.
    /// Like retail, it is played at the listener rather than at the item's former world position.
    /// </remarks>
    public void PlayGenericPickupSound(GenericPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);

        // Ammo pickups are authored as GenericPickup/AmmoPickup world objects. Resolve and award them
        // at the same collection edge as the retail pickup cue, so the object, inventory count and sound
        // cannot drift apart. An explicit task payload wins over the catalog fallback.
        if (mission.Player?.Weapons is { } weapons
            && mission.AmmoTypes is { } ammoTypes
            && AmmoPickupCatalog.TryResolve(pickup, ammoTypes, out AmmoType ammo))
        {
            weapons.Ammunition.Add(ammo.AmmoId, AmmoPickupCatalog.Amount(pickup, ammo.AmmoId));
        }

        host.SoundOutput?.PlayDefinition("pickup", priority: 180);
    }

    /// <summary>Advances the level's authored ambience and keeps its beds sounding.</summary>
    /// <param name="listenerInUnits">Where the ear is, in engine units.</param>
    /// <remarks>
    /// The beds are played <b>flat</b>, not positionally, because that is what the original does with
    /// them: it sets the sound's position to the listener's every tick, which is a wash around the
    /// player rather than a source in the world. Gain is the area's fade, so walking into a machine
    /// room brings its hum up over twenty ticks instead of switching it on at the doorway.
    /// </remarks>
    public void TickAmbience(Vector3d listenerInUnits)
    {
        if (mission.Ambience is not { } ambience || host.SoundOutput is null)
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
                    host.SoundOutput.Stop(running);
                }

                area.IsSounding = false;
                continue;
            }

            if (area.PendingSound.Length > 0)
            {
                AudioVoiceHandle started = host.SoundOutput.PlayDefinition(
                    area.PendingSound, gain: (float)area.Fade, priority: 60);

                if (started != AudioVoiceHandle.None)
                {
                    _ambientVoices[area] = started;
                    area.IsSounding = true;
                }
            }
            else if (area.IsSounding && _ambientVoices.TryGetValue(area, out AudioVoiceHandle voice))
            {
                if (host.SoundOutput.IsPlaying(voice))
                {
                    host.SoundOutput.SetVoiceParameters(voice, VoiceParameters.Default with { Gain = (float)area.Fade });
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
        host.SoundOutput?.PlayDefinition(PlayerBody.JumpSoundName, priority: 200);
    }

    /// <summary>Plays a footstep where it landed.</summary>
    /// <param name="step">The step, already resolved against the ground it landed on.</param>
    /// <remarks>
    /// Pitched between 0.8 and 1.3, which is the whole of why two steps on the same surface are never
    /// quite the same sound. Low priority: a firefight is more worth a voice than a footfall.
    /// </remarks>
    public void PlayFootstep(in Footstep step)
    {
        if (host.SoundOutput is null || step.Sound.Length == 0)
        {
            return;
        }

        // One pitch for the whole step: 0x463920 computes it once and hands the same value to the
        // material voice and to the rain voice, so the two halves of a wet step stay together.
        float pitch = FootstepSounds.Pitch(mission.SoundRandom);

        host.SoundOutput.PlayDefinition(
            step.Sound,
            step.PositionInUnits,
            gain: step.VolumeScale,
            pitch: pitch,
            priority: 40);

        // The wet layer is played AS WELL AS the material's own sound, never instead of it. Its gate
        // is 0x416FB0, a single global byte -- the level's weather flag rather than a shelter test --
        // which is what the rain ambience already reads through the RainEffect task.
        if (host.IsRaining)
        {
            host.SoundOutput.PlayDefinition(
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
    public void PlayLandingSound((string Sound, Vector3d PositionInUnits) landing)
    {
        if (host.SoundOutput is null || landing.Sound.Length == 0)
        {
            return;
        }

        host.SoundOutput.PlayDefinition(landing.Sound, landing.PositionInUnits, priority: 60);
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
        if (host.SoundOutput is null || mission.AlarmSpeakers is null)
        {
            return;
        }

        foreach (AlarmSpeaker speaker in mission.AlarmSpeakers.Speakers)
        {
            string sound = mission.AlarmSpeakers.SoundNow(speaker);
            bool on = sound.Length > 0;
            bool sounding = _sirenVoices.TryGetValue(speaker, out AudioVoiceHandle voice);

            // A siren shot out mid-alarm swaps its working loop for its faulty one (0x4494D0).
            if (sounding && on && _sirenSounds.TryGetValue(speaker, out string? playingSound)
                && !string.Equals(playingSound, sound, StringComparison.Ordinal))
            {
                host.SoundOutput.Stop(voice);
                _sirenVoices.Remove(speaker);
                sounding = false;
            }

            bool playing = sounding && host.SoundOutput.IsPlaying(voice);

            if (on && (!sounding || !playing))
            {
                if (sounding && _reportedSirenFailures.Add(speaker))
                {
                    host.Logger.Warning(
                        "Audio.Alarm",
                        $"voice {voice.Value} for {speaker.Task.Name} ended while its condition remained true; restarting.");
                }

                AudioVoiceHandle started = host.SoundOutput.PlayDefinition(
                    sound,
                    speaker.PositionUnits,
                    priority: 70);
                _sirenVoices[speaker] = started;
                _sirenSounds[speaker] = sound;

                if (!sounding)
                {
                    host.Logger.Information(
                        "Audio.Alarm",
                        $"start {speaker.Task.Name}: sound='{sound}', voice={started.Value}, "
                            + $"playing={host.SoundOutput.IsPlaying(started)}, position={speaker.PositionUnits}, "
                            + $"condition='{speaker.AlarmExpression}'.");
                }

                if ((!started.IsValidHandle || !host.SoundOutput.IsPlaying(started))
                    && _reportedSirenFailures.Add(speaker))
                {
                    host.Logger.Warning(
                        "Audio.Alarm",
                        $"'{sound}' failed to produce a playing voice for {speaker.Task.Name}.");
                }
            }
            else if (!on && sounding)
            {
                host.SoundOutput.Stop(voice);
                _sirenVoices.Remove(speaker);
                _reportedSirenFailures.Remove(speaker);
                host.Logger.Information(
                    "Audio.Alarm",
                    $"stop {speaker.Task.Name}: voice={voice.Value}, condition='{speaker.AlarmExpression}'.");
            }
        }
    }

    /// <summary>Keeps the detection and motor loops owned by each <c>SCameraLens</c> in sync.</summary>
    private void TickSecurityCameraSounds()
    {
        if (mission.SecurityCameras is not { } cameras || host.SoundOutput is null)
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
                    || !host.SoundOutput.IsPlaying(sounding.Voice)))
            {
                host.SoundOutput.Stop(sounding.Voice);
                _cameraBeepVoices.Remove(camera);
            }

            if (beep.Length > 0 && !_cameraBeepVoices.ContainsKey(camera))
            {
                AudioVoiceHandle voice = host.SoundOutput.PlayDefinition(beep, camera.HeadPositionUnits, priority: 72);
                _cameraBeepVoices[camera] = (beep, voice);
            }

            if (_cameraMoveVoices.TryGetValue(camera, out AudioVoiceHandle moving)
                && (!camera.IsMoving || !host.SoundOutput.IsPlaying(moving)))
            {
                host.SoundOutput.Stop(moving);
                _cameraMoveVoices.Remove(camera);
            }

            if (camera.IsMoving && !_cameraMoveVoices.ContainsKey(camera))
            {
                _cameraMoveVoices[camera] = host.SoundOutput.PlayDefinition(
                    "camera_move",
                    camera.HeadPositionUnits,
                    priority: 55);
            }
        }
    }

    /// <summary>Plays what a level task does with a sound: a switch's click, a generator's hum, an electric fence's buzz.</summary>
    /// <param name="sound">The task's sound.</param>
    public void PlayInteractionSound(InteractionSoundEvent sound)
    {
        switch (sound.What)
        {
            case InteractionSound.AreaStarted when mission.Ambience is { } ambience:
                _taskAreas[sound.Task] = ambience.Add(sound);
                return;
            case InteractionSound.AreaStopped:
                if (_taskAreas.Remove(sound.Task, out PlacedAmbientArea? area))
                {
                    AmbientAreaRegistry.Retire(area);
                }

                return;
        }

        if (host.SoundOutput is not { } output)
        {
            return;
        }

        switch (sound.What)
        {
            case InteractionSound.Played:
                output.PlayDefinition(sound.Sound, sound.Position, priority: 120);
                break;
            case InteractionSound.LoopStarted:
                AudioVoiceHandle voice = output.PlayDefinition(sound.Sound, sound.Position, priority: 120);
                if (voice.IsValidHandle)
                {
                    _taskLoops[sound.Task] = voice;
                }

                break;
            case InteractionSound.LoopStopped:
                if (_taskLoops.Remove(sound.Task, out AudioVoiceHandle running))
                {
                    output.Stop(running);
                }

                break;
        }
    }

    /// <summary>Plays a door's sounds, and owns the one voice that has to be held and stopped.</summary>
    /// <param name="door">The door that made the noise.</param>
    /// <param name="what">Which of its four moments this is.</param>
    public void PlayDoorSound(PlacedDoor door, DoorSoundEvent what)
    {
        if (host.SoundOutput is null)
        {
            return;
        }

        if (what == DoorSoundEvent.StoppedMoving)
        {
            if (_doorVoices.Remove(door, out AudioVoiceHandle running))
            {
                host.SoundOutput.Stop(running);
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

        AudioVoiceHandle voice = host.SoundOutput.PlayDefinition(name, door.Position, priority: 120);

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
    public void PlayElevatorSound(PlacedElevator elevator, ElevatorSoundEvent what)
    {
        if (host.SoundOutput is null)
        {
            return;
        }

        if (what == ElevatorSoundEvent.Stopped)
        {
            StopElevatorTravelVoice(elevator);

            if (elevator.StopSound.Length > 0)
            {
                host.SoundOutput.PlayDefinition(elevator.StopSound, elevator.State.Position, priority: 120);
            }

            return;
        }

        if (what == ElevatorSoundEvent.Started)
        {
            if (elevator.StartSound.Length > 0)
            {
                host.SoundOutput.PlayDefinition(elevator.StartSound, elevator.State.Position, priority: 120);
            }

            return;
        }

        if (elevator.MoveSound.Length > 0)
        {
            // A repeat start can happen after a scripted retarget. Retire the old voice first so a
            // replaced dictionary entry can never leave an untracked, permanently looping sample.
            StopElevatorTravelVoice(elevator);
            AudioVoiceHandle voice = host.SoundOutput.PlayDefinition(
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
        if (host.SoundOutput is not null && _elevatorVoices.Remove(elevator, out AudioVoiceHandle running))
        {
            host.SoundOutput.Stop(running);
        }
    }

    /// <summary>Keeps each retail elevator travel loop attached to the moving car.</summary>
    public void UpdateElevatorSoundPositions()
    {
        if (host.SoundOutput is null || mission.Elevators is null)
        {
            return;
        }

        foreach (PlacedElevator elevator in mission.Elevators.Elevators)
        {
            if (!_elevatorVoices.TryGetValue(elevator, out AudioVoiceHandle voice))
            {
                continue;
            }

            if (!elevator.State.HasMovementRequest)
            {
                // Retail owns this handle from request acceptance through arrival, including the
                // door-close gate before physical travel (IGI.exe 0x43D58E-0x43D5C8).
                host.SoundOutput.Stop(voice);
                _elevatorVoices.Remove(elevator);
                continue;
            }

            if (!host.SoundOutput.IsPlaying(voice))
            {
                _elevatorVoices.Remove(elevator);
                continue;
            }

            host.SoundOutput.SetDefinitionVoicePosition(
                elevator.MoveSound,
                voice,
                elevator.State.Position,
                priority: 120);
        }
    }

    /// <summary>Sounds every grenade that bounced this tick, and lets the AI hear it.</summary>
    /// <remarks>
    /// The retail grenade collision (<c>0x00476CC0</c>) plays <c>grenade_bnc_1</c> at the grenade and
    /// posts a noise the AI can investigate, both debounced so a rolling grenade sounds once. The
    /// simulation already applied the debounce; here each surviving bounce is heard.
    /// </remarks>
    public void PlayGrenadeBounces()
    {
        if (mission.Player is null)
        {
            return;
        }

        foreach (Vector3d at in mission.Player.Grenades.Bounces)
        {
            host.SoundOutput?.PlayDefinition("grenade_bnc_1", at, priority: 120);
        }
    }

    /// <summary>The sounds of the player's update: the binoculars' zoom and the medipack.</summary>
    public void SoundPlayerUpdate()
    {
        PlayBinocularTransitionSound();
        PlayMedipackSound();
    }

    /// <summary>The sounds of the player's weapon and body this tick: the report, the launcher, the cook, the reload and the jump.</summary>
    public void SoundPlayerWeaponAndBody()
    {
        PlayerCamera player = mission.Player!;

        // And the sound of it. Driven every tick rather than only on the ones that fire,
        // because an automatic's loop has to be told when the trigger was RELEASED as much
        // as when it was pulled -- see WeaponSoundPlayer.
        // FUN_0040FB30/0x40FB90 drives medipack animation tasks directly. A medipack is
        // not a firearm, so never feed its weapon record through WeaponSoundPlayer: some
        // live rows carry a legacy gun sound and that produced a SPAS-like report. Passing
        // null also closes an automatic gun loop when the player switches to the syringe.
        WeaponType? weaponForSound = player.HeldWeaponType?.Category == WeaponCategory.Medipack
            ? null
            : player.HeldWeaponType;
        bool m203Launched = player.M203LaunchedThisTick;
        _weaponSounds?.Tick(
            player.FiredThisTick && !m203Launched && weaponForSound is not null,
            weaponForSound);
        if (m203Launched)
        {
            // GunM16A2's native callback (0x473510) plays this launch report itself; it is
            // not the M16A2's primary rifle loop and must remain audible on an alternate shot.
            // The native call attaches it to the launcher task position, not to the listener.
            Vector3d launchPosition = player.Body.Eye;
            if (player.Weapons?.M203Shots is { Count: > 0 } m203Shots)
            {
                launchPosition = m203Shots[0].Origin;
            }

            host.SoundOutput?.PlayDefinition("m203_launch_1", launchPosition, priority: 250);
        }

        // The grenade's cook tick: a looping sound held while the throw charges, stopped the
        // moment it is released (0x40F590 starts it, 0x40F640 stops it).
        TickGrenadeCook();

        // Reloads and a few other weapon actions name their sounds through BOED animation
        // events rather than through the weapon record. The rig advanced inside Player.Tick,
        // so its crossed-event interval is ready to dispatch here.
        // The native medipack callbacks dispatch their own clips and do not enter the
        // generic firearm BOED trigger path. Suppress those generic triggers while the
        // medipack is held, otherwise a shared animation event can sound like reload/fire.
        if (player.HeldWeaponType?.Category != WeaponCategory.Medipack)
        {
            PlayPlayerAnimationSounds();
        }

        // State 28's entry (0x40BBF7) plays jump_1 through 0x4DD750 on the take-off tick.
        if (player.Body.TookOffThisTick)
        {
            PlayJumpSound();
        }
    }

    /// <summary>The alarm speakers' sirens and the security cameras' beeps and motors, following their own state.</summary>
    public void SoundAlarms()
    {
        TickAlarmSpeakers();
        TickSecurityCameraSounds();
    }

    /// <summary>Makes the player's weapon voice afresh on the device just opened, and drops the soldiers'.</summary>
    public void OnDeviceOpened()
    {
        _soldierWeaponSounds.Clear();
        _weaponSounds = new WeaponSoundPlayer(host.SoundOutput!);
    }

    /// <summary>Stops the player's held voices and the weapons' loops, as the bank is emptied for a mission.</summary>
    public void ResetForMission()
    {
        StopBinocularTransitionSound();
        StopGrenadeCook();
        _weaponSounds?.Reset();
        foreach (WeaponSoundPlayer soldier in _soldierWeaponSounds.Values)
        {
            soldier.Reset();
        }

        _soldierWeaponSounds.Clear();
    }

    /// <summary>Stops the player's weapon loop where it is.</summary>
    public void ResetWeaponSounds() => _weaponSounds?.Reset();

    /// <summary>Forgets the ambience voices, as a level's areas are rebuilt.</summary>
    public void ForgetAmbience()
    {
        _ambientVoices.Clear();
        _taskAreas.Clear();
        _taskLoops.Clear();
    }

    /// <summary>Forgets the security cameras' voices, as the cameras are rebuilt or released.</summary>
    public void ForgetCameras()
    {
        _cameraBeepVoices.Clear();
        _cameraMoveVoices.Clear();
    }

    /// <summary>Forgets the sirens' voices and the failures already reported, as the speakers are rebuilt or released.</summary>
    public void ForgetSirens()
    {
        _sirenVoices.Clear();
        _reportedSirenFailures.Clear();
    }

    /// <summary>Stops a destroyed speaker's siren.</summary>
    /// <param name="speaker">The speaker.</param>
    public void SilenceSiren(AlarmSpeaker speaker)
    {
        if (_sirenVoices.Remove(speaker, out AudioVoiceHandle siren))
        {
            host.SoundOutput?.Stop(siren);
        }
    }

    /// <summary>Stops a destroyed camera's beep and motor.</summary>
    /// <param name="camera">The camera.</param>
    public void SilenceCamera(SecurityCamera camera)
    {
        if (_cameraBeepVoices.Remove(camera, out var beep))
        {
            host.SoundOutput?.Stop(beep.Voice);
        }

        if (_cameraMoveVoices.Remove(camera, out AudioVoiceHandle motor))
        {
            host.SoundOutput?.Stop(motor);
        }
    }

    /// <summary>Plays a sound a soldier's script asks for (<c>AIAction_PlaySound</c>).</summary>
    /// <param name="name">The definition.</param>
    /// <param name="position">Where, in world units.</param>
    /// <param name="attached">Whether it follows the soldier.</param>
    public void PlayScriptSound(string name, Vector3d position, bool attached) =>
        host.SoundOutput?.PlayDefinition(name, position, priority: 200);
}
