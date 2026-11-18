using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The server's running state of the level, taken into this client's copy.</content>
public sealed partial class Igi2ClientWorld
{
    private ScriptVariableMirror? _variables;
    private Pickups.Igi2GunPickupTask[] _guns = [];
    private readonly Igi2BlastLog _blasts = new();

    // The guns the server lays that the level did not place.
    private readonly Igi2DroppedGuns _dropped = new();

    // The mines the server lays.
    private readonly Igi2LaidMines _mines = new();
    private int? _ownEntity;
    private readonly Igi2FiredLog _fired = new();
    private readonly Igi2VehicleStates _vehicles;

    // The replica weapon of another body here, by its entity: a soldier's, or another player's.
    private Weapons.Igi2WeaponFiring? ReplicaWeaponOf(int entity) =>
        Igi2ReplicatedWorld.SoldierIndex(entity) is { } index
            ? _level.Soldiers is { } soldiers && (uint)index < (uint)soldiers.Bodies.Count ? soldiers.Bodies[index].Weapon.Firing : null
            : _vehicles.WeaponOf(entity) ?? _puppets.GetValueOrDefault(entity)?.Weapons?.Firing;

    /// <inheritdoc/>
    /// <remarks>The server's cutscene key comes with it: a client's scenes end when its host skips them, and never on their own.</remarks>
    public void ApplyLevel(byte[] state) =>
        _level.FollowedBreakKey = Igi2LevelState.Apply(_guns, _variables, state, _blasts, _ownEntity, _level.Explosions, (_dropped, _level.Human.Pickups), (_mines, _level.Mines, _level.Weapons?.Inventory.Tables));

    // The level's verdicts and counters are the server's from now on (LevelRun.IsReplica): this client's scripts read the
    // server's variables, and its flow never decides the mission on its own.
    private void BindLevelState()
    {
        _variables = Igi2LevelState.MirrorOf(_level);
        _guns = Igi2LevelState.LaidOutGuns(_level);
        _level.Level?.IsReplica = true;

        // Only the server's guns are laid here: a guard's copy dying, or this player's swap, throws nothing down of its own.
        _level.Human.Pickups?.IsReplica = true;

        // And only the server's mines, lying still: this player's own laying lays nothing of its own here.
        _level.Mines?.IsReplica = true;
    }
}
