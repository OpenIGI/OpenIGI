using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Vehicles;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// The level's vehicles and guns as a co-op server shares them (<see cref="Game.Multiplayer.Snapshot.Vehicles"/>): each car where
/// it stands and its gun's aim, each train how far along its rail, each turret's and each mounted gun's aim -- in the tree's order,
/// which every machine playing the level shares.
/// </summary>
/// <remarks>
/// A client's copies are the server's puppets (<see cref="Igi2CarTask.IsPuppet"/>): they drive, watch, kill, fire and blow up only
/// as the server's do. Their rounds come with what the server's fired (<see cref="Igi2FiredLog"/>), their blasts with its explosions.
/// </remarks>
internal sealed class Igi2VehicleStates
{
    private readonly Igi2CarTask[] _cars;
    private readonly Igi2TrainTask[] _trains;
    private readonly Igi2AiStationaryGunHolderTask[] _turrets;
    private readonly Igi2StationaryGunTask[] _mounted;
    private readonly BitWriter _writer = new(512);

    /// <summary>Initializes a new instance of the <see cref="Igi2VehicleStates"/> class over a level's vehicles and guns.</summary>
    /// <param name="level">The level.</param>
    public Igi2VehicleStates(Igi2TitlePlayer level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Engine.Tasks.GameTask[] tasks = [.. level.LevelTasks()];
        _cars = [.. tasks.OfType<Igi2CarTask>()];
        _trains = [.. tasks.OfType<Igi2TrainTask>().Where(static t => !t.IsCarriage)];
        _turrets = [.. tasks.OfType<Igi2AiStationaryGunHolderTask>()];
        _mounted = [.. tasks.OfType<Igi2StationaryGunTask>()];
    }

    /// <summary>Gets each car's, turret's and mounted gun's weapon, by the source a launch from it is noted under.</summary>
    /// <returns>Each weapon, with its source.</returns>
    public IEnumerable<(int Source, Igi2WeaponFiring Firing)> Weapons()
    {
        for (int i = 0; i < _cars.Length; i++)
        {
            if (_cars[i].Gun is { } gun)
            {
                yield return (CarSource + i, gun.Firing);
            }
        }

        for (int i = 0; i < _turrets.Length; i++)
        {
            if (_turrets[i].Gun is { } gun)
            {
                yield return (TurretSource + i, gun.Firing);
            }
        }

        for (int i = 0; i < _mounted.Length; i++)
        {
            if (_mounted[i].Firing is { } firing)
            {
                yield return (MountedSource + i, firing);
            }
        }
    }

    /// <summary>The source a car's gun's launches are noted under: its place past this.</summary>
    public const int CarSource = 200000;

    /// <summary>The source a turret's launches are noted under: its place past this.</summary>
    public const int TurretSource = 300000;

    /// <summary>The source a mounted gun's launches are noted under: its place past this.</summary>
    public const int MountedSource = 400000;

    /// <summary>Gets the weapon a launch's source names, on a client, or <see langword="null"/> for one that is no vehicle's or gun's.</summary>
    /// <param name="source">The source.</param>
    /// <returns>The weapon.</returns>
    public Igi2WeaponFiring? WeaponOf(int source) => source switch
    {
        // A gun this client's own player works fired its rounds here as it fired them (Igi2StationaryGunTask.Run): the
        // server's are not fired again over them.
        >= MountedSource when source - MountedSource < _mounted.Length => _mounted[source - MountedSource] is { Operator: null } gun ? gun.Firing : null,
        >= TurretSource and < MountedSource when source - TurretSource < _turrets.Length => _turrets[source - TurretSource].Gun?.Firing,
        >= CarSource and < TurretSource when source - CarSource < _cars.Length => _cars[source - CarSource].Gun?.Firing,
        _ => null,
    };

    /// <summary>Writes the vehicles and guns as they are, on the server.</summary>
    /// <returns>The bytes, or <see langword="null"/> for a level with none.</returns>
    public byte[]? Capture()
    {
        if (_cars.Length + _trains.Length + _turrets.Length + _mounted.Length == 0)
        {
            return null;
        }

        BitWriter w = _writer;
        w.Reset();
        w.WriteVarUInt((uint)_cars.Length);
        foreach (Igi2CarTask car in _cars)
        {
            Igi2CarPose pose = car.Pose();
            w.WriteBool(pose.IsExploded);
            if (pose.IsExploded)
            {
                continue;
            }

            Igi2NetPosition.Write(w, pose.Position);
            (float alpha, float beta, float gamma) = EngineEuler.Angles(pose.Orientation);
            w.WriteSingle(alpha);
            w.WriteSingle(beta);
            w.WriteSingle(gamma);
            w.WriteBool(pose.IsDoorOpen);
            WriteAim(w, pose.Gun);
        }

        w.WriteVarUInt((uint)_trains.Length);
        foreach (Igi2TrainTask train in _trains)
        {
            Igi2TrainLeg leg = train.Leg();
            w.WriteSingle(leg.Distance);
            w.WriteSingle(leg.Speed);
            w.WriteSingle(leg.Thrust);
        }

        w.WriteVarUInt((uint)_turrets.Length);
        foreach (Igi2AiStationaryGunHolderTask turret in _turrets)
        {
            WriteAim(w, turret.Gun?.Aim());
        }

        w.WriteVarUInt((uint)_mounted.Length);
        foreach (Igi2StationaryGunTask gun in _mounted)
        {
            WriteAim(w, gun.Aim());
        }

        return w.AsSpan().ToArray();
    }

    /// <summary>Stands this client's copies where the server's are.</summary>
    /// <param name="state">The bytes, as <see cref="Capture"/> wrote them.</param>
    /// <param name="leadTicks">The ticks this client's own player runs ahead of the snapshot.</param>
    /// <param name="flowTick">The level's tick.</param>
    public void Apply(byte[] state, int leadTicks, int flowTick)
    {
        ArgumentNullException.ThrowIfNull(state);
        BitReader r = new(state);
        uint cars = r.ReadVarUInt();
        for (int i = 0; i < cars && !r.IsOverflowed; i++)
        {
            Igi2CarPose pose = r.ReadBool()
                ? new Igi2CarPose(default, [], false, true, null)
                : new Igi2CarPose(Igi2NetPosition.Read(ref r), EngineEuler.Rotation(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), r.ReadBool(), false, ReadAim(ref r));
            if (!r.IsOverflowed && i < _cars.Length)
            {
                _cars[i].Hold(pose);
            }
        }

        uint trains = r.ReadVarUInt();
        for (int i = 0; i < trains && !r.IsOverflowed; i++)
        {
            Igi2TrainLeg leg = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (!r.IsOverflowed && i < _trains.Length)
            {
                _trains[i].Hold(leg, leadTicks, flowTick);
            }
        }

        uint turrets = r.ReadVarUInt();
        for (int i = 0; i < turrets && !r.IsOverflowed; i++)
        {
            Igi2GunAim? aim = ReadAim(ref r);
            if (!r.IsOverflowed && i < _turrets.Length && _turrets[i].Gun is { } gun && aim is { } held)
            {
                gun.IsPuppet = true;
                gun.Hold(held);
            }
        }

        uint mounted = r.ReadVarUInt();
        for (int i = 0; i < mounted && !r.IsOverflowed; i++)
        {
            Igi2GunAim? aim = ReadAim(ref r);
            if (!r.IsOverflowed && i < _mounted.Length && aim is { } held)
            {
                _mounted[i].Hold(held);
            }
        }
    }

    /// <summary>Makes every vehicle and gun of this client's level the server's puppet, before any snapshot is taken.</summary>
    public void Puppet()
    {
        foreach (Igi2CarTask car in _cars)
        {
            car.IsPuppet = true;
            car.Gun?.IsPuppet = true;
        }

        foreach (Igi2TrainTask train in _trains)
        {
            foreach (Igi2TrainTask car in train.Rake)
            {
                car.IsPuppet = true;
            }
        }

        foreach (Igi2AiStationaryGunHolderTask turret in _turrets)
        {
            turret.Gun?.IsPuppet = true;
        }

        foreach (Igi2StationaryGunTask gun in _mounted)
        {
            gun.IsPuppet = true;
            gun.Firing?.IsReplica = true;
        }
    }

    private static void WriteAim(BitWriter w, Igi2GunAim? aim)
    {
        w.WriteBool(aim is not null);
        if (aim is { } a)
        {
            w.WriteSingle(a.Tilt);
            w.WriteSingle(a.Turn);
            w.WriteBits(a.Shots, 8);
        }
    }

    private static Igi2GunAim? ReadAim(ref BitReader r) =>
        r.ReadBool() ? new Igi2GunAim(r.ReadSingle(), r.ReadSingle(), (byte)r.ReadBits(8)) : null;
}
