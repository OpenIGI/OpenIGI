using System.Globalization;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Missions;

/// <content>The checks that hold a mission to what the game's own code demands of a level: its fixed lists, its player, its guard generators.</content>
public static partial class MissionValidator
{
    private const string LimitsCheck = "Limits";
    private const string StructureCheck = "Structure";

    private const string PlayerType = "HumanPlayer";
    private const string GeneratorType = "GuardGenerator";
    private const string AiType = "HumanAI";
    private const string HumanPrefix = "Human";

    // A list this full is worth a word before it is over.
    private const double NearlyFull = 0.9;

    /// <summary>How full each of the game's fixed lists is with what a mission's script makes.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="vocabulary">The title's vocabulary, which names the lists.</param>
    /// <returns>Each list with how many places the script takes in it, in the title's order.</returns>
    public static IReadOnlyList<(TaskListLimit List, int Used)> ListUsage(MissionDocument document, ScriptVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(vocabulary);
        Dictionary<string, int> byType = new(StringComparer.Ordinal);
        foreach (MissionTask task in document.EnumerateTasks())
        {
            byType[task.TypeName] = byType.GetValueOrDefault(task.TypeName) + 1;
        }

        return [.. vocabulary.TaskLists.Select(list => (list, list.TaskTypes.Sum(type => byType.GetValueOrDefault(type))))];
    }

    // The game makes each of its task lists once, at a fixed size, and one task more than a list holds stops it as the
    // level loads (QTaskList_Add). The title says which types fill which list.
    private static void CheckLimits(MissionDocument document, IMissionFacts facts, List<MissionIssue> issues)
    {
        if (facts.Vocabulary is not { TaskLists.Count: > 0 } vocabulary)
        {
            return;
        }

        foreach ((TaskListLimit list, int used) in ListUsage(document, vocabulary))
        {
            if (used > list.Room)
            {
                string reserve = list.Reserve > 0
                    ? string.Create(CultureInfo.InvariantCulture, $" ({list.Reserve} of its {list.Capacity} are kept for what the game adds as it plays)")
                    : string.Empty;
                issues.Add(new MissionIssue(IssueSeverity.Error, null, string.Create(
                    CultureInfo.InvariantCulture,
                    $"The mission has {used} {list.Name}, and the game's list holds {list.Room}{reserve}: the game stops as the level loads."))
                {
                    Check = LimitsCheck,
                });
            }
            else if (used >= list.Room * NearlyFull)
            {
                issues.Add(new MissionIssue(IssueSeverity.Warning, null, string.Create(
                    CultureInfo.InvariantCulture,
                    $"The mission has {used} {list.Name} of the {list.Room} the game's list has room for."))
                {
                    Check = LimitsCheck,
                });
            }
        }
    }

    /// <summary>The file a soldier's mind runs, under the mission's folder: the game loads <c>MISSION:AI/&lt;id&gt;.qsc</c> by the task's id.</summary>
    /// <param name="aiTaskId">The <c>HumanAI</c> task's id.</param>
    /// <returns>The path of its compiled script.</returns>
    public static string AiScriptPath(int aiTaskId) => string.Create(CultureInfo.InvariantCulture, $"ai/{aiTaskId}.qvm");

    // A soldier's mind is the script named for its HumanAI task's id (HumanAI's parameter handler, "MISSION:AI/%d.qsc").
    // Without it the game warns "No AI script in HumanAI #n" and the soldier only stands.
    private static void CheckAiScripts(MissionDocument document, IMissionFacts facts, List<MissionIssue> issues)
    {
        foreach (MissionTask ai in document.EnumerateTasks().Where(static t => t.TypeName == AiType))
        {
            if (ai.Id >= 0 && facts.HasMissionFile(AiScriptPath(ai.Id)) == false)
            {
                issues.Add(new MissionIssue(IssueSeverity.Warning, ai, string.Create(
                    CultureInfo.InvariantCulture,
                    $"The mission has no {AiScriptPath(ai.Id)}: the game warns of it as the level loads and the soldier only stands."))
                {
                    Check = StructureCheck,
                });
            }
        }
    }

    // What the level's own tasks take for granted: a player to run the level for, and guard generators made the one way
    // the game reads them.
    private static void CheckStructure(MissionDocument document, List<MissionIssue> issues)
    {
        // Only a level that knows the type can be missing its player: a menu or a config script declares none.
        if (document.Declarations.ContainsKey(PlayerType) && !document.EnumerateTasks().Any(static t => t.TypeName == PlayerType))
        {
            issues.Add(new MissionIssue(IssueSeverity.Error, null, "The mission has no player (HumanPlayer): the game has nobody to run the level for and stops as it loads.")
            {
                Check = StructureCheck,
            });
        }

        foreach (MissionTask generator in document.EnumerateTasks().Where(static t => t.TypeName == GeneratorType))
        {
            // GuardGenerator_Run (0x487EE0) asks its first child whether it is a dead human, and spawns copies of that one
            // child with everything under it (GuardTemplate_Spawn, 0x488040); nothing else under it is part of the guard.
            if (generator.Children.Count == 0)
            {
                issues.Add(new MissionIssue(IssueSeverity.Error, generator, "It has no soldier to make copies of: the game reads one that is not there.")
                {
                    Check = StructureCheck,
                });
                continue;
            }

            if (!generator.Children[0].TypeName.StartsWith(HumanPrefix, StringComparison.Ordinal))
            {
                issues.Add(new MissionIssue(IssueSeverity.Error, generator, $"Its first child is a {generator.Children[0].TypeName}, but the game reads it as the soldier to copy.")
                {
                    Check = StructureCheck,
                });
            }

            if (generator.Children.Count > 1)
            {
                issues.Add(new MissionIssue(IssueSeverity.Warning, generator, "Only its first child, the soldier, is copied when it spawns; what else is under it belongs in a container beside it.")
                {
                    Check = StructureCheck,
                });
            }
        }
    }
}
