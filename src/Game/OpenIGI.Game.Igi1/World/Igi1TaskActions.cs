using System.Globalization;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What making each condition of a Project I.G.I. task hold does, in plain words.</summary>
/// <remarks>
/// The parameters are the ones the levels declare (<c>Task_DeclareParameters</c>), spelt as they spell them. Only a
/// condition whose holding does one plain thing is named; the rest are offered by their parameter's name.
/// </remarks>
public static class Igi1TaskActions
{
    /// <summary>The most objectives a <c>DefineComputerObjective</c> declares.</summary>
    private const int Objectives = 6;

    /// <summary>Gets the actions.</summary>
    public static IReadOnlyList<TaskAction> All { get; } =
    [
        new("StatusMessage", "Send", "Show the message"),
        new("LevelFlow", "Complete", "Complete the mission"),
        new("LevelFlow", "Failed", "Fail the mission"),
        .. Enumerable.Range(1, Objectives).SelectMany(static n => (TaskAction[])
        [
            new("DefineComputerObjective", string.Create(CultureInfo.InvariantCulture, $"Objective {n} Complete Expression"), string.Create(CultureInfo.InvariantCulture, $"Complete objective {n}")),
            new("DefineComputerObjective", string.Create(CultureInfo.InvariantCulture, $"Objective {n} Failed Expression"), string.Create(CultureInfo.InvariantCulture, $"Fail objective {n}")),
        ]),
        new("ConditionalContainer", "Condition", "Run what is inside"),
        new("Door", "Open door expression", "Open the door"),
        new("Door", "Close door expression", "Close the door"),
        new("Door", "Locked expression", "Lock the door"),
        // Every retail scene that waits on something (38 of 45; the rest run on 1 or nothing) runs on
        // "(its start) && !CutScene_N.isFinished", so it plays once.
        new("CutScene", "Run", "Play the cutscene") { Until = "isFinished" },
        new("CutScene", "Reset", "Reset the cutscene"),
        new("AnimTask", "Run", "Play the animation"),
        new("LevelTimer", "On", "Run the timer"),
        new("LevelTimer", "Reset", "Reset the timer"),
        new("GuardGenerator", "Generate guards", "Send in the guards"),
        new("ConditionalSound", "Condition", "Play the sound"),
        new("AlarmControl", "Alarm Expression", "Sound the alarm"),
        new("AlarmLight", "On expression", "Switch the alarm light on"),
        new("Siren", "On Expression", "Sound the siren"),
        new("SCamera", "On Expression", "Switch the camera on"),
        new("Generator", "On expression", "Switch the generator on"),
        new("Radio", "On expression", "Switch the radio on"),
        new("RotatingObject", "On expression", "Start it turning"),
        new("Fence", "Electric Expression", "Electrify the fence"),
        new("MineField", "Explode", "Set off the mines"),
        new("ComputerHilight", "Hilight", "Show the map marker"),
        new("RainEffect", "Is Active", "Make it rain"),
        .. ((string[])["ExplodeObject", "Generator", "Radio", "Siren", "Terminal", "AlarmControl", "SCameraControl", "GenericTBA", "PlaceExplosiveTBA"])
            .Select(static type => new TaskAction(type, "Explosion expression", "Blow it up")),
    ];
}
