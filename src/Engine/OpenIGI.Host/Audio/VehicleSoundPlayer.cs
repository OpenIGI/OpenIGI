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
/// <c>helicopter_1</c> (<c>0x4316D0</c>) and a plane <c>su27</c> (<c>0x433690</c>). Each sound of the set asks the
/// vehicle for its parameters every tick (<c>0x42F850</c>, <c>0x432BB0</c>, <c>0x434A30</c>): the throttle, whether
/// the engine is driving, and -- a car only -- the speed. A <c>DefineGraph</c> loops with
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
    private const int Priority = VehicleEngineSound.Priority;

    private readonly SoundSystem _sounds;
    private readonly Random _random;
    private readonly Dictionary<object, VehicleEngineSound> _engines = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(GameTask Vehicle, int Index), GunVoices> _guns = [];
    private readonly HashSet<object> _seenEngines = new(ReferenceEqualityComparer.Instance);
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

            object key = vehicle.Key ?? vehicle.Vehicle;
            if (!_engines.TryGetValue(key, out VehicleEngineSound? set))
            {
                set = VehicleEngineSound.Create(_sounds, vehicle.SoundSet);
                _engines[key] = set;
            }

            _seenEngines.Add(key);
            set.Tick(_sounds, [vehicle.Throttle, vehicle.Running, vehicle.Speed], vehicle.PositionUnits, listenerUnits);
        }

        // A vehicle that went out of the world, blew up, or turned its "Play sound" off loses its set at once.
        foreach (object gone in _engines.Keys.Where(vehicle => !_seenEngines.Contains(vehicle)).ToArray())
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
        foreach (VehicleEngineSound set in _engines.Values)
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
}
