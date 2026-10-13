using System.Globalization;
using System.Text;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>What a save game says of itself, <c>SaveGame_s</c>: its version, its mission and the level's tick it was made at.</summary>
/// <param name="Version">The format's version: 63.</param>
/// <param name="MissionId">The mission, <c>Game_iMissionID</c>.</param>
/// <param name="Ticks">The level's tick.</param>
public readonly record struct Igi2SaveInfo(int Version, int MissionId, int Ticks)
{
    /// <summary>The version this game writes, and the least it reads.</summary>
    public const int CurrentVersion = 63;

    /// <summary>Gets a value indicating whether it can be loaded, <c>isValid</c>: its version is the current one or later.</summary>
    public bool IsValid => Version >= CurrentVersion;
}

/// <summary>
/// I.G.I. 2's save games (savegame.c, serialize.c): a script that, run, puts the level's state back. It opens with
/// <c>SaveGameInfo(version, mission, ticks);</c> and <c>BreakScript();</c>, which stops a run that only wants the header, then has a
/// <c>DefineTaskState</c> call for each task that keeps state: the task found by its nearest ancestor with a level id and the types
/// and places of the tasks between, then the task's own fields.
/// </summary>
/// <remarks>
/// <para>
/// <c>Serialize_SerializeHandler</c>, from the root down: a task's children are written before it. A task with a level id is written
/// against itself; one without, or one that is written with its parent, against the nearest ancestor that has one.
/// </para>
/// <para>
/// <c>Serialize_DeSerializeScriptFunction</c>: the ancestor is looked up by its id and checked against its type's name, and each step
/// down is the child of that type at that place among its siblings of the type; a task that is not found is skipped. The fields are
/// read in the order written (<see cref="IIgi2Serialized.ReadState"/>).
/// </para>
/// <para>
/// The retail game compiles the script to <c>.qvm</c> beside it; this game writes the script and reads either.
/// </para>
/// </remarks>
public static class Igi2SaveGame
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
    /// <returns>The script's text, or <see langword="null"/> for a header that is not valid.</returns>
    public static string? Create(GameTask root, Igi2SaveInfo info)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!info.IsValid)
        {
            return null;
        }

        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"{SaveGameInfo}({info.Version}, {info.MissionId}, {info.Ticks});\r\n{BreakScript}();\r\n");
        Dictionary<GameTask, int> ids = Ids(LevelRoot(root));
        Serialize(root, text, ids);
        return text.ToString();
    }

    /// <summary>Reads a save game's header without putting its state back, <c>SaveGame_Check</c>.</summary>
    /// <param name="script">The script's text.</param>
    /// <returns>The header; not valid when the script has none.</returns>
    public static Igi2SaveInfo Check(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Igi2SaveInfo info = default;
        foreach (string statement in Statements(script))
        {
            if (TryCall(statement, out string name, out IReadOnlyList<string> arguments) && name == SaveGameInfo && arguments.Count >= 3)
            {
                info = new Igi2SaveInfo(Int(arguments[0]), Int(arguments[1]), Int(arguments[2]));
                break;
            }
        }

        return info;
    }

    /// <summary>
    /// Puts a save game's state back onto a level's tasks, <c>SaveGame_Load</c>: each <c>DefineTaskState</c> in turn; the script's
    /// text is read, as its compiled form would be run.
    /// </summary>
    /// <param name="script">The script's text.</param>
    /// <param name="root">The level's root.</param>
    /// <param name="natives">The namespace its constants (<c>TRUE</c>, <c>FALSE</c>) resolve in.</param>
    /// <param name="functions">The function table <c>DefineTaskState</c> is run through.</param>
    /// <returns>The header.</returns>
    public static Igi2SaveInfo Load(string script, GameTask root, NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        Igi2SaveInfo info = Check(script);
        if (!info.IsValid)
        {
            return info;
        }

        Dictionary<int, GameTask> byId = [];
        foreach ((GameTask task, int id) in Ids(LevelRoot(root)))
        {
            byId.TryAdd(id, task);
        }

        functions.Register(natives, DefineTaskState, c => Define(c, byId), 0);
        foreach (string statement in Statements(script))
        {
            if (statement.StartsWith(DefineTaskState, StringComparison.Ordinal))
            {
                Igi2LevelExpression call = new(statement, "Level", natives);
                if (!call.IsEmpty)
                {
                    call.Run(natives, functions);
                }
            }
        }

        return info;
    }

    /// <summary>Puts one task's state back, <c>Serialize_DeSerializeScriptFunction</c>.</summary>
    /// <param name="call">The call.</param>
    /// <param name="byId">The level's tasks by their ids.</param>
    /// <returns>Nought.</returns>
    public static QvmValue Define(Igi2ScriptCall call, IReadOnlyDictionary<int, GameTask> byId)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(byId);
        int next = 0;
        if (Find(call, byId, ref next) is IIgi2Serialized serialized)
        {
            serialized.ReadState(new Igi2StateReader(call, next, reader => FindReference(call, byId, reader)));
        }

        return QvmValue.Zero;
    }

    // Serialize_SerializeHandler: children first, then the task, against itself or its nearest ancestor with an id.
    private static void Serialize(GameTask task, StringBuilder text, IReadOnlyDictionary<GameTask, int> ids)
    {
        foreach (GameTask child in task.Children)
        {
            Serialize(child, text, ids);
        }

        if (task is not IIgi2Serialized serialized)
        {
            return;
        }

        GameTask? anchor = !serialized.IsSavedWithParent && ids.ContainsKey(task) ? task : null;
        if (anchor is null)
        {
            for (GameTask? up = task.Parent; up is not null; up = up.Parent)
            {
                if (ids.ContainsKey(up))
                {
                    anchor = up;
                    break;
                }
            }
        }

        if (anchor is null)
        {
            return;
        }

        Igi2StateWriter writer = new(target => Hierarchy(target, ids));
        serialized.WriteState(writer);
        text.Append(DefineTaskState).Append('(').Append(Hierarchy(anchor, task, ids)).Append(writer.Arguments).Append(");\n");
    }

    // A reference to a task: its hierarchy from its nearest ancestor with an id, or -1.
    private static string Hierarchy(GameTask target, IReadOnlyDictionary<GameTask, int> ids)
    {
        for (GameTask? up = target; up is not null; up = up.Parent)
        {
            if (ids.ContainsKey(up))
            {
                return Hierarchy(up, target, ids);
            }
        }

        return "-1";
    }

    // Serialize_PrintQTaskHierarchy: the ancestor's id, its type, the levels down, each level's type and place among its type, then
    // the task's own id.
    private static string Hierarchy(GameTask anchor, GameTask task, IReadOnlyDictionary<GameTask, int> ids)
    {
        List<GameTask> path = [];
        for (GameTask? at = task; at is not null && !ReferenceEquals(at, anchor); at = at.Parent)
        {
            path.Add(at);
        }

        path.Reverse();
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"{ids.GetValueOrDefault(anchor, -1)}, \"{anchor.Type?.Name}\", {path.Count}");
        foreach (GameTask step in path)
        {
            string type = step.Type?.Name ?? string.Empty;
            int place = step.Parent?.Children.Where(c => c.Type?.Name == type).TakeWhile(c => !ReferenceEquals(c, step)).Count() ?? 0;
            text.Append(CultureInfo.InvariantCulture, $", \"{type}\", {place}");
        }

        text.Append(CultureInfo.InvariantCulture, $", {ids.GetValueOrDefault(task, -1)}");
        return text.ToString();
    }

    // Serialize_GetTaskFromArgList and Serialize_GetChildTaskFromArgList.
    private static GameTask? Find(Igi2ScriptCall call, IReadOnlyDictionary<int, GameTask> byId, ref int next)
    {
        if (call.Count < next + 3)
        {
            return null;
        }

        int id = call.GetInt32(next++);
        string type = call.GetText(next++);
        int levels = call.GetInt32(next++);
        GameTask? task = byId.GetValueOrDefault(id);
        if (task is null || task.Type?.Name != type)
        {
            return null;
        }

        for (int level = 0; level < levels; level++)
        {
            string childType = call.GetText(next++);
            int place = call.GetInt32(next++);
            task = task?.Children.Where(c => c.Type?.Name == childType).Skip(place).FirstOrDefault();
        }

        next++;
        return task;
    }

    private static GameTask? FindReference(Igi2ScriptCall call, IReadOnlyDictionary<int, GameTask> byId, Igi2StateReader reader)
    {
        int next = reader.Next;
        GameTask? task = Find(call, byId, ref next);
        while (reader.Next < next)
        {
            reader.Int();
        }

        return task;
    }

    // Level_atLevelQTaskID: the tasks that have a level id, under the level's root.
    private static Dictionary<GameTask, int> Ids(GameTask root)
    {
        Dictionary<GameTask, int> ids = new(ReferenceEqualityComparer.Instance);
        foreach (GameTask task in Igi2TaskTree.Descendants(root).Prepend(root))
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

    private static IEnumerable<string> Statements(string script)
    {
        StringBuilder current = new();
        bool quoted = false;
        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];
            if (quoted && c == '\\' && i + 1 < script.Length)
            {
                current.Append(c).Append(script[++i]);
                continue;
            }

            if (c == '"')
            {
                quoted = !quoted;
            }

            if (c == ';' && !quoted)
            {
                string statement = current.ToString().Trim();
                if (statement.Length > 0)
                {
                    yield return statement;
                }

                current.Clear();
                continue;
            }

            current.Append(c);
        }
    }

    private static bool TryCall(string statement, out string name, out IReadOnlyList<string> arguments)
    {
        int open = statement.IndexOf('(', StringComparison.Ordinal);
        int close = statement.LastIndexOf(')');
        name = open > 0 ? statement[..open].Trim() : string.Empty;
        arguments = open > 0 && close > open
            ? [.. statement[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
            : [];
        return open > 0;
    }

    private static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
