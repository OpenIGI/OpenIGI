using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Runs the destruction of dying cars for <see cref="DestructibleObjectRegistry"/>: the blasts each one
/// sets off, their fuses, and the wreck countdown that finally takes the car out of the world.
/// </summary>
internal sealed class DestructibleVehicleBlasts
{
    private readonly List<DestructibleObject> _objects;
    private readonly IgiRandom _random;
    private readonly List<VehicleBlast> _pendingVehicleBlasts = [];
    private readonly List<VehicleBlast> _readyVehicleBlasts = [];
    private readonly List<DestructibleObject> _explodedVehicles = [];

    /// <summary>Creates the car destruction state over the registry's objects.</summary>
    /// <param name="objects">Every destructible of the level, whose children go with a car.</param>
    /// <param name="random">The engine's global stream, which rolls each explosion point's delay.</param>
    internal DestructibleVehicleBlasts(List<DestructibleObject> objects, IgiRandom random)
    {
        _objects = objects;
        _random = random;
    }

    /// <summary>Returns the blasts dying cars set off that are due this tick.</summary>
    /// <remarks>
    /// Each is an <c>Explosion0</c> of its own: the caller applies it to the world exactly as it does any
    /// other, handing it no source record — so no car blast can set another car off.
    /// </remarks>
    internal IReadOnlyList<VehicleBlast> ConsumeVehicleBlasts()
    {
        if (_readyVehicleBlasts.Count == 0)
        {
            return [];
        }

        VehicleBlast[] ready = [.. _readyVehicleBlasts];
        _readyVehicleBlasts.Clear();
        return ready;
    }

    /// <summary>Returns the cars whose countdown ran out this tick and left the world.</summary>
    internal IReadOnlyList<DestructibleObject> ConsumeExplodedVehicles()
    {
        if (_explodedVehicles.Count == 0)
        {
            return [];
        }

        DestructibleObject[] exploded = [.. _explodedVehicles];
        _explodedVehicles.Clear();
        return exploded;
    }

    /// <summary>One tick of a dying car's destruction, <c>0x42E5A0</c>.</summary>
    /// <remarks>
    /// The first tick sets the blasts off and starts a countdown — ten ticks after a single blast, thirty
    /// after one per explosion point. When it runs out the car spawns its wreck (<c>0x4F3390</c>, nothing
    /// when the record names none), deletes its children (<c>0x401780</c>), gives up its world object
    /// (<c>0x4F4820</c>), is switched off (<c>0x42FC80</c>) and only then raises <c>isExploded</c>. The car
    /// tick skips all of it once <c>isExploded</c> is set.
    /// </remarks>
    internal void TickDyingVehicle(DestructibleObject car)
    {
        if (!car.IsDying || car.IsDestroyed)
        {
            return;
        }

        if (car.DestructionTicks == -1)
        {
            StartVehicleBlasts(car);
            car.DestructionTicks = car.ExplosionPoints.Count == 0
                ? DestructibleObjectRegistry.CarSingleBlastTicks
                : DestructibleObjectRegistry.CarPointBlastTicks;
            return;
        }

        int remaining = car.DestructionTicks--;
        if (remaining != 1)
        {
            return;
        }

        car.IsDestroyed = true;
        _explodedVehicles.Add(car);

        // Its children go with it: whatever the model attached (the truck's windows) and the wheels.
        foreach (DestructibleObject child in _objects)
        {
            if (!ReferenceEquals(child, car) && DestructibleObjectRegistry.IsOwnedBy(child.Owner, car.Owner))
            {
                child.IsDestroyed = true;
            }
        }
    }

    /// <summary>The blasts <c>0x42E5A0</c> sets off on its first tick.</summary>
    /// <remarks>
    /// <para>
    /// With no explosion points, one <c>Explosion0</c> at the car's origin, sized from the car's own
    /// bound <c>r</c> (its largest scale times <c>MESH+0x78</c>): damage 1, radius <c>r/4</c>, falloff
    /// <c>r/20</c>, full range <c>r/200</c>, three fragments and one fireball, going off at once.
    /// </para>
    /// <para>
    /// With them, one per point at that magic vertex: damage 1, radius 12288, falloff and full range 3072,
    /// three fragments and two fireballs, each after its own <c>rand % 30</c> ticks. Every one plays
    /// <c>explo_03_l</c> and posts the 25 m explosion event, and hands over no source record.
    /// </para>
    /// <para>
    /// <c>0x480FE0</c> draws three values for the fireball's drift and <c>0x4810A0</c> three for its spin
    /// from the same global stream; they are drawn here too so that each point's delay is the one the
    /// original rolls.
    /// </para>
    /// </remarks>
    private void StartVehicleBlasts(DestructibleObject car)
    {
        if (car.ExplosionPoints.Count == 0)
        {
            float bound = car.ExplosionContactRadiusUnits;
            DrawExplosionSpin();
            _pendingVehicleBlasts.Add(new VehicleBlast(
                car,
                car.PositionUnits,
                DamageFactor: 1f,
                RadiusUnits: bound * 0.25f,
                FalloffUnits: bound * 0.05f,
                FullRangeUnits: bound * 0.005f,
                Fragments: 3,
                Fireballs: 1));
            return;
        }

        foreach (int point in car.ExplosionPoints)
        {
            int delay = (int)_random.NextBounded(30u);
            DrawExplosionSpin();
            _pendingVehicleBlasts.Add(new VehicleBlast(
                car,
                ExplosionPointPosition(car, point),
                DamageFactor: 1f,
                RadiusUnits: 12288f,
                FalloffUnits: 3072f,
                FullRangeUnits: 3072f,
                Fragments: 3,
                Fireballs: 2)
            {
                DelayTicks = delay,
            });
        }
    }

    private void DrawExplosionSpin()
    {
        for (int i = 0; i < 6; i++)
        {
            _ = _random.NextSingle();
        }
    }

    /// <summary>
    /// A magic vertex of the car's model in the world, <c>0x414E20</c>: the origin when the model has no
    /// such vertex.
    /// </summary>
    private static Vector3d ExplosionPointPosition(DestructibleObject car, int index)
    {
        if (car.Placement?.Model is not { } model || !model.TryGetMagicVertex(index, out Vector3f local))
        {
            return car.PositionUnits;
        }

        Vector3f offset = car.Orientation.Transposed().Transform(local);
        return car.PositionUnits + new Vector3d(offset.X, offset.Y, offset.Z);
    }

    /// <summary>
    /// Runs each pending car blast's fuse, <c>0x480F40</c>: a non-zero delay counts down, and the blast
    /// goes off on the tick it reaches nought — the tick it was set off, for no delay at all.
    /// </summary>
    internal void TickVehicleBlasts()
    {
        for (int i = 0; i < _pendingVehicleBlasts.Count; i++)
        {
            VehicleBlast blast = _pendingVehicleBlasts[i];
            if (blast.DelayTicks > 0)
            {
                blast.DelayTicks--;
            }

            if (blast.DelayTicks == 0)
            {
                _readyVehicleBlasts.Add(blast);
                _pendingVehicleBlasts.RemoveAt(i--);
            }
        }
    }
}
