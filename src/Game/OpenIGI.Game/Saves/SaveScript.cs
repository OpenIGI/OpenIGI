using System.Globalization;
using System.Text;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Saves;

/// <summary>
/// The engine's save games (I.G.I. 2's savegame.c and serialize.c): a script that, run, puts the level's state back. It opens
/// with <c>SaveGameInfo(version, mission, ticks);</c> and <c>BreakScript();</c>, which stops a run that only wants the header, then
/// has a <c>DefineTaskState</c> call for each task that keeps state: the task found by its nearest ancestor with a level id and the
/// types and places of the tasks between, then the task's own fields.
/// </summary>
/// <remarks>
/// <para>
/// <c>Serialize_SerializeHandler</c>, from the root down: a task's children are written before it. A task with a level id is
/// written against itself. One without is written only when its type asks to be kept with its parent
/// (<see cref="ISavedState.IsSavedWithParent"/>), against the nearest ancestor that has an id; such a type is written against
/// its ancestor even when it has an id of its own.
/// </para>
/// <para>
/// <c>Serialize_DeSerializeScriptFunction</c>: the ancestor is looked up by its id and checked against its type's name, and each
/// step down is the child of that type at that place among its siblings of the type. A task that is not found is skipped, unless
/// it is the last step and of a type that is made anew (<see cref="ISaveStates.Recreate"/>). The fields are read in the order
/// written, and once the whole script has run every state is told so (<see cref="ISavedState.LoadDone"/>), children first.
/// </para>
/// <para>
/// The retail game compiles the script to <c>.qvm</c> in place; this game writes the script and reads either.
/// </para>
/// </remarks>
public static partial class SaveScript
{
    /// <summary>The call a task's state is written as.</summary>
    public const string DefineTaskState = "DefineTaskState";

    /// <summary>The call the header is written as.</summary>
    public const string SaveGameInfo = "SaveGameInfo";

    /// <summary>The call that stops a run that only wants the header.</summary>
    public const string BreakScript = "BreakScript";

    /// <summary>Writes a save game of a level's tasks, <c>SaveGame_Create</c>.</summary>
    /// <param name="root">The task the state is written from.</param>
    /// <param name="info">The header.</param>
    /// <param name="context">The format, the level's states and the title's own statements.</param>
    /// <returns>The script's text, or <see langword="null"/> for a header that is not valid.</returns>
    public static string? Create(GameTask root, SaveInfo info, SaveContext context)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);
        if (!info.IsValid)
        {
            return null;
        }

        StringBuilder text = new();
        text.Append(Header(info));
        Writer writer = new(context.States, Ids(LevelRoot(root)), text);
        writer.Serialize(root);
        foreach (ISaveSection section in context.Sections)
        {
            text.Append(section.Write());
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes a save game of one part of a task alone, as <c>Game_SaveCarryover</c> saves the player's weapon task with
    /// <c>Serialize_isForceSerializeChildren</c> set: the header, then that part's state against the task.
    /// </summary>
    /// <param name="owner">The task the part belongs to.</param>
    /// <param name="partType">The part's type, such as <c>WeaponHandler</c>.</param>
    /// <param name="info">The header.</param>
    /// <param name="context">The format and the level's states.</param>
    /// <returns>The script's text, or <see langword="null"/> for a header that is not valid or a task without that part.</returns>
    public static string? CreatePart(GameTask owner, string partType, SaveInfo info, SaveContext context)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(partType);
        ArgumentNullException.ThrowIfNull(context);
        if (!info.IsValid || context.States.PartsOf(owner).FirstOrDefault(p => p.TypeName == partType) is not { } part)
        {
            return null;
        }

        Dictionary<GameTask, int> ids = Ids(LevelRoot(owner));
        if (!ids.ContainsKey(owner))
        {
            return null;
        }

        StringBuilder text = new();
        text.Append(Header(info));
        new Writer(context.States, ids, text).WritePart(owner, owner, part, 0);
        return text.ToString();
    }

    /// <summary>
    /// The tasks a save game file keeps the state of, each as the types its <c>DefineTaskState</c> names: the ancestor's, then each
    /// step's down to the task, joined by <c>/</c>. Ids and places are left out, so two saves of a mission made at different moments
    /// can be compared for what kinds of task they keep.
    /// </summary>
    /// <param name="file">The file's bytes, compiled or text.</param>
    /// <returns>A path for each <c>DefineTaskState</c>, in the file's order.</returns>
    public static IReadOnlyList<string> TaskPaths(ReadOnlyMemory<byte> file)
    {
        List<string> paths = [];
        Run(file, [], headerOnly: false, (name, call) =>
        {
            if (name == DefineTaskState && call.Count >= 3)
            {
                int levels = call.GetInt32(2);
                paths.Add(string.Join('/', Enumerable.Range(0, levels + 1)
                    .Select(static i => 1 + (i * 2))
                    .Where(i => i < call.Count)
                    .Select(call.GetText)));
            }
        });
        return paths;
    }

    private static string Header(SaveInfo info) =>
        string.Create(CultureInfo.InvariantCulture, $"{SaveGameInfo}({info.Version}, {info.MissionId}, {info.Ticks});\r\n{BreakScript}();\r\n");

    // Level_atLevelQTaskID: the tasks that have a level id, under the level's root.
    private static Dictionary<GameTask, int> Ids(GameTask root)
    {
        Dictionary<GameTask, int> ids = new(ReferenceEqualityComparer.Instance);
        foreach (GameTask task in root.Descendants().Prepend(root))
        {
            if (task.Id != GameTask.NoId)
            {
                ids.TryAdd(task, task.Id);
            }
        }

        return ids;
    }

    private static GameTask LevelRoot(GameTask task)
    {
        GameTask root = task;
        while (root.Parent is { } up && root is not LevelTask)
        {
            root = up;
        }

        return root;
    }

    // The children of a task that are of a type and still stand, in order: what the original's QTask_GetFirstChildOfType and
    // QTask_GetNextSiblingOfType walk.
    private static IEnumerable<GameTask> ChildrenOfType(GameTask parent, string type, ISaveStates states) =>
        parent.Children.Where(c => !c.IsDeleted && c.Type.Name == type && states.IsPresent(c));

    /// <summary>The write side: Serialize_SerializeHandler and what it calls.</summary>
    private sealed class Writer(ISaveStates states, IReadOnlyDictionary<GameTask, int> ids, StringBuilder text)
    {
        // Serialize_SerializeHandler, with a type's own handler before it: the parts the task's handler writes itself
        // (Serialize_SerializeChild), the children, then the task.
        public void Serialize(GameTask task)
        {
            IReadOnlyList<SavedPart> parts = states.PartsOf(task);
            WriteParts(task, parts, withParent: false);
            foreach (GameTask child in task.Children)
            {
                if (!child.IsDeleted && states.IsPresent(child))
                {
                    Serialize(child);
                }
            }

            // What was made beneath it at run time stands after its own children.
            WriteParts(task, parts, withParent: true);

            if (states.StateOf(task) is not { IsSaved: true } state)
            {
                return;
            }

            GameTask? anchor = state.IsSavedWithParent
                ? NearestWithId(task.Parent)
                : ids.ContainsKey(task) ? task : null;
            if (anchor is null)
            {
                return;
            }

            StateWriter writer = new(Reference);
            state.WriteState(writer);
            text.Append(DefineTaskState).Append('(').Append(Hierarchy(anchor, task)).Append(writer.Arguments).Append(");\n");
        }

        // One part of a task: against the task itself, or, for a type kept with its parent, against the nearest task with an id
        // from the task up.
        public void WritePart(GameTask anchor, GameTask owner, SavedPart part, int place)
        {
            StateWriter writer = new(Reference);
            part.State.WriteState(writer);
            text.Append(DefineTaskState).Append('(').Append(PartHierarchy(anchor, owner, part, place)).Append(writer.Arguments).Append(");\n");
        }

        private void WriteParts(GameTask owner, IReadOnlyList<SavedPart> parts, bool withParent)
        {
            Dictionary<string, int> places = new(StringComparer.Ordinal);
            foreach (SavedPart part in parts)
            {
                // Its place among the owner's children of its type: after the tasks that are of it.
                if (!places.TryGetValue(part.TypeName, out int place))
                {
                    place = ChildrenOfType(owner, part.TypeName, states).Count();
                }

                places[part.TypeName] = place + 1;
                if (part.State.IsSavedWithParent != withParent || !part.State.IsSaved)
                {
                    continue;
                }

                GameTask? anchor = withParent ? NearestWithId(owner) : owner;
                if (anchor is not null)
                {
                    WritePart(anchor, owner, part, place);
                }
            }
        }

        private GameTask? NearestWithId(GameTask? from)
        {
            for (GameTask? up = from; up is not null; up = up.Parent)
            {
                if (ids.ContainsKey(up))
                {
                    return up;
                }
            }

            return null;
        }

        // SerializeQTaskRef_ParameterPrintHandler: a reference to a task is its hierarchy from its nearest ancestor with an id.
        private string Reference(GameTask target) => NearestWithId(target) is { } anchor ? Hierarchy(anchor, target) : "-1";

        // Serialize_PrintQTaskHierarchy: the ancestor's id, its type, the levels down, each level's type and place among its
        // type, then the task's own id.
        private string Hierarchy(GameTask anchor, GameTask task)
        {
            StringBuilder path = Steps(anchor, task, extraLevels: 0);
            return path.Append(CultureInfo.InvariantCulture, $", {ids.GetValueOrDefault(task, -1)}").ToString();
        }

        // A part one level below its task: the task's hierarchy with one more level, the part's type and place, and its id.
        private string PartHierarchy(GameTask anchor, GameTask owner, SavedPart part, int place)
        {
            StringBuilder path = Steps(anchor, owner, extraLevels: 1);
            return path.Append(CultureInfo.InvariantCulture, $", \"{part.TypeName}\", {place}, {part.Id}").ToString();
        }

        private StringBuilder Steps(GameTask anchor, GameTask task, int extraLevels)
        {
            List<GameTask> path = [];
            for (GameTask? at = task; at is not null && !ReferenceEquals(at, anchor); at = at.Parent)
            {
                path.Add(at);
            }

            path.Reverse();
            StringBuilder steps = new();
            steps.Append(CultureInfo.InvariantCulture, $"{ids.GetValueOrDefault(anchor, -1)}, \"{anchor.Type.Name}\", {path.Count + extraLevels}");
            foreach (GameTask step in path)
            {
                string type = step.Type.Name;
                int place = step.Parent is { } parent
                    ? ChildrenOfType(parent, type, states).TakeWhile(c => !ReferenceEquals(c, step)).Count()
                    : 0;
                steps.Append(CultureInfo.InvariantCulture, $", \"{type}\", {place}");
            }

            return steps;
        }
    }
}
