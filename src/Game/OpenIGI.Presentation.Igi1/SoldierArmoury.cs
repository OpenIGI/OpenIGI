using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>What the soldiers, the stationary guns and the vehicles' guns are armed with, and the weapon models each draws.</summary>
/// <remarks>
/// A held Weapon task exists independently of the third-person mesh: combat reads the task, so a missing or
/// unrenderable model leaves a soldier armed and merely draws nothing in his hands.
/// </remarks>
/// <param name="groups">The groups the renderer draws; shared, not copied.</param>
/// <param name="sim">The soldiers' simulation, whose brains learn whether a grenade is carried.</param>
/// <param name="meshes">What builds the weapon models.</param>
/// <param name="logger">Where a weapon missing from the table, and every armed gunner, is reported.</param>
internal sealed class SoldierArmoury(
    List<SoldierGroup> groups,
    SoldierSimulation sim,
    SoldierMeshBuilder meshes,
    IOpenIgiLogger logger)
{
    private readonly List<SoldierGroup> _groups = groups;
    private readonly SoldierSimulation _sim = sim;
    private readonly SoldierMeshBuilder _meshes = meshes;
    private readonly IOpenIgiLogger _logger = logger;

    private readonly Dictionary<string, SoldierHeldWeapon> _heldWeaponsByModel = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<AiStationaryGun, SoldierHeldWeapon?> _stationaryWeaponVisuals = [];

    // The weapon models the vehicles' guns carry, by model, and where the runtime poses each gun (0x470050).
    private readonly Dictionary<string, SoldierHeldWeapon?> _vehicleWeaponVisuals = new(StringComparer.OrdinalIgnoreCase);
    private Func<IEnumerable<VehicleGunVisual>>? _vehicleGuns;
    private readonly HashSet<int> _reportedStationaryVisuals = [];

    /// <summary>Gets the weapon model each stationary gun is drawn as, or <see langword="null"/> for one with none.</summary>
    public Dictionary<AiStationaryGun, SoldierHeldWeapon?> StationaryWeaponVisuals => _stationaryWeaponVisuals;

    /// <summary>Gets where the runtime poses each vehicle's gun, or <see langword="null"/> when no vehicle carries one.</summary>
    public Func<IEnumerable<VehicleGunVisual>>? VehicleGuns => _vehicleGuns;

    /// <summary>Finds the weapon model a vehicle's gun carries.</summary>
    /// <param name="model">The weapon model's name.</param>
    /// <param name="visual">Receives the model, or <see langword="null"/> where it did not build.</param>
    /// <returns><see langword="true"/> when the model was asked for.</returns>
    public bool TryGetVehicleWeapon(string model, out SoldierHeldWeapon? visual) =>
        _vehicleWeaponVisuals.TryGetValue(model, out visual);

    /// <summary>Finds the weapon model a soldier armed with a gun model holds.</summary>
    /// <param name="gunModel">The weapon's gun model.</param>
    /// <param name="weapon">Receives the model, or <see langword="null"/> where no soldier holds one.</param>
    /// <returns><see langword="true"/> when some soldier holds it.</returns>
    public bool TryGetHeldWeapon(string gunModel, out SoldierHeldWeapon? weapon) =>
        _heldWeaponsByModel.TryGetValue(gunModel, out weapon);

    /// <summary>Notes that a stationary gun's visual has been drawn, so it is reported only once.</summary>
    /// <param name="taskId">The gun's task.</param>
    /// <returns><see langword="true"/> the first time.</returns>
    public bool FirstDrawOf(int taskId) => _reportedStationaryVisuals.Add(taskId);

    /// <summary>
    /// Builds the weapon models the soldiers and the stationary guns are drawn holding, once the simulation has armed them
    /// (<see cref="SoldierSimulation.Arm"/>).
    /// </summary>
    /// <param name="mission">The mission whose packs hold the weapon models.</param>
    /// <param name="count">How many soldiers there are, for the report.</param>
    /// <returns>How many soldiers hold a weapon model.</returns>
    public int Dress(LoadedMission mission, int count)
    {
        _heldWeaponsByModel.Clear();

        Dictionary<string, SoldierHeldWeapon?> cache = new(StringComparer.OrdinalIgnoreCase);
        _stationaryWeaponVisuals.Clear();
        _vehicleWeaponVisuals.Clear();
        _vehicleGuns = null;
        _reportedStationaryVisuals.Clear();
        if (_sim.Interactions is { } interactions)
        {
            foreach (AiStationaryGun gun in interactions.Items
                .Select(static item => item.StationaryGun)
                .OfType<AiStationaryGun>())
            {
                if (gun.Weapon is not { } weapon)
                {
                    _stationaryWeaponVisuals[gun] = null;
                    continue;
                }

                SoldierHeldWeapon? held = Build(weapon.GunModel);
                _stationaryWeaponVisuals[gun] = held;
                _logger.Information(
                    SoldierRenderer.LogCategory + ".Gunner",
                    $"task={gun.TaskId} weapon={weapon.ScriptId} model={weapon.GunModel} "
                        + $"position=({gun.Position.X:0.##},{gun.Position.Y:0.##},{gun.Position.Z:0.##}) "
                        + $"visual={(held is null ? "missing" : "ready")}");
            }
        }

        int armed = 0;
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (member.Agent.WeaponType is not { } weapon || Build(weapon.GunModel) is not { } held)
                {
                    continue;
                }

                member.Weapon = held;
                _heldWeaponsByModel[weapon.GunModel] = held;
                member.CarriedWeapon = held;
                armed++;
            }
        }

        _logger.Information(
            SoldierRenderer.LogCategory,
            $"{armed} of {count} soldier(s) hold a weapon model, over {cache.Values.Count(w => w is not null)} " +
            $"distinct model(s).");
        return armed;

        SoldierHeldWeapon? Build(string gunModel)
        {
            if (!cache.TryGetValue(gunModel, out SoldierHeldWeapon? held))
            {
                held = _meshes.BuildWeapon(mission, gunModel);
                cache[gunModel] = held;
            }

            return held;
        }
    }

    /// <summary>Builds the weapon models the vehicles' guns carry, and remembers where the runtime poses each.</summary>
    /// <param name="mission">The mission whose packs hold the models.</param>
    /// <param name="weaponModels">The models the vehicles' guns carry.</param>
    /// <param name="guns">Where each gun is posed, asked every frame.</param>
    public void ArmVehicleGuns(
        LoadedMission mission,
        IEnumerable<string> weaponModels,
        Func<IEnumerable<VehicleGunVisual>> guns)
    {
        _vehicleWeaponVisuals.Clear();
        foreach (string model in weaponModels)
        {
            if (!_vehicleWeaponVisuals.ContainsKey(model))
            {
                _vehicleWeaponVisuals[model] = _meshes.BuildWeapon(mission, model);
            }
        }

        _vehicleGuns = _vehicleWeaponVisuals.Count > 0 ? guns : null;
    }

    /// <summary>Forgets the soldiers' and the vehicles' weapon models, which the mesh builder has released.</summary>
    public void Release()
    {
        _heldWeaponsByModel.Clear();
        _vehicleWeaponVisuals.Clear();
        _vehicleGuns = null;
    }
}
