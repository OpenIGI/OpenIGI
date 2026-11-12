using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>The checks themselves.</summary>
public static partial class MissionValidator
{
    private const string IdsCheck = "Ids";
    private const string DeclarationCheck = "Values";
    private const string ReferenceCheck = "References";
    private const string ModelCheck = "Models";
    private const string GroundCheck = "Ground";
    private const string TargetCheck = "Target";

    // The model level data names for a marker only the original's editor drew.
    private const string EditorMarkerModel = "waypoint";

    // Further from the ground than this, a standing object falls or is buried when the level starts.
    private const double AboveGroundMetres = 2.5;
    private const double BelowGroundMetres = 0.75;

    // The task types that stand on the ground and are dropped onto it by the game itself.
    private static readonly HashSet<string> Standing = new(StringComparer.Ordinal) { "HumanSoldier", "HumanPlayer" };

    private static void CheckIds(MissionDocument document, List<MissionIssue> issues)
    {
        Dictionary<int, MissionTask> ids = [];
        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id).Where(static id => id >= 0)];
        foreach (MissionTask task in document.EnumerateTasks())
        {
            int id = task.Id;

            // Task_FromId (0x4F1030) refuses anything at or above the table's 4096 slots, and Task_New drops an id that is
            // already bound without a word: in both cases the task exists but nothing can find it by id.
            string? problem = id >= TaskTree.IdCapacity
                ? string.Create(CultureInfo.InvariantCulture, $"Id {id} is past the game's {TaskTree.IdCapacity}-slot id table; scripts cannot find this object.")
                : id >= 0 && !ids.TryAdd(id, task)
                    ? string.Create(CultureInfo.InvariantCulture, $"Id {id} is already used by '{ids[id].Label}'; the game creates this one without an id.")
                    : null;
            if (problem is null)
            {
                continue;
            }

            MissionTask moving = task;
            issues.Add(new MissionIssue(IssueSeverity.Error, task, problem)
            {
                Check = IdsCheck,
                Fixes = [new IssueFix("Give It a Free Id", () => FreeId(used) is int free ? Claim(used, free, new SetIdentityCommand(document, moving, moving.Label, free)) : null)],
            });
        }
    }

    private static void CheckDeclarations(MissionDocument document, List<MissionIssue> issues)
    {
        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (task.UnnamedArguments is { Count: > 0 } && task.Declaration is not null)
            {
                issues.Add(new MissionIssue(IssueSeverity.Warning, task, $"Its values do not match the '{task.TypeName}' declaration, so they are kept as written and cannot be edited.")
                {
                    Check = DeclarationCheck,
                });
            }
        }
    }

    private static void CheckModels(MissionDocument document, IMissionFacts facts, List<MissionIssue> issues)
    {
        foreach (MissionTask task in document.EnumerateTasks())
        {
            // Only what the level's packs place: soldiers' bone models come from the shared humans set, and a waypoint is
            // the editor's marker for a task the game never draws.
            if (task.TypeName.StartsWith("Human", StringComparison.Ordinal))
            {
                continue;
            }

            if (task.FindParameter("Model") is { Arguments: [QscStringLiteral { Value: { Length: > 0 } model }] }
                && !model.Equals(EditorMarkerModel, StringComparison.OrdinalIgnoreCase) && facts.HasModel(model) == false)
            {
                issues.Add(new MissionIssue(IssueSeverity.Warning, task, $"Its model '{model}' is not in the level: it is not drawn and nothing collides with it.")
                {
                    Check = ModelCheck,
                });
            }
        }
    }

    // Soldiers and the player well off the ground, each with a fix that stands it on the ground under it.
    private static void CheckGround(MissionDocument document, IMissionFacts facts, List<MissionIssue> issues)
    {
        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (!Standing.Contains(task.TypeName) || task.FindParameter("Position") is not { Arguments.Count: 3 } position
                || !ScriptValues.TryGetNumber(position.Arguments[0], out double x) || !ScriptValues.TryGetNumber(position.Arguments[1], out double y)
                || !ScriptValues.TryGetNumber(position.Arguments[2], out double z))
            {
                continue;
            }

            Vector3d at = new(WorldUnits.ToMetres(x), WorldUnits.ToMetres(y), WorldUnits.ToMetres(z));
            if (facts.GroundUnder(at) is not { } ground)
            {
                continue;
            }

            double off = at.Z - ground;
            if (off <= AboveGroundMetres && off >= -BelowGroundMetres)
            {
                continue;
            }

            MissionTask standing = task;
            issues.Add(new MissionIssue(
                IssueSeverity.Warning,
                task,
                off > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"It stands {off:0.#} m above the ground: it drops when the level starts.")
                    : string.Create(CultureInfo.InvariantCulture, $"It is {-off:0.#} m below the ground."))
            {
                Check = GroundCheck,
                Fixes = [new IssueFix("Drop to Ground", () => SetParameterCommand.Numbers(document, standing, "Position", [x, y, WorldUnits.FromMetres(ground)]))],
            });
        }
    }

    // A task scripts name as Type_Id, or null for one they name otherwise (this, a native's prefix).
    private static (string Type, int Id)? Referenced(string subject)
    {
        int split = subject.LastIndexOf('_');
        return split > 0 && int.TryParse(subject.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            ? (subject[..split], id)
            : null;
    }

    // The lowest id in the game's table no task has.
    private static int? FreeId(HashSet<int> used)
    {
        for (int id = 1; id < TaskTree.IdCapacity; id++)
        {
            if (!used.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    // A step that takes an id, marked used so the next fix does not take it as well.
    private static Commands.IEditorCommand Claim(HashSet<int> used, int id, Commands.IEditorCommand command)
    {
        used.Add(id);
        return command;
    }
}
