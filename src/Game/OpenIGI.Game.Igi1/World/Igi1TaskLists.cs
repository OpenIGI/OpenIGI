using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The fixed-size task lists of Project I.G.I., and the level task types that take a place in each.</summary>
/// <remarks>
/// <para>
/// <c>Game_Init</c> (<c>0x415530</c>) makes six lists with <c>QTaskList_New</c> and the modules make their own; a task's
/// create handler adds it (<c>QTaskList_Add</c>), and one more than a list holds is fatal. A type made from another runs
/// its parent's create handler too, so it takes its parent's places as well: a <c>Terminal</c> is an <c>ExplodeObject</c>,
/// an <c>AlarmControl</c> a <c>Terminal</c>.
/// </para>
/// <para>
/// Only what a level's script declares is listed. Ladders, glass and exploding parts are made from models, mines by their
/// field, a camera's lens by its camera (one each, counted as the camera here), and grenades as they are thrown; the lists
/// they share keep a reserve.
/// </para>
/// </remarks>
public static class Igi1TaskLists
{
    private static readonly string[] Humans = ["Human", "HumanPlayer", "HumanSoldier", "HumanSoldierFemale", "HumanSoldierPistol", "HumanSoldierRPG"];

    // ExplodeObject and the types made from it.
    private static readonly string[] Explodables =
        ["ExplodeObject", "Generator", "Radio", "Siren", "Terminal", "AlarmControl", "SCameraControl", "GenericTBA", "PlaceExplosiveTBA"];

    /// <summary>Gets the lists.</summary>
    public static IReadOnlyList<TaskListLimit> All { get; } =
    [
        new("things to use (doors, switches, terminals, cabinets, fences, wires, mounted guns)", 0x200,
            ["Switch", "Door", "GenericTBA", "PlaceExplosiveTBA", "Wire", "Fence", "Terminal", "AlarmControl", "SCameraControl", "Cabinet", "StationaryGun"])
        { Source = "Game_Init 0x415530, list 0", Reserve = 64 },
        new("fences and mines", 0x40, ["Fence"]) { Source = "Game_Init 0x415530, list 1", Reserve = 16 },
        new("pickups", 0x100, ["GenericPickup", "GunPickup", "AmmoPickup"]) { Source = "Game_Init 0x415530, list 2", Reserve = 32 },
        new("things an explosion can reach (people, vehicles, cameras, lights, terminals, explosives)", 0x100,
            [.. Explodables, .. Humans, "AlarmLight", "Car", "Heli", "SCamera"])
        { Source = "Game_Init 0x415530, list 3", Reserve = 32 },
        new("cars and helicopters", 0x10, ["Car", "Heli"]) { Source = "Game_Init 0x415530, list 4" },
        new("people", 0x80, Humans) { Source = "Human_Init, gHumanList" },
        new("map computer markers", 32, ["ComputerHilight"]) { Source = "ComputerHilight_Init, gHilightList" },
        new("security cameras", 0x40, ["SCamera"]) { Source = "SCamera_Init, gLensList" },
        new("spline path guides", 100, ["SplinePathGuideQTask"]) { Source = "gSplinePathGuides" },
    ];
}
