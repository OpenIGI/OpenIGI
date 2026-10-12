using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Config;

/// <summary>
/// The script constants the game's modules register at start-up beside their natives: the game modes (game.c), the menu
/// manager's requests (menumanager.c), the graphics options (gfxoptions.c), the teams (human.c <c>Human_apzTeamStrings</c>) and
/// the column view's data types (columnviewcolumn.c).
/// </summary>
public static class Igi2GameConstants
{
    /// <summary>Gets the constants, as name and value.</summary>
    public static IReadOnlyList<(string Name, int Value)> All { get; } =
    [
        ("GAME_GAMEMODE_SINGLEPLAYER", 0),
        ("GAME_GAMEMODE_MULTIPLAYER", 1),
        ("MENUMANAGER_IDLE", 10),
        ("MENUMANAGER_QUIT", 11),
        ("MENUMANAGER_QUITTOMAINMENU", 12),
        ("MENUMANAGER_STARTGAME", 14),
        ("MENUMANAGER_JOINGAME", 15),
        ("DIRECT3DRENDER_FILTERINGTYPE_BILINEAR", 0),
        ("DIRECT3DRENDER_FILTERINGTYPE_TRILINEAR", 1),
        ("DIRECT3DRENDER_FILTERINGTYPE_ANISOTROPIC", 2),
        ("GFXOPTIONS_ANTIALIASING_DISABLED", 0),
        ("GFXOPTIONS_ANTIALIASING_X2", 1),
        ("GFXOPTIONS_ANTIALIASING_X4", 2),
        ("GFXOPTIONS_ANTIALIASING_X6", 3),
        ("WATERRENDER_QUALITY_NORMAL", 0),
        ("WATERRENDER_QUALITY_HIGH", 1),
        ("WATERRENDER_QUALITY_HIGHEST", 2),
        ("GFXOPTIONS_DETAIL_HIGH", 0),
        ("GFXOPTIONS_DETAIL_MEDIUM", 1),
        ("GFXOPTIONS_DETAIL_LOW", 2),
        ("GFXOPTIONS_DETAIL_CUSTOM", 3),
        ("GFXOPTIONS_TEXTUREDETAIL_HIGH", 0),
        ("GFXOPTIONS_TEXTUREDETAIL_LOW", 1),
        ("GFXOPTIONS_TEXTUREDETAIL_ULTRALOW", 2),
        ("HUMAN_TEAM_GOOD", 0),
        ("HUMAN_TEAM_BAD", 1),
        ("COLUMNVIEW_DATATYPE_BASICSTRING", 0),
        ("COLUMNVIEW_DATATYPE_NUMERICSTRING", 1),
    ];

    /// <summary>
    /// Gets the task-type constants the scripts name, each worth its type's id (<c>TASKTYPE_*</c> as each type's module registers
    /// it), with the type it names.
    /// </summary>
    public static IReadOnlyList<(string Constant, string Type)> TaskTypes { get; } =
    [
        ("TASKTYPE_AISTATIONARYGUN", "AIStationaryGun"),
        ("TASKTYPE_BOMBBACKPACK", "BombBackpack"),
        ("TASKTYPE_CARDOOR", "CarDoor"),
        ("TASKTYPE_DEATHZONE", "DeathZone"),
        ("TASKTYPE_DRAWER", "Drawer"),
        ("TASKTYPE_GLASS", "Glass"),
        ("TASKTYPE_GRENADEPIN", "GrenadePin"),
        ("TASKTYPE_HITZONE", "HitZone"),
        ("TASKTYPE_LADDER", "Ladder"),
        ("TASKTYPE_PRIMARYMAGICOBJ", "PrimaryMagicObj"),
        ("TASKTYPE_RPGROCKET", "RPGRocket"),
        ("TASKTYPE_SHADOWVOLUME", "ShadowVolume"),
        ("TASKTYPE_WEAPONMAGICOBJ", "WeaponMagicObj"),
    ];

    /// <summary>Registers the plain constants.</summary>
    /// <param name="natives">The namespace.</param>
    public static void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        foreach ((string name, int value) in All)
        {
            natives.RegisterConstant(name, value);
        }
    }

    /// <summary>Registers the task-type constants, once every module has registered its classes: a type no class implements is declared.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="types">The task types.</param>
    public static void RegisterTaskTypes(NativeRegistry natives, TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(types);
        foreach ((string constant, string type) in TaskTypes)
        {
            TaskType registered = types.TryGet(type, out TaskType? known) ? known : types.Declare(type, []);
            natives.RegisterConstant(constant, registered.Id);
        }
    }
}
