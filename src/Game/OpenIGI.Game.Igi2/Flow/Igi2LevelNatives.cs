using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The natives the level's scripts reach the level's own objects through: the player and the humans (humanplayer.c, human.c), the
/// areas (areaactivateqtask.c) and the level flow (levelflow.c), registered for the session and bound to each level as it is stood.
/// </summary>
public sealed class Igi2LevelNatives
{
    /// <summary>The weapon a player with no primary weapon is taken to hold.</summary>
    public const string KnifeId = "WEAPON_ID_KNIFE";

    /// <summary>The primary weapon category, <c>WEAPONCATEGORY_PRIMARY</c>.</summary>
    public const int PrimaryCategory = 3;

    /// <summary>Gets the player the <c>HumanPlayer_*</c> natives address, the roster's first, or <see langword="null"/> between levels.</summary>
    public Igi2HumanPlayer? Player => Players.Primary;

    /// <summary>Gets the players of the level running; the original's one player is a roster of one.</summary>
    public PlayerRoster<Igi2HumanPlayer> Players { get; } = new();

    private Func<int, object?> _taskById = static _ => null;

    /// <summary>Gets the laser designator's module state.</summary>
    public Igi2LaserDesignatorState LaserDesignator { get; } = new();

    /// <summary>Gets the level flow of the level running, or <see langword="null"/>.</summary>
    public Igi2LevelFlowTask? LevelFlow { get; private set; }

    /// <summary>Binds the natives to a level.</summary>
    /// <param name="player">The player.</param>
    /// <param name="taskById">The level's tasks by id, <c>Level_atLevelQTaskID</c>, each as the object it runs as.</param>
    /// <param name="levelFlow">The level's flow.</param>
    public void Bind(Igi2HumanPlayer? player, Func<int, object?> taskById, Igi2LevelFlowTask? levelFlow)
    {
        ArgumentNullException.ThrowIfNull(taskById);
        Players.Set(player);
        _taskById = taskById;
        LevelFlow = levelFlow;
        LaserDesignator.Reset();
        LaserDesignator.TaskById = taskById;
        LaserDesignator.LevelTick = () => LevelFlow?.Tick ?? 0;
    }

    /// <summary>Registers the natives for compiled scripts and script text alike.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    public void Register(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);

        // HumanPlayer_ActivateScriptFunction: a task by id that answers GetActivate is activated as though found.
        functions.Register(natives, "HumanPlayer_Activate", call =>
        {
            if (call.Count > 0 && (uint)call.GetInt32(0) < 0x1000u && _taskById(call.GetInt32(0)) is IIgi2Activatable target)
            {
                Player?.ScriptActivate(target);
            }

            return QvmValue.Zero;
        }, 1);

        // HumanPlayer_LockActivateScriptFunction.
        functions.Register(natives, "HumanPlayer_LockActivate", call =>
        {
            if (call.Count > 0)
            {
                Player?.LockActivate(call.GetInt32(0));
            }

            return QvmValue.Zero;
        }, 1);

        functions.Register(natives, "HumanPlayer_DisableAllWeapons", _ =>
        {
            Player?.DisableAllWeapons();
            return QvmValue.Zero;
        }, 0);

        // HumanPlayer_isDeadCB: one with no player or a dead one, as a real; of several, one when none is left alive.
        functions.Register(natives, "HumanPlayer_isDead", _ => QvmValue.FromDouble(Players.Nearest(static p => p.IsDead ? null : 0) is null ? 1.0 : 0.0), 1);

        functions.Register(natives, "HumanPlayer_GetPrimaryWeaponModel", _ => QvmValue.FromString(PrimaryWeapon()?.FixedModel ?? string.Empty), 0);
        functions.Register(natives, "HumanPlayer_GetPrimaryWeaponID", _ => QvmValue.FromString(PrimaryWeapon()?.WeaponId ?? string.Empty), 0);

        // AreaActivateQTask_IdInAreaCB: one when the second task, an object of the world, stands inside the first, an area.
        functions.Register(natives, "AreaActivate_IdInArea", call =>
        {
            bool inside = call.Count == 2
                && (uint)call.GetInt32(0) < 0x1000u
                && (uint)call.GetInt32(1) < 0x1000u
                && _taskById(call.GetInt32(0)) is Igi2AreaActivateTask area
                && PositionOf(_taskById(call.GetInt32(1))) is { } at
                && area.IsInside(at);
            return inside ? QvmValue.FromInt32(1) : QvmValue.Zero;
        }, 2);

        // LevelFlow_SetTimeOfDay: the clock set to an hour of the day, in ticks of thirty a second.
        functions.Register(natives, "LevelFlow_SetTimeOfDay", call =>
        {
            if (LevelFlow is { } flow && call.Count > 0)
            {
                flow.SetTimeOfDay(call.GetReal(0));
            }

            return QvmValue.Zero;
        }, 1);

        // LaserDesignator_GetTicksHeldOnTask: the ticks the dot has rested on the task, nought for any other.
        functions.Register(natives, "LaserDesignator_GetTicksHeldOnTask", call =>
        {
            object? task = call.Count > 0 && (uint)call.GetInt32(0) < 0x1000u ? _taskById(call.GetInt32(0)) : null;
            return call.Count > 0 && ReferenceEquals(task, LaserDesignator.HitTask)
                ? QvmValue.FromInt32((LevelFlow?.Tick ?? 0) - LaserDesignator.TickFirstHit)
                : QvmValue.Zero;
        }, 1);

        // LaserDesignatorOverlay_SetLockedOnStatus.
        functions.Register(natives, "LaserDesignatorOverlay_SetLockedOnStatus", call =>
        {
            if (call.Count == 1)
            {
                LaserDesignator.IsLockedOn = call.GetInt32(0) != 0;
            }

            return QvmValue.FromInt32(1);
        }, 1);

        // Human_PopPositionScriptFunction: a human by id is put at a spot in metres; the player goes back to its default states.
        functions.Register(natives, "Human_PopPosition", call =>
        {
            if (call.Count == 4 && (uint)call.GetInt32(0) < 0x1000u && _taskById(call.GetInt32(0)) is Igi2Human human)
            {
                Vector3d at = new(call.GetReal(1) * 4096.0, call.GetReal(2) * 4096.0, call.GetReal(3) * 4096.0);
                human.PopPositionDroppingGround(at);
                if (ReferenceEquals(human, Player))
                {
                    Player.GoToDefaultState();
                }
            }

            return QvmValue.Zero;
        }, 4);
    }

    /// <summary>Where a task of the level stands, as an object of the world: a human's feet or a placed task's position.</summary>
    private static Vector3d? PositionOf(object? task) => task switch
    {
        Igi2Human human => human.Position,
        GameTask placed when placed.TryGetScriptValue("Position", out object? value) && value is Vector3d position => position,
        _ => null,
    };

    /// <summary>
    /// The player's primary weapon, <c>HumanPlayer_GetPrimaryWeaponType</c>: the first weapon carried in the primary category, in the
    /// inventory's order; with none, or no player, the knife.
    /// </summary>
    /// <returns>The weapon type, or <see langword="null"/> when there is no knife either.</returns>
    public Igi2WeaponTypeTask? PrimaryWeapon()
    {
        Igi2Inventory? inventory = (Player?.Weapon as Igi2PlayerWeapons)?.Inventory;
        if (inventory?.Weapons.FirstOrDefault(static w => w.IsActive && w.WeaponType.WeaponCategory == PrimaryCategory) is { } primary)
        {
            return primary.WeaponType;
        }

        return inventory?.FindWeapon(KnifeId)?.WeaponType;
    }
}
