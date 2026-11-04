using System.Globalization;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Reads tasks from script text as new tasks of this document: each that had an id gets a free one, and what the copies
    /// say about each other (a condition reading <c>Type_Id.field</c>, a parameter naming a task) follows them.
    /// </summary>
    /// <param name="text">Script text, typically from <see cref="WriteTasks"/>.</param>
    /// <param name="original">Receives each new id's old one, so a caller can carry what it kept by the old ids.</param>
    /// <returns>The tasks, ready to insert, and the types this document cannot take.</returns>
    /// <exception cref="QscParseException">The text is not script.</exception>
    /// <remarks>
    /// For logic: <see cref="ReadTasks"/> pastes tasks anonymous, which suits objects, but a condition finds a task by its id.
    /// References to tasks outside what was copied are left alone, so a pasted trigger still reads the mission's own tasks.
    /// When every id is taken, a task stays anonymous.
    /// </remarks>
    public PastedTasks ReadTasksAsNew(string text, out IReadOnlyDictionary<int, int> original)
    {
        PastedTasks read = ReadTasks(text);
        QscScript script = QscParser.ParseScript(text, Version);
        List<MissionTask> sources = [];
        foreach (QscStatement statement in script.Statements)
        {
            if (statement is QscExpressionStatement { Expression: QscCall { Name: NewFunction } call } && TryReadTask(call, out MissionTask? task))
            {
                sources.Add(task);
            }
        }

        // The clones are the sources less the skipped, in order: pair them, task for task down each subtree.
        Dictionary<int, int> renumbered = [];
        Dictionary<int, int> back = [];
        HashSet<int> used = [.. EnumerateTasks().Select(static t => t.Id).Where(static id => id >= 0)];
        int next = 1;
        List<(MissionTask Copy, MissionTask Source)> pairs = [];
        int s = 0;
        foreach (MissionTask copy in read.Tasks)
        {
            while (s < sources.Count && sources[s].TypeName != copy.TypeName)
            {
                s++;
            }

            if (s < sources.Count)
            {
                pairs.AddRange(copy.SelfAndSubtree().Zip(sources[s].SelfAndSubtree()));
                s++;
            }
        }

        foreach ((MissionTask copy, MissionTask source) in pairs.Where(static p => p.Source.Id >= 0))
        {
            while (next < MaximumTaskId && used.Contains(next))
            {
                next++;
            }

            if (next >= MaximumTaskId)
            {
                break;
            }

            used.Add(next);
            renumbered[source.Id] = next;
            back[next] = source.Id;
            copy.Id = next;
        }

        foreach ((MissionTask copy, _) in pairs)
        {
            foreach (MissionParameter parameter in copy.Parameters)
            {
                for (int i = 0; i < parameter.Arguments.Count; i++)
                {
                    parameter.Arguments[i] = Follow(copy.TypeName, parameter, parameter.Arguments[i], pairs, renumbered);
                }
            }
        }

        original = back;
        return read;
    }

    /// <summary>The first id past the 4096-entry table a script finds tasks in.</summary>
    public const int MaximumTaskId = 4096;

    // An argument with the copies' old ids written as their new ones: a reference parameter's number, a condition's names.
    private static QscExpression Follow(string type, MissionParameter parameter, QscExpression argument, List<(MissionTask Copy, MissionTask Source)> pairs, Dictionary<int, int> renumbered)
    {
        if (TaskPinHints.TryGetReference(type, parameter.Name, out _)
            && ScriptValues.TryGetInteger(argument, out int id) && renumbered.TryGetValue(id, out int moved))
        {
            return ScriptValues.Integer(moved);
        }

        if (parameter.Declaration.Type != Engine.Tasks.ScriptParamType.VarString || !ScriptValues.TryGetString(argument, out string text) || text.Length == 0)
        {
            return argument;
        }

        string rewritten = text;
        foreach ((_, MissionTask source) in pairs.Where(p => p.Source.Id >= 0 && renumbered.ContainsKey(p.Source.Id)))
        {
            string old = string.Create(CultureInfo.InvariantCulture, $"{source.TypeName}_{source.Id}");
            string now = string.Create(CultureInfo.InvariantCulture, $"{source.TypeName}_{renumbered[source.Id]}");
            rewritten = Regex.Replace(rewritten, $@"\b{Regex.Escape(old)}\b", now, RegexOptions.CultureInvariant);
        }

        return rewritten == text ? argument : new QscStringLiteral(rewritten);
    }
}
