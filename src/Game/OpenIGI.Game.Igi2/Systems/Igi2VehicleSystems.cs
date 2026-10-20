using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>What rides and what fires on its own: trains, mounted and automatic guns, and cars.</summary>
internal static class Igi2VehicleSystems
{
    /// <summary>
    /// Trains are measured and put on the splines they ride, every carriage moving and drawn moving; then the AnimTasks that drive
    /// them set them back to their start (<c>Train_UpdateInternalDataAfterLoadHandler</c>, <c>AnimQTask_UpdateInternalDataHandler</c>).
    /// </summary>
    public static void Trains(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        foreach (Vehicles.Igi2TrainTask train in b.Root.Descendants().OfType<Vehicles.Igi2TrainTask>().Where(static t => !t.IsCarriage))
        {
            GameTask? rails = train.RailroadId < 0
                ? null
                : b.Root.Descendants().FirstOrDefault(t => t.Id == train.RailroadId && t.Type.Name == World.Igi2SplineObject.SplineType);
            train.Prepare(b.Models, rails is null ? null : World.Igi2SplineObject.Read(rails).Path);
            foreach (Vehicles.Igi2TrainTask car in train.Rake)
            {
                car.Sounds = player.Sounds;
                car.Magic = b.Magic;
                car.Bind(player.CollisionWorld.SolidsOf(Vehicles.Igi2TrainTask.TypeName, car.Id));
                b.Movers.Add(car);
            }
        }
    }

    /// <summary>Mounted guns are armed with weapons of their own and turn on their mounts; the player and the AI's soldiers work them.</summary>
    public static void StationaryGuns(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        if (b.Services.TryGet(out Igi2HumanWeapons? gunWeapons))
        {
            Igi2GameMaterials? gunMaterials = b.Services.TryGet(out Igi2GameMaterials? found) ? found : null;
            foreach (Igi2StationaryGunTask gun in b.Root.Descendants().OfType<Igi2StationaryGunTask>())
            {
                gun.Magic = b.Magic;
                gun.Random = player.Human.Random;
                gun.Prepare(gunWeapons, b.Models);
                gun.Bind(player.CollisionWorld.SolidsOf(Igi2StationaryGunTask.TypeName, gun.Id));
                if (gun.Firing is { } firing)
                {
                    Arm(b, firing, () => gun.Mount, gunMaterials);
                }

                b.Activatables.Add(gun);
                b.Movers.Add(gun);
                b.Guns.Add(gun);
            }
        }

        player.StationaryGuns = b.Guns;
    }

    /// <summary>Automatic guns watch from their muzzles and turn their heads and barrels towards what they see.</summary>
    public static void Turrets(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        if (b.Services.TryGet(out Igi2HumanWeapons? turretWeapons))
        {
            Igi2GameMaterials? turretMaterials = b.Services.TryGet(out Igi2GameMaterials? found) ? found : null;
            Detection.Igi2DetectionWorld? detection = b.SessionAi?.Detection;
            foreach (Igi2AiStationaryGunHolderTask turret in b.Root.Descendants().OfType<Igi2AiStationaryGunHolderTask>())
            {
                turret.Prepare(turretWeapons, b.Models, b.Magic, detection, player.Sounds, player.Human.Random);
                if (turret.Gun is { } gun)
                {
                    Arm(b, gun.Firing, () => gun.Muzzle, turretMaterials);
                }

                b.Turrets.Add(turret);
            }
        }

        player.Turrets = b.Turrets;
    }

    /// <summary>
    /// Cars are physics objects of the types their models name, on sprung wheels, with their guns on their attach points; they move,
    /// and are drawn moving, and blasts reach them (<c>Car_UpdateInternalDataHandler</c>).
    /// </summary>
    public static void Cars(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        if (b.Services.TryGet(out OpenIGI.Game.Physics.PhysicsObjectTypes? carTypes)
            && b.Services.TryGet(out Igi2GameMaterials? carMaterials))
        {
            b.Services.TryGet(out Igi2HumanWeapons? carWeapons);
            Detection.Igi2DetectionWorld? carDetection = b.SessionAi?.Detection;
            Physics.Igi2PhysicsWorld roads = new(player.CollisionWorld, carMaterials, static owner => !Igi2ObjectCollision.PassesThrough(owner));
            foreach (Vehicles.Igi2CarTask car in b.Root.Descendants().OfType<Vehicles.Igi2CarTask>())
            {
                car.Magic = b.Magic;
                car.Sounds = player.Sounds;
                car.Explosions = player.Explosions;
                car.Bind(player.CollisionWorld.SolidsOf(Vehicles.Igi2CarTask.TypeName, car.Id));
                car.Prepare(carTypes, b.Models, roads, carWeapons, carDetection, player.Human.Random);
                if (car.Gun is { } gun)
                {
                    Arm(b, gun.Firing, () => gun.Muzzle, carMaterials);
                }

                player.Explosions?.AddTarget(car);
                b.Movers.Add(car);
                b.Cars.Add(car);
            }
        }

        player.Cars = b.Cars;
    }

    /// <summary>The AI finds a mounted gun by its id.</summary>
    public static void StationaryGunLookup(Igi2LevelBuild b)
    {
        if (b.SessionAi is { } gunAi)
        {
            Dictionary<int, Igi2StationaryGunTask> gunsById = b.Guns.Where(static g => g.Id >= 0).ToDictionary(static g => g.Id);
            gunAi.StationaryGun = id => gunsById.GetValueOrDefault(id);
        }
    }

    // A level gun's firing: its sounds and flashes from where it fires, its impacts, and the world its rounds are traced through
    // when the session knows the materials.
    private static void Arm(Igi2LevelBuild b, Igi2WeaponFiring firing, Func<Core.Numerics.Vector3d> from, Igi2GameMaterials? materials)
    {
        Igi2TitlePlayer player = b.Player;
        firing.Effects = new Igi2LevelWeaponEffects(player.Sounds, from, b.SessionAi);
        firing.ImpactEffects = new Igi2LevelImpactEffects(player.Sounds, b.SessionAi) { Explosions = player.Explosions };
        if (materials is not null)
        {
            firing.World = new Igi2LevelGunShotWorld(player.CollisionWorld, materials, b.SessionAi);
        }
    }
}
