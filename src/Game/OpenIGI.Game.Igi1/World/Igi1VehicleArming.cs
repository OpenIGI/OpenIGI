using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Gives every authored vehicle that can fire the guns its model carries, and aims them at the player.</summary>
/// <remarks>
/// <para>
/// A vehicle's guns are attachments of its model (<c>0x4F4800</c> spawns them through <c>0x4C4950</c>);
/// each <c>TASKTYPE_AISTATIONARYGUN</c> names its weapon by model (<c>0x413C00</c>). A gun mounted on
/// another gun -- a T80's coaxial M2HB on its turret -- sits at the sum of both attachment offsets.
/// </para>
/// <para>
/// Missile rows (T80, APC) launch into the vehicle missile simulation; gunshot rows (M2HB, MIL) go
/// through the soldiers' round cast at probability 1, the value <c>0x46F950</c>'s fire message carries.
/// </para>
/// </remarks>
/// <param name="mission">The mission's world, whose player the guns aim at and whose soldiers their rounds meet.</param>
/// <param name="host">What shows the guns and logs what was armed.</param>
/// <param name="loaded">The mission, whose packs hold the vehicles' models.</param>
/// <param name="roster">The weapon table.</param>
/// <param name="player">The player the guns aim at.</param>
/// <param name="runtime">The level's vehicles.</param>
/// <param name="magic">The level's magic objects, which name a model's attachments.</param>
internal sealed class Igi1VehicleArming(
    MissionWorld mission,
    IPlayerSetupHost host,
    LoadedMission loaded,
    WeaponTypeRegistry roster,
    PlayerCamera player,
    CutsceneRuntime runtime,
    MagicObjectRegistry magic)
{
    private const string LogCategory = "Ai";
    private const string ModelField = "Model";

    // The weapon models the armed guns carry, which their mount models do not include.
    private readonly List<string> _weaponModels = [];

    // The humans a gun holder's record is offered, filled again each time it asks.
    private readonly List<AiDetectable> _detectables = [];

    /// <summary>Arms the level's vehicles and gun holders, and wires what they aim at and what their shots do.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    public void Arm(LevelTask levelRoot)
    {
        int armed = 0;
        foreach (DeclaredTask vehicle in levelRoot.Descendants().OfType<DeclaredTask>())
        {
            ConfigureDeathZones(vehicle);
            if (GunModelOf(vehicle) is { } model && ArmGuns(vehicle, model))
            {
                armed++;
            }
        }

        if (armed == 0)
        {
            return;
        }

        ShowGuns();
        AimAtPlayer();
        runtime.Vehicles.GunHolderSubjects = Subjects;
        runtime.Vehicles.VehicleCombatLineOfSight = (owner, from, to) =>
            mission.Trace is null
            || SoldierSimulation.HasLineOfSight(mission.Trace, from, to, Igi1HumanAi.SightObstructionTolerance, owner);
        runtime.Vehicles.VehicleCombatShotFired = Fire;
        host.Logger.Information(LogCategory, $"{armed} armed vehicle(s) and gun holder(s).");
    }

    private static bool IsVehicle(DeclaredTask task) => task is CarTask or HeliTask;

    private static string? Text(DeclaredTask task, string field) =>
        task.TryGetValue(field, out object? raw) ? raw as string : null;

    // The model whose attachments are a task's guns, or null for one that carries none. A gun holder carries its guns as
    // attachments of its holder model, as a car does of its own. A helicopter's gun runs only while its AnimTask flies it;
    // CutsceneRuntime gates that.
    private static string? GunModelOf(DeclaredTask task)
    {
        if (task.Type.Name == "AIStationaryGunHolder" && Text(task, "Holder model") is { } holderModel)
        {
            return holderModel;
        }

        return IsVehicle(task) && !string.IsNullOrWhiteSpace(Text(task, "Can fire")) ? Text(task, ModelField) : null;
    }

    private void ConfigureDeathZones(DeclaredTask task)
    {
        if (IsVehicle(task) && Text(task, ModelField) is { } zoneModel)
        {
            runtime.Vehicles.ConfigureDeathZones(task, VehicleDeathZone.FromModel(zoneModel, ModelOf, magic));
        }
    }

    private bool ArmGuns(DeclaredTask carrier, string modelName)
    {
        IReadOnlyList<VehicleGun> guns = VehicleGun.FromModel(
            modelName,
            ModelOf,
            magic,
            roster,
            (attachment, weaponModel) => host.Logger.Warning(
                LogCategory,
                $"Vehicle gun {attachment} names weapon model {weaponModel}, which no weapon row uses."));
        if (guns.Count == 0)
        {
            return false;
        }

        runtime.Vehicles.ConfigureVehicleGuns(carrier, guns);
        _weaponModels.AddRange(guns.Select(static gun => gun.Mount.WeaponModel));
        host.Logger.Information(
            LogCategory,
            $"{carrier.Type.Name} {carrier.Id} ({modelName}) carries "
                + string.Join(", ", guns.Select(static gun => gun.Weapon.ScriptId)) + ".");
        return true;
    }

    private MefModel? ModelOf(string name) =>
        loaded.LevelPack.TryGetModel(name, out MefModel? mef) || loaded.CommonPack.TryGetModel(name, out mef)
            ? mef
            : null;

    // The guns turn their mounts and carry weapon models of their own, which the mount models do not include.
    private void ShowGuns()
    {
        if (host.Scene is { } armedScene)
        {
            runtime.Vehicles.BindVehicleGunPlacements(armedScene);
        }

        mission.SoldierPresenter?.ArmVehicleGuns(loaded, _weaponModels, runtime.Vehicles.VehicleGunVisuals);
    }

    private void AimAtPlayer()
    {
        // A vehicle gun aims at what its vehicle's vision holds: the human's model origin (Human+32),
        // about a metre over the feet standing and half that crouched -- not the eye.
        runtime.Vehicles.VehicleCombatTarget = () => LivingPlayerAt(Igi1HumanAi.HumanOriginAboveFeet);

        // A vehicle's detection sweep tests the point the human reports to every viewer -- the soldiers' mark over
        // the feet -- along the soldiers' sight ray (0x489B20), which passes its own body.
        runtime.Vehicles.VehicleSightTarget = () => LivingPlayerAt(Igi1HumanAi.DetectionMarkAboveFeet);
    }

    private bool PlayerLives => !mission.IsPlayerDead && !mission.PlayerLife.Health.IsDead;

    // The rig, or another game's player standing in its stead: where its feet are and whether it keeps low.
    private (Vector3d Feet, bool Low) PlayerPlace() => mission.PlayerStandIn is { } standIn
        ? (standIn.Feet, standIn.IsKeepingLow)
        : (player.Body.Position, player.Body.IsCrouched);

    private Vector3d? LivingPlayerAt(Func<bool, double> heightAboveFeet)
    {
        if (!PlayerLives)
        {
            return null;
        }

        (Vector3d feet, bool low) = PlayerPlace();
        return Above(feet, heightAboveFeet(low));
    }

    private static Vector3d Above(Vector3d feet, double height) => feet + new Vector3d(0d, 0d, height);

    // A holder's record is offered every human in the global list's order (0x489750), each described by itself
    // (0x45FE30): the player, the partners, then the soldiers.
    private List<AiDetectable> Subjects()
    {
        _detectables.Clear();
        int playerTeam = mission.Soldiers?.PlayerTeam ?? 0;
        (Vector3d feet, bool low) = PlayerPlace();
        _detectables.Add(HumanDetectable((object?)mission.PlayerStandIn ?? player, feet, low, playerTeam, PlayerLives));
        foreach (Multiplayer.Igi1NetworkPlayer partner in mission.NetworkPlayers)
        {
            _detectables.Add(HumanDetectable(
                partner, partner.Player.Body.Position, partner.Player.IsCrouched, playerTeam, alive: !partner.IsDead));
        }

        mission.Soldiers?.AddDetectables(_detectables);
        return _detectables;
    }

    // A human as it describes itself to a record: its mark over the feet, its origin, its side, whether it lives.
    private static AiDetectable HumanDetectable(object key, Vector3d feet, bool low, int team, bool alive) => new(
        key,
        Above(feet, Igi1HumanAi.DetectionMarkAboveFeet(low)),
        Above(feet, Igi1HumanAi.HumanOriginAboveFeet(low)),
        team,
        alive);

    private void Fire(VehicleCombatShot shot)
    {
        if (shot.IsHitscan)
        {
            FireRound(shot);
        }
        else
        {
            LaunchMissile(shot);
        }
    }

    private void FireRound(VehicleCombatShot shot)
    {
        if (!roster.TryGetByScriptId(shot.WeaponScriptId, out WeaponType gunshot))
        {
            return;
        }

        mission.VehicleRoundOwner = shot.Owner;
        try
        {
            mission.Soldiers?.RoundHandler?.Invoke(shot.Origin, shot.Target, 1f, gunshot, null);
        }
        finally
        {
            mission.VehicleRoundOwner = null;
        }
    }

    // The report is the gun's, on the hold edges Gun_Fire plays it on (VehicleSoundPlayer); the missile
    // task makes no launch sound of its own.
    private void LaunchMissile(VehicleCombatShot shot) =>
        mission.VehicleMissiles.Spawn(
            shot.Origin,
            shot.Target,
            shot.MissileType,
            shot.MuzzleVelocityMetresPerSecond,
            shot.Direction,
            shot.ModelName,
            collisionOwner: shot.Owner,
            sourceWeaponId: roster.TryGetByScriptId(shot.WeaponScriptId, out WeaponType launcher) ? launcher.WeaponId : 0);
}
