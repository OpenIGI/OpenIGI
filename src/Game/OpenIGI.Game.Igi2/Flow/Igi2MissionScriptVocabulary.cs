using OpenIGI.Game.World;
using OpenIGI.Scripting.Vocabulary;
using static OpenIGI.Game.World.MissionScriptVocabulary;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// The natives I.G.I. 2's level expressions call beyond the shared ones: arity as the registrars declare it, each argument
/// typed by how the callback reads it.
/// </summary>
/// <remarks>
/// A registrar arity of -1 is a callback that counts its own arguments; those are listed with the trailing parameters a
/// call may leave out. <c>HumanPlayer_isInPlay</c> and <c>NetObjective_NotifyWhoDidIt</c> are retail natives the port does
/// not implement yet; they are here so a retail mission that calls them still reads as typed nodes.
/// </remarks>
public static class Igi2MissionScriptVocabulary
{
    /// <summary>Gets the global variables I.G.I. 2's registrars bind, shared ones first.</summary>
    public static IReadOnlyList<NativeParameter> Globals { get; } =
    [
        .. MissionScriptVocabulary.Globals,
        new("NetMission_eCurrentObjective", ScriptType.Int, "The multiplayer objective in play."),
        new("NetManager_eTeam", ScriptType.Int, "The local player's team."),
        new("Computer_nZoomLevel", ScriptType.Int, "The map computer's zoom."),
        .. Enumerable.Range(0, 10).Select(static d => new NativeParameter($"Key{d}.isPressed", ScriptType.Bool, $"Whether the {d} key is held.")),
    ];

    /// <summary>Gets the game module's constants, each family the names sharing all but their last word (<c>HUMAN_TEAM</c>).</summary>
    public static IReadOnlyList<ConstantFamily> Families { get; } =
    [
        .. Config.Igi2GameConstants.All
            .GroupBy(static c => c.Name[..Math.Max(1, c.Name.LastIndexOf('_'))], StringComparer.Ordinal)
            .Select(static g => new ConstantFamily(g.Key, [.. g.Select(static c => new KeyValuePair<string, int>(c.Name, c.Value))])),
    ];

    /// <summary>Gets the natives, shared ones first.</summary>
    public static IReadOnlyList<NativeSignature> Signatures { get; } =
    [
        .. MissionScriptVocabulary.Signatures,
        Pure("AreaActivate_IdInArea", ScriptType.Bool, "Whether an object stands inside an AreaActivate.",
            new("Area", ScriptType.TaskRef), new("Object", ScriptType.TaskRef)),
        Action("ConveyorBelt_SetSpeed", [new("Belt", ScriptType.TaskRef), new("X", ScriptType.Real), new("Y", ScriptType.Real)], "Sets how fast a conveyor belt runs each way."),
        Action("HumanPlayer_Activate", [new("Task", ScriptType.TaskRef)], "Activates a task as though the player had used it."),
        Action("HumanPlayer_LockActivate", [new("On", ScriptType.Bool)], "Stops the player using things, or lets them again."),
        Action("HumanPlayer_DisableAllWeapons", [], "Takes the player's weapons out of use."),
        Pure("HumanPlayer_isInPlay", ScriptType.Bool, "Whether the player is in play."),
        Pure("HumanPlayer_isDead", ScriptType.Bool, "Whether the player is dead (of several, whether none is left alive)."),
        Pure("HumanPlayer_GetPrimaryWeaponID", ScriptType.Text, "The id of the player's primary weapon."),
        Pure("HumanPlayer_GetPrimaryWeaponModel", ScriptType.Text, "The model of the player's primary weapon."),
        Action("Human_AddWeapon", [new("Weapon", ScriptType.Text)], "Gives the human running the script a weapon."),
        Action("Human_AddAmmo", [new("Ammo", ScriptType.Text), new("Count", ScriptType.Int)], "Gives the human running the script ammunition."),
        Action("Human_AddAmmoForWeapon", [new("Weapon", ScriptType.Text), new("Count", ScriptType.Int), new("Extra", ScriptType.Int)], "Gives ammunition for a weapon.") with { OptionalCount = 2 },
        Action("Human_PopPosition", [new("Human", ScriptType.TaskRef), new("X metres", ScriptType.Real), new("Y metres", ScriptType.Real), new("Z metres", ScriptType.Real)], "Puts a human at a spot; the player goes back to its default states."),
        Pure("LaserDesignator_GetTicksHeldOnTask", ScriptType.Int, "How many ticks the laser dot has rested on a task.", new NativeParameter("Task", ScriptType.TaskRef)),
        Action("LaserDesignatorOverlay_SetLockedOnStatus", [new("On", ScriptType.Bool)], "Shows the laser designator as locked on, or not."),
        Action("LevelFlow_SetTimeOfDay", [new("Hour", ScriptType.Real)], "Sets the clock to an hour of the day."),
        Action("NetObjective_NotifyWhoDidIt", [new("Objective", ScriptType.Int), new("Team", ScriptType.Int)], "Tells the players which team completed an objective."),
        Action("SequenceCommand_SetCommand", [new("Command", ScriptType.Int)], "Picks the sequence's next command.") with { OptionalCount = 1 },
        Action("SoundGenerator_PlaySound", [new("Sound", ScriptType.Text)], "Plays a sound definition at the generator."),
        Action("SoundGenerator_StopSound", [new("Sound", ScriptType.Text, "Ignored: the generator stops whatever it plays.")], "Stops the generator's sound.") with { OptionalCount = 1 },
        Action("SplinePathGuide_SetRunning", [new("On", ScriptType.Bool)], "Starts or stops a spline path guide.") with { OptionalCount = 1 },
    ];
}
