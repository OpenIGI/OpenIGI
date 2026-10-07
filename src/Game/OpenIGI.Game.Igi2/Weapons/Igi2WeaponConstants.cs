using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>The script constants the weapon scripts are written against, with the values the engine registers.</summary>
/// <remarks>
/// <para>
/// Every value is read from the call that registers it in <c>igi2.exe</c>: <c>WeaponType_Open</c>
/// (<c>0x436C60</c>) for the categories, the weapon types, the sights and the ammo displays, <c>AmmoType_Open</c>
/// (<c>0x43B080</c>) for the undefined ammo id, and <c>HumanAI</c>'s registration (<c>0x45CC42</c>–<c>0x45CC99</c>) for the
/// gunshot hearing ranges the weapons name. Several names share a value on purpose: the explosive, RPG and other
/// categories are all registered as zero, as are the crosshair and circle sights.
/// </para>
/// <para>
/// A <c>TASKTYPE_*</c> constant is the id of the task type it names, which is what the engine compares it with when
/// it makes a weapon's fire types. Here that is the id this session's registry gives the type; retail's own ids
/// are its own registration order and nothing in a script depends on their numbers. <c>TASKTYPE_NONE</c> is the
/// exception, a fixed <c>512</c> beyond every type.
/// </para>
/// </remarks>
public static class Igi2WeaponConstants
{
    /// <summary>The value of <c>TASKTYPE_NONE</c>: no fire type.</summary>
    public const int NoTaskType = 512;

    /// <summary>The value of <c>AMMO_ID_UNDEFINED</c> and <c>WEAPON_ID_UNDEFINED</c>.</summary>
    public const int Undefined = -1;

    private static readonly (string Name, int Value)[] Values =
    [
        ("WEAPONCATEGORY_NONE", 0),
        ("WEAPONCATEGORY_KNIFE", 1),
        ("WEAPONCATEGORY_SIDEARM", 2),
        ("WEAPONCATEGORY_PRIMARY", 3),
        ("WEAPONCATEGORY_GRENADE", 4),
        ("WEAPONCATEGORY_PROXIMITYMINE", 5),
        ("WEAPONCATEGORY_FLASHBANG", 6),
        ("WEAPONCATEGORY_SMOKEGRENADE", 7),
        ("WEAPONCATEGORY_MEDIPACK", 8),
        ("WEAPONCATEGORY_C4", 9),
        ("WEAPONCATEGORY_LASERDESIGNATOR", 10),
        ("WEAPONCATEGORY_THERMOCHEMICAL", Igi2WeaponCategory.ThermoChemical),
        ("WEAPONCATEGORY_BINOCULARS", Igi2WeaponCategory.Binoculars),
        ("WEAPONCATEGORY_EXPLOSIVE", 0),
        ("WEAPONCATEGORY_RPG", 0),
        ("WEAPONCATEGORY_OTHER", 0),
        ("WEAPONTYPE_GUN", 0),
        ("WEAPONTYPE_PISTOL", 1),
        ("WEAPONTYPE_SHOTGUN", 2),
        ("WEAPONTYPE_KNIFE", 3),
        ("WEAPONTYPE_GRENADE", Igi2WeaponTypeKind.Grenade),
        ("WEAPONTYPE_BINOCULAR", 5),
        ("WEAPONTYPE_MEDIPACK", 6),
        ("WEAPONTYPE_PROXIMITYMINE", Igi2WeaponTypeKind.ProximityMine),
        ("WEAPONTYPE_C4BOMB", 8),
        ("WEAPONTYPE_SPECIAL", 9),
        ("SIGHTDISPLAYTYPE_NONE", 0),
        ("SIGHTDISPLAYTYPE_PISTOL", 1),
        ("SIGHTDISPLAYTYPE_SHOTGUN", 2),
        ("SIGHTDISPLAYTYPE_SMG", 3),
        ("SIGHTDISPLAYTYPE_ASSAULTRIFLE", 4),
        ("SIGHTDISPLAYTYPE_MINIMI", 5),
        ("SIGHTDISPLAYTYPE_RPG", 6),
        ("SIGHTDISPLAYTYPE_GRENADE", 7),
        ("SIGHTDISPLAYTYPE_SNIPERRIFLE", 8),
        ("SIGHTDISPLAYTYPE_CROSSHAIR", 0),
        ("SIGHTDISPLAYTYPE_CIRCLE", 0),
        ("AMMODISPLAYTYPE_NONE", 0),
        ("AMMODISPLAYTYPE_NORMAL", 1),
        ("AMMODISPLAYTYPE_CLIP", 2),
        ("AMMODISPLAYTYPE_BARREL", 3),
        ("AMMODISPLAYTYPE_SHELL", 4),
        ("AMMODISPLAYTYPE_GRENADE", 5),
        ("AMMO_ID_UNDEFINED", Undefined),
        ("WEAPON_ID_UNDEFINED", Undefined),
        ("TASKTYPE_NONE", NoTaskType),
        ("HUMANAI_DETECTIONEVENT_GUNSHOT_SILENCED_RANGE", 20480),
        ("HUMANAI_DETECTIONEVENT_GUNSHOT_PISTOL_RANGE", 204800),
        ("HUMANAI_DETECTIONEVENT_GUNSHOT_RANGE", 409600),
        ("HUMANAI_DETECTIONEVENT_CANNON_RANGE", 409600),
    ];

    /// <summary>The task types a weapon's fire types and projectiles are named by, as <c>TASKTYPE_*</c> constant and type name.</summary>
    private static readonly (string Constant, string Type)[] TaskTypes =
    [
        ("TASKTYPE_GUN", Igi2GunTask.TypeName),
        ("TASKTYPE_KNIFE", Igi2KnifeTask.TypeName),
        ("TASKTYPE_ZOOM", Igi2ZoomTask.TypeName),
        ("TASKTYPE_FIREMODE", Igi2FireModeTask.TypeName),
        ("TASKTYPE_PULLPIN", Igi2PullPinTask.TypeName),
        ("TASKTYPE_PROJECTILELAUNCHER", Igi2ProjectileLauncherTask.TypeName),
        ("TASKTYPE_C4BOMB", Igi2C4BombTask.TypeName),
        ("TASKTYPE_PROXIMITYMINE", Igi2ProximityMineTask.TypeName),
        ("TASKTYPE_REMOTEMINE", Igi2RemoteMineTask.TypeName),
        ("TASKTYPE_MEDIPACK", Igi2MedipackTask.TypeName),
        ("TASKTYPE_LASERDESIGNATOR", Igi2LaserDesignatorTask.TypeName),
        ("TASKTYPE_LASERDOTSWITCH", Igi2LaserDotSwitchTask.TypeName),
        ("TASKTYPE_GUNSHOT", "GunShot"),
        ("TASKTYPE_GRENADE", "Grenade"),
        ("TASKTYPE_IMPACTGRENADE", "ImpactGrenade"),
        ("TASKTYPE_MISSILE", "Missile"),
        ("TASKTYPE_ZOOMOVERLAY", "ZoomOverlay"),
        ("TASKTYPE_SNIPEROVERLAY", "SniperOverlay"),
        ("TASKTYPE_AUGSNIPEROVERLAY", "AugSniperOverlay"),
        ("TASKTYPE_DRAGUNOVSNIPEROVERLAY", "DragunovSniperOverlay"),
        ("TASKTYPE_G11SNIPEROVERLAY", "G11SniperOverlay"),
        ("TASKTYPE_G36SNIPEROVERLAY", "G36SniperOverlay"),
        ("TASKTYPE_PSG1SNIPEROVERLAY", "Psg1SniperOverlay"),
        ("TASKTYPE_BINOCULAROVERLAY", "BinocularOverlay"),
        ("TASKTYPE_LASERDESIGNATOROVERLAY", "LaserDesignatorOverlay"),
    ];

    /// <summary>Registers the constants, the task-type ones after the types they name.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="types">The task-type registry; a named type no class implements yet is declared with no parameters.</param>
    public static void Register(NativeRegistry natives, TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(types);

        foreach ((string name, int value) in Values)
        {
            natives.RegisterConstant(name, value);
        }

        foreach ((string constant, string type) in TaskTypes)
        {
            TaskType registered = types.TryGet(type, out TaskType? known) ? known : types.Declare(type, []);
            natives.RegisterConstant(constant, registered.Id);
        }
    }
}
