using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>Tasks copied out of a document and pasted in.</summary>
/// <param name="Tasks">The tasks read, new and unattached, their ids anonymous.</param>
/// <param name="Skipped">The type names of tasks left out because the document declares no such type.</param>
public readonly record struct PastedTasks(IReadOnlyList<MissionTask> Tasks, IReadOnlyList<string> Skipped);

/// <summary>Copying tasks out as script text and pasting them back in.</summary>
public sealed partial class MissionDocument
{
    /// <summary>Writes tasks as the script that makes them: their types' declarations, then a <c>Task_New</c> for each.</summary>
    /// <param name="tasks">The tasks, with everything under them.</param>
    /// <returns>Script text, readable by <see cref="ReadTasks"/> in any document of the same game, and by a person.</returns>
    /// <remarks>
    /// Text rather than an object, so a copy survives the document closing, can go between missions, and can be pasted into
    /// the script panel or anything else that takes text.
    /// </remarks>
    public string WriteTasks(IEnumerable<MissionTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        MissionTask[] roots = [.. tasks];
        List<QscStatement> statements = [];
        foreach (string type in roots.SelectMany(static t => t.SelfAndSubtree()).Select(static t => t.TypeName).Distinct(StringComparer.Ordinal))
        {
            if (Declarations.TryGetValue(type, out TaskDeclaration? declaration))
            {
                statements.Add(new QscExpressionStatement(WriteDeclaration(declaration)));
            }
        }

        statements.AddRange(roots.Select(static t => (QscStatement)new QscExpressionStatement(WriteTask(t))));
        return QscPrinter.Print(new QscScript(statements, Version));
    }

    /// <summary>Reads tasks from script text, against this document's own declarations.</summary>
    /// <param name="text">Script text, typically from <see cref="WriteTasks"/>.</param>
    /// <returns>The tasks, ready to insert, and the types this document cannot take.</returns>
    /// <exception cref="QscParseException">The text is not script.</exception>
    /// <remarks>
    /// The text's own declarations are not taken in: a task is read as this document declares its type, and one whose type
    /// the copied text declares but this document does not is left out (and named in <see cref="PastedTasks.Skipped"/>) rather than changing what the
    /// mission declares. Ids start anonymous, as <see cref="MissionTask.Clone"/>'s do, so nothing pasted takes an id a
    /// script already finds something else by.
    /// </remarks>
    public PastedTasks ReadTasks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        QscScript script = QscParser.ParseScript(text, Version);
        List<MissionTask> tasks = [];
        List<string> skipped = [];

        // Types the copied text declares but this mission does not: their values would mean nothing here.
        HashSet<string> foreign = new(StringComparer.Ordinal);
        foreach (QscStatement statement in script.Statements)
        {
            if (statement is QscExpressionStatement { Expression: QscCall { Name: DeclareFunction } declare }
                && declare.Arguments.Count > 0
                && ScriptValues.TryGetString(declare.Arguments[0], out string declared)
                && !Declarations.ContainsKey(declared))
            {
                foreign.Add(declared);
            }
        }

        foreach (QscStatement statement in script.Statements)
        {
            if (statement is not QscExpressionStatement { Expression: QscCall { Name: NewFunction } call }
                || !TryReadTask(call, out MissionTask? task))
            {
                continue;
            }

            if (task.SelfAndSubtree().FirstOrDefault(t => foreign.Contains(t.TypeName)) is { } unknown)
            {
                skipped.Add(unknown.TypeName);
                continue;
            }

            tasks.Add(task.Clone());
        }

        return new PastedTasks(tasks, skipped);
    }
}
