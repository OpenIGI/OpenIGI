using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Physics;

/// <summary>What I.G.I. 2's <c>physicsobj</c> scripts name, for the engine's physics object types (<see cref="PhysicsObjectTypes"/>).</summary>
public static class Igi2PhysicsObjectTypes
{
    /// <summary>The task types the scripts name, as constant and type name.</summary>
    public static readonly IReadOnlyList<(string Constant, string Type)> TaskTypes =
    [
        ("TASKTYPE_GENERICPHYSICSOBJ", "GenericPhysicsObj"),
        ("TASKTYPE_GENERICPHYSICSMAGICOBJ", "GenericPhysicsMagicObj"),
        ("TASKTYPE_GRENADE", "Grenade"),
        ("TASKTYPE_CAR", "Car"),
        ("TASKTYPE_WHEEL", "Wheel"),
        ("TASKTYPE_HELI", "Heli"),
        ("TASKTYPE_ROTOR", "Rotor"),
        ("TASKTYPE_MISSILE", "Missile"),
        ("TASKTYPE_PLANE", "Plane"),
    ];

    /// <summary>
    /// The constants the type-specific arguments name: the grenade's kinds (<c>Grenade_Open</c>, <c>igi2 0x485390</c>) and
    /// the missile's smoke and guidance (<c>Missile_Open</c>, <c>0x52F5B0</c>).
    /// </summary>
    public static readonly IReadOnlyList<(string Name, int Value)> Constants =
    [
        ("GRENADE_EXPLOSIONTYPE_NORMAL", Igi2GrenadeKind.Normal),
        ("GRENADE_EXPLOSIONTYPE_FLASHBANG", Igi2GrenadeKind.Flashbang),
        ("GRENADE_EXPLOSIONTYPE_SMOKE", Igi2GrenadeKind.Smoke),
        ("MISSILE_SMOKE_NONE", 0),
        ("MISSILE_SMOKE_NORMAL", 1),
        ("MISSILE_TYPE_MISSILE_DIRECT", 0),
        ("MISSILE_TYPE_HIND_HELICOPTER", 1),
        ("MISSILE_TYPE_MISSILE_BALLISTIC", 2),
        ("MISSILE_TYPE_MISSILE_ANTITANK", 3),
        ("MISSILE_TYPE_MISSILE_KEYBOARD", 4),
        ("MISSILE_TYPE_BOMB", 5),
    ];

    /// <summary>The engine's reader of I.G.I. 2's types.</summary>
    /// <param name="logger">Where the load reports.</param>
    /// <returns>The reader.</returns>
    public static PhysicsObjectTypes Create(IOpenIgiLogger? logger = null) => new(TaskTypes, Constants, logger);
}
