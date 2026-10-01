using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Audio;

/// <summary>Every sound a vehicle makes: its engine sound set, its guns' reports and its turrets' traverse.</summary>
/// <remarks>
/// <para>
/// <b>The engine set.</b> A car instantiates the set its physics record names (<c>+610</c>) as a child of itself
/// (<c>0x42E320</c> through <c>0x4E6B00</c>) while its "Play sound" holds; a helicopter instantiates
/// <c>helicopter_1</c> (<c>0x4316D0</c>). Each sound of the set asks the vehicle for three parameters every tick
/// (<c>0x42F850</c>): the throttle, whether the engine is driving, and the speed. A <c>DefineGraph</c> loops with
/// its gain and pitch read off two eleven-point curves of its parameter (<c>0x4EAEB0</c>, <c>0x4E7030</c>); a
/// <c>DefineTriggerOnce</c> plays once each time its parameter turns true (<c>0x4EA040</c>). The T80's
/// <c>t80_sounds</c> is defined by no retail script, so a tank's hull is silent.
/// </para>
/// <para>
/// <b>The guns</b> follow <c>Gun_Fire</c> (<c>0x478BA0</c>). A row of at most one round a burst plays one of its two
/// sounds at random for each round. A longer burst starts its first sound when the hold begins -- the T80 cannon's
/// <c>rpg_launch_1</c>, the M2HB's <c>m2hb_loop</c> -- and, when the hold ends, stops it and plays the second
/// (<c>rpg_launch_1</c> again, <c>m2hb_loop_e</c>). A one-shot that ends while the hold lasts is not restarted.
/// The mount's traverse loop (<c>tank_turret</c>) sounds while the gun is still turning to its aim (<c>0x46F390</c>).
/// </para>
/// </remarks>
internal sealed class VehicleSoundPlayer
{
    private const int Priority = 180;
    private const float TickSeconds = 1f / 30f;

    private readonly SoundSystem _sounds;
    private readonly Random _random;
    private readonly Dictionary<GameTask, EngineSet> _engines = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(GameTask Vehicle, int Index), GunVoices> _guns = [];
    private readonly HashSet<GameTask> _seenEngines = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(GameTask Vehicle, int Index)> _seenGuns = [];

    /// <summary>Initializes a new instance of the <see cref="VehicleSoundPlayer"/> class.</summary>
    /// <param name="sounds">Where sounds are played.</param>
    /// <param name="random">Picks between a single-shot row's two sounds.</param>
    public VehicleSoundPlayer(SoundSystem sounds, Random random)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(random);
        _sounds = sounds;
        _random = random;
    }

    /// <summary>Advances every vehicle's sounds one tick.</summary>
    /// <param name="vehicles">Every live vehicle's sound inputs.</param>
    /// <param name="guns">Every live vehicle gun's sound inputs.</param>
    /// <param name="listenerUnits">Where the ear is, in world units, for the distance culling a graph does.</param>
    public void Tick(IEnumerable<VehicleSoundSource> vehicles, IEnumerable<VehicleGunSound> guns, Vector3d listenerUnits)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(guns);

        _seenEngines.Clear();
        foreach (VehicleSoundSource vehicle in vehicles)
        {
            if (!vehicle.SoundOn)
            {
                continue;
            }

            if (!_engines.TryGetValue(vehicle.Vehicle, out EngineSet? set))
            {
                set = EngineSet.Create(_sounds, vehicle.SoundSet);
                _engines[vehicle.Vehicle] = set;
            }

            _seenEngines.Add(vehicle.Vehicle);
            set.Tick(_sounds, vehicle, listenerUnits);
        }

        // A vehicle that went out of the world, blew up, or turned its "Play sound" off loses its set at once.
        foreach (GameTask gone in _engines.Keys.Where(vehicle => !_seenEngines.Contains(vehicle)).ToArray())
        {
            _engines[gone].Stop(_sounds);
            _engines.Remove(gone);
        }

        _seenGuns.Clear();
        foreach (VehicleGunSound gun in guns)
        {
            (GameTask, int) key = (gun.Vehicle, gun.Index);
            if (!_guns.TryGetValue(key, out GunVoices? voices))
            {
                voices = new GunVoices();
                _guns[key] = voices;
            }

            _seenGuns.Add(key);
            TickGun(gun, voices);
        }

        // A gun deleted with its vehicle stops its loop without the tail (0x478840).
        foreach ((GameTask, int) gone in _guns.Keys.Where(key => !_seenGuns.Contains(key)).ToArray())
        {
            _guns[gone].Stop(_sounds);
            _guns.Remove(gone);
        }
    }

    /// <summary>Stops every vehicle sound, for a level that is going.</summary>
    public void StopAll()
    {
        foreach (EngineSet set in _engines.Values)
        {
            set.Stop(_sounds);
        }

        foreach (GunVoices voices in _guns.Values)
        {
            voices.Stop(_sounds);
        }

        _engines.Clear();
        _guns.Clear();
    }

    private void TickGun(in VehicleGunSound gun, GunVoices voices)
    {
        if (gun.Weapon.MaximumRoundsPerBurst <= 1)
        {
            for (int round = 0; round < gun.RoundsFired; round++)
            {
                string name = _random.Next(2) == 0 ? gun.Weapon.FireSound : gun.Weapon.FireLoopEndSound;
                _sounds.PlayDefinition(name, gun.PositionUnits, priority: Priority);
            }
        }
        else if (gun.Hold)
        {
            if (!voices.FireHeld)
            {
                voices.FireHeld = true;
                voices.Fire = _sounds.PlayDefinition(gun.Weapon.FireSound, gun.PositionUnits, priority: Priority);
            }
            else if (voices.Fire != AudioVoiceHandle.None && _sounds.IsPlaying(voices.Fire))
            {
                _sounds.SetDefinitionVoicePosition(gun.Weapon.FireSound, voices.Fire, gun.PositionUnits, priority: Priority);
            }
        }
        else if (voices.FireHeld)
        {
            voices.FireHeld = false;
            _sounds.Stop(voices.Fire);
            voices.Fire = AudioVoiceHandle.None;
            _sounds.PlayDefinition(gun.Weapon.FireLoopEndSound, gun.PositionUnits, priority: Priority);
        }

        if (gun.Turning && gun.TurnSound.Length > 0)
        {
            if (voices.Turn == AudioVoiceHandle.None || !_sounds.IsPlaying(voices.Turn))
            {
                voices.Turn = _sounds.PlayDefinition(gun.TurnSound, gun.PositionUnits, priority: Priority);
            }
            else
            {
                _sounds.SetDefinitionVoicePosition(gun.TurnSound, voices.Turn, gun.PositionUnits, priority: Priority);
            }
        }
        else if (voices.Turn != AudioVoiceHandle.None)
        {
            _sounds.Stop(voices.Turn);
            voices.Turn = AudioVoiceHandle.None;
        }
    }

    /// <summary>The voices of one gun: its fire sound while held, and its turret's traverse.</summary>
    private sealed class GunVoices
    {
        public bool FireHeld { get; set; }

        public AudioVoiceHandle Fire { get; set; }

        public AudioVoiceHandle Turn { get; set; }

        public void Stop(SoundSystem sounds)
        {
            sounds.Stop(Fire);
            sounds.Stop(Turn);
            Fire = AudioVoiceHandle.None;
            Turn = AudioVoiceHandle.None;
            FireHeld = false;
        }
    }

    /// <summary>One instantiated engine sound set: a voice slot per sound the named definition holds.</summary>
    private sealed class EngineSet
    {
        private readonly List<Member> _members;

        private EngineSet(List<Member> members) => _members = members;

        public static EngineSet Create(SoundSystem sounds, string name)
        {
            List<Member> members = [];
            if (name.Length > 0
                && sounds.Definitions is { } definitions
                && definitions.TryResolve(name, out SoundDefinition? root)
                && root is not null)
            {
                Collect(root, members);
            }

            return new EngineSet(members);
        }

        public void Tick(SoundSystem sounds, in VehicleSoundSource vehicle, Vector3d listenerUnits)
        {
            ReadOnlySpan<float> parameters = [vehicle.Throttle, vehicle.Running, vehicle.Speed];
            double distance = (listenerUnits - vehicle.PositionUnits).Length;
            foreach (Member member in _members)
            {
                member.Tick(sounds, parameters, vehicle.PositionUnits, distance);
            }
        }

        public void Stop(SoundSystem sounds)
        {
            foreach (Member member in _members)
            {
                member.Stop(sounds);
            }
        }

        private static void Collect(SoundDefinition definition, List<Member> into)
        {
            if (definition.Kind == SoundDefinitionKind.Group)
            {
                foreach (SoundDefinition child in definition.Children)
                {
                    Collect(child, into);
                }

                return;
            }

            into.Add(new Member(definition));
        }
    }

    /// <summary>One sound of an engine set and its voice.</summary>
    private sealed class Member(SoundDefinition definition)
    {
        private AudioVoiceHandle _voice;
        private TriggerState _trigger;
        private float _elapsed;
        private bool _started;

        private enum TriggerState
        {
            Armed,
            Playing,
            Spent,
        }

        public void Tick(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, double distanceUnits)
        {
            bool inRange = distanceUnits <= definition.MaximumDistanceMetres * SoundSystem.UnitsPerMetre;
            switch (definition.Kind)
            {
                case SoundDefinitionKind.Graph:
                    TickGraph(sounds, parameters, positionUnits, inRange);
                    break;
                case SoundDefinitionKind.TriggerOnce:
                    TickTrigger(sounds, parameters, positionUnits, inRange);
                    break;
                default:
                    // A plain sound in a set plays once, from its creation, following the vehicle.
                    if (!_started)
                    {
                        _started = true;
                        _voice = Start(sounds, positionUnits, Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                    }
                    else if (_voice != AudioVoiceHandle.None && sounds.IsPlaying(_voice))
                    {
                        Move(sounds, positionUnits, Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                    }

                    break;
            }
        }

        public void Stop(SoundSystem sounds)
        {
            sounds.Stop(_voice);
            _voice = AudioVoiceHandle.None;
        }

        // 0x4EAEB0: gain off curve A of its parameter (0 while the parameter is negative), pitch off curve B plus the
        // optional added parameter; looping, and only issued within its maximum distance. Gain 0 frees the voice, so
        // the loop starts again from the top when it rises.
        private void TickGraph(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, bool inRange)
        {
            float gain = 1f;
            if (definition.VolumeParameterIndex >= 0)
            {
                float x = Parameter(parameters, definition.VolumeParameterIndex);
                gain = x < 0f ? 0f : SoundDefinition.Sample(definition.GainCurve, x);
            }

            float pitch = definition.PitchParameterIndex >= 0
                ? SoundDefinition.Sample(definition.PitchCurve, Parameter(parameters, definition.PitchParameterIndex))
                : 1f;
            if (definition.PitchAddParameterIndex >= 0)
            {
                pitch += Parameter(parameters, definition.PitchAddParameterIndex);
            }

            gain *= Math.Clamp(definition.Volume, 0f, 1f);
            if (!(gain > 0f) || !inRange)
            {
                Stop(sounds);
                return;
            }

            if (_voice == AudioVoiceHandle.None || !sounds.IsPlaying(_voice))
            {
                _voice = Start(sounds, positionUnits, gain, pitch, looping: true);
            }
            else
            {
                Move(sounds, positionUnits, gain, pitch, looping: true);
            }
        }

        // 0x4EA040: fires on its parameter turning true (inverted: zero), plays for its duration or its sample's
        // length, and arms again only once the parameter has gone false.
        private void TickTrigger(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, bool inRange)
        {
            float value = definition.TriggerParameterIndex >= 0 ? Parameter(parameters, definition.TriggerParameterIndex) : 0f;
            bool fired = definition.Invert ? value == 0f : (int)value != 0;
            switch (_trigger)
            {
                case TriggerState.Armed:
                    if (fired && inRange)
                    {
                        float gain = definition.VolumeParameterIndex >= 0
                            ? Parameter(parameters, definition.VolumeParameterIndex)
                            : 1f;
                        _voice = Start(sounds, positionUnits, gain * Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                        _elapsed = 0f;
                        _trigger = TriggerState.Playing;
                    }

                    break;
                case TriggerState.Playing:
                    _elapsed += TickSeconds;
                    bool done = definition.DurationSeconds >= 0f
                        ? _elapsed >= definition.DurationSeconds
                        : definition.Loop ? !fired : _voice == AudioVoiceHandle.None || !sounds.IsPlaying(_voice);
                    if (done)
                    {
                        Stop(sounds);
                        _trigger = fired ? TriggerState.Spent : TriggerState.Armed;
                    }
                    else if (_voice != AudioVoiceHandle.None)
                    {
                        Move(sounds, positionUnits, Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                    }

                    break;
                case TriggerState.Spent:
                    if (!fired)
                    {
                        _trigger = TriggerState.Armed;
                    }

                    break;
            }
        }

        private AudioVoiceHandle Start(SoundSystem sounds, Vector3d positionUnits, float gain, float pitch, bool looping) =>
            sounds.Play(definition.BankKey, Parameters(positionUnits, gain, pitch, looping));

        private void Move(SoundSystem sounds, Vector3d positionUnits, float gain, float pitch, bool looping) =>
            sounds.SetVoiceParameters(_voice, Parameters(positionUnits, gain, pitch, looping));

        private VoiceParameters Parameters(Vector3d positionUnits, float gain, float pitch, bool looping) =>
            (VoiceParameters.Default with
            {
                Gain = gain,
                Pitch = Math.Clamp(pitch, 0.5f, 2f),
                Looping = looping,
                Priority = Priority,
            }).At(
                SoundSystem.ToMetres(positionUnits + definition.Offset),
                definition.MinimumDistanceMetres,
                definition.MaximumDistanceMetres);

        private static float Parameter(ReadOnlySpan<float> parameters, int index) =>
            index >= 0 && index < parameters.Length ? parameters[index] : 0f;
    }
}
