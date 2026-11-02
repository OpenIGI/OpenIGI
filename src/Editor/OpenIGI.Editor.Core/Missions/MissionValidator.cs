using System.Globalization;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>How serious a problem is.</summary>
public enum IssueSeverity
{
    /// <summary>Worth knowing; the mission still works.</summary>
    Warning,

    /// <summary>The mission will not work as written in the retail game.</summary>
    Error,
}

/// <summary>One problem found in a mission.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Task">The task it is about, or <see langword="null"/> for the mission as a whole.</param>
/// <param name="Message">What is wrong, in a sentence.</param>
public sealed record MissionIssue(IssueSeverity Severity, MissionTask? Task, string Message);

/// <summary>
/// Checks a mission against the rules the retail game enforces silently, so a mistake is found in the editor rather
/// than as a mission that loads and then misbehaves.
/// </summary>
public static class MissionValidator
{
    /// <summary>Checks a mission.</summary>
    /// <param name="document">The mission.</param>
    /// <returns>The problems, errors first.</returns>
    public static IReadOnlyList<MissionIssue> Validate(MissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<MissionIssue> issues = [];
        Dictionary<int, MissionTask> ids = [];

        foreach (MissionTask task in document.EnumerateTasks())
        {
            int id = task.Id;

            // Task_FromId (0x4F1030) refuses anything at or above the table's 4096 slots, and Task_New drops an id that
            // is already bound without a word: in both cases the task exists but nothing can find it by id.
            if (id >= TaskTree.IdCapacity)
            {
                issues.Add(new MissionIssue(IssueSeverity.Error, task, string.Create(CultureInfo.InvariantCulture,
                    $"Id {id} is past the game's {TaskTree.IdCapacity}-slot id table; scripts cannot find this object.")));
            }
            else if (id >= 0 && !ids.TryAdd(id, task))
            {
                issues.Add(new MissionIssue(IssueSeverity.Error, task, string.Create(CultureInfo.InvariantCulture,
                    $"Id {id} is already used by '{ids[id].Label}'; the game creates this one without an id.")));
            }

            if (task.UnnamedArguments is { Count: > 0 } && task.Declaration is not null)
            {
                issues.Add(new MissionIssue(IssueSeverity.Warning, task,
                    $"Its values do not match the '{task.TypeName}' declaration, so they are kept as written and cannot be edited."));
            }
        }

        return [.. issues.OrderByDescending(static i => i.Severity)];
    }
}
