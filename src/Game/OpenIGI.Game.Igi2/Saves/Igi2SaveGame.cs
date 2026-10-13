using System.Globalization;
using System.Text;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Qvm;
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

    /// <summary>The call a pane of glass's breakage is written as.</summary>
    public const string GlassContainerCall = "GlassContainer";

    /// <summary>The call that stops a run that only wants the header.</summary>
    public const string BreakScript = "BreakScript";

    /// <summary>Writes a save game of a level's tasks, <c>SaveGame_Create</c>.</summary>
    /// <param name="root">The task the state is written from.</param>
    /// <param name="info">The header.</param>
    /// <param name="glass">The level's panes of glass, written after the tasks, or <see langword="null"/>.</param>
    /// <returns>The script's text, or <see langword="null"/> for a header that is not valid.</returns>
    public static string? Create(GameTask root, Igi2SaveInfo info, Interaction.Igi2GlassContainer? glass = null)
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
        text.Append(glass?.SaveLines());
        return text.ToString();
    }

    /// <summary>
    /// Writes a save game of one part of a task alone, as <c>Game_SaveCarryover</c> saves the player's weapon task: the header, then
    /// that part's state against the task.
    /// </summary>
    /// <param name="owner">The task the part belongs to.</param>
    /// <param name="partType">The part's type, such as <c>Weapon</c>.</param>
    /// <param name="info">The header.</param>
    /// <returns>The script's text, or <see langword="null"/> for a header that is not valid or a task without that part.</returns>
    public static string? CreatePart(GameTask owner, string partType, Igi2SaveInfo info)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(partType);
        if (!info.IsValid || owner is not IIgi2Serialized serialized
            || serialized.SerializedParts.FirstOrDefault(p => p.TypeName == partType) is not { } part)
        {
            return null;
        }

        Dictionary<GameTask, int> ids = Ids(LevelRoot(owner));
        if (!ids.ContainsKey(owner))
        {
            return null;
        }

        Igi2StateWriter writer = new(target => Hierarchy(target, ids));
        part.State.WriteState(writer);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SaveGameInfo}({info.Version}, {info.MissionId}, {info.Ticks});\r\n{BreakScript}();\r\n{DefineTaskState}({PartHierarchy(Hierarchy(owner, owner, ids), partType, 0)}{writer.Arguments});\n");
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
    /// <param name="glass">The level's panes of glass, put back as the save's <c>GlassContainer</c> lines say, or <see langword="null"/>.</param>
    /// <returns>The header.</returns>
    public static Igi2SaveInfo Load(
        string script, GameTask root, NativeRegistry natives, Igi2ScriptFunctions functions, Interaction.Igi2GlassContainer? glass = null)
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
        Dictionary<string, int> panes = new(StringComparer.Ordinal);
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
            else if (TryCall(statement, out string name, out IReadOnlyList<string> arguments) && name == GlassContainerCall && arguments.Count == 2)
            {
                panes[arguments[0].Trim('"')] = Int(arguments[1]);
            }
        }

        glass?.Restore(panes);
        return info;
    }

    /// <summary>
    /// Reads a save game file's header, <c>SaveGame_Check</c>: the file as retail leaves it, compiled in place
    /// (<c>Script_CompileEx</c>), is run until its <c>BreakScript</c>; the script's text is read.
    /// </summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>The header; not valid when the file has none.</returns>
    public static Igi2SaveInfo CheckFile(ReadOnlyMemory<byte> file) =>
        QvmProgram.IsQvm(file.Span) ? RunCompiled(file, null, null, null) : Check(Encoding.Latin1.GetString(file.Span));

    /// <summary>Puts a save game file's state back onto a level's tasks, compiled or text (<c>SaveGame_LoadFile</c>).</summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="root">The level's root.</param>
    /// <param name="natives">The namespace a text save's constants resolve in.</param>
    /// <param name="functions">The function table a text save's <c>DefineTaskState</c> is run through.</param>
    /// <param name="misread">
    /// When given, takes each task whose state was not read to the end of its arguments, or past it: its type, then how many it read
    /// of how many there were.
    /// </param>
    /// <param name="glass">The level's panes of glass, put back as the save's <c>GlassContainer</c> lines say, or <see langword="null"/>.</param>
    /// <returns>The header.</returns>
    public static Igi2SaveInfo LoadFile(
        ReadOnlyMemory<byte> file,
        GameTask root,
        NativeRegistry natives,
        Igi2ScriptFunctions functions,
        ICollection<string>? misread = null,
        Interaction.Igi2GlassContainer? glass = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!QvmProgram.IsQvm(file.Span))
        {
            return Load(Encoding.Latin1.GetString(file.Span), root, natives, functions, glass);
        }

        Dictionary<int, GameTask> byId = [];
        foreach ((GameTask task, int id) in Ids(LevelRoot(root)))
        {
            byId.TryAdd(id, task);
        }

        return RunCompiled(file, byId, misread, glass);
    }

    // A compiled save run with its own three calls: the header taken, a run for the header stopped at the break, and each
    // DefineTaskState put back when there are tasks to put it on.
    private static Igi2SaveInfo RunCompiled(
        ReadOnlyMemory<byte> file, IReadOnlyDictionary<int, GameTask>? byId, ICollection<string>? misread, Interaction.Igi2GlassContainer? glass)
    {
        QvmProgram program;
        try
        {
            program = QvmProgram.Read(file);
        }
        catch (QvmFormatException)
        {
            return default;
        }

        NativeRegistry calls = new();
        calls.RegisterConstant("TRUE", 1);
        calls.RegisterConstant("FALSE", 0);
        Igi2SaveInfo info = default;
        QvmInterpreter? machine = null;
        calls.RegisterFunction(SaveGameInfo, c =>
        {
            info = new Igi2SaveInfo(c.GetInt32(0), c.GetInt32(1), c.GetInt32(2));
            return QvmValue.Zero;
        }, 3);
        calls.RegisterFunction(BreakScript, _ =>
        {
            if (byId is null)
            {
                machine?.Break();
            }

            return QvmValue.Zero;
        });
        calls.RegisterFunction(DefineTaskState, c => byId is null ? QvmValue.Zero : Define(new Igi2ScriptCall(c), byId, misread));

        // GlassContainer("<key>", <state>): a pane's breakage, gathered (GlassContainer_DeSerializeScriptFunction) and put back once
        // the run is done (GlassContainer_DeSerializeDoneHandler).
        Dictionary<string, int> panes = new(StringComparer.Ordinal);
        calls.RegisterFunction(GlassContainerCall, c =>
        {
            if (c.ArgumentCount == 2)
            {
                panes[c.GetString(0)] = c.GetInt32(1);
            }

            return QvmValue.Zero;
        }, 2);
        machine = new QvmInterpreter(program, calls);
        machine.Run();
        if (byId is not null)
        {
            glass?.Restore(panes);
        }

        return info;
    }

    /// <summary>Puts one task's state back, <c>Serialize_DeSerializeScriptFunction</c>.</summary>
    /// <param name="call">The call.</param>
    /// <param name="byId">The level's tasks by their ids.</param>
    /// <param name="misread">When given, takes the task when its state was not read to exactly the end of its arguments.</param>
    /// <returns>Nought.</returns>
    public static QvmValue Define(Igi2ScriptCall call, IReadOnlyDictionary<int, GameTask> byId, ICollection<string>? misread = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(byId);
        int next = 0;
        if (FindState(call, byId, ref next) is { } serialized)
        {
            Igi2StateReader reader = new(call, next, r => FindReference(call, byId, r));
            serialized.ReadState(reader);
            if (reader.Next != call.Count)
            {
                misread?.Add(string.Create(CultureInfo.InvariantCulture, $"{serialized.GetType().Name} read {reader.Next - next} of {call.Count - next}"));
            }
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

        string hierarchy = Hierarchy(anchor, task, ids);
        Dictionary<string, int> places = new(StringComparer.Ordinal);
        foreach (Igi2SerializedPart part in serialized.SerializedParts)
        {
            int place = places.GetValueOrDefault(part.TypeName);
            places[part.TypeName] = place + 1;
            Igi2StateWriter partWriter = new(target => Hierarchy(target, ids));
            part.State.WriteState(partWriter);
            text.Append(DefineTaskState).Append('(').Append(PartHierarchy(hierarchy, part.TypeName, place)).Append(partWriter.Arguments).Append(");\n");
        }

        Igi2StateWriter writer = new(target => Hierarchy(target, ids));
        serialized.WriteState(writer);
        text.Append(DefineTaskState).Append('(').Append(hierarchy).Append(writer.Arguments).Append(");\n");
    }

    // A part one level below its task: the task's hierarchy with one more level, the part's type and place, and no id of its own.
    private static string PartHierarchy(string hierarchy, string type, int place)
    {
        string[] fields = hierarchy.Split(", ");
        int levels = int.Parse(fields[2], CultureInfo.InvariantCulture) + 1;
        StringBuilder text = new();
        text.Append(fields[0]).Append(", ").Append(fields[1]).Append(", ").Append(levels.ToString(CultureInfo.InvariantCulture));
        for (int i = 3; i < fields.Length - 1; i++)
        {
            text.Append(", ").Append(fields[i]);
        }

        text.Append(CultureInfo.InvariantCulture, $", \"{type}\", {place}, -1");
        return text.ToString();
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

    // The task's state, or, where the last step down names a type the task has no child of at that place, the part of the task retail
    // keeps as one.
    private static IIgi2Serialized? FindState(Igi2ScriptCall call, IReadOnlyDictionary<int, GameTask> byId, ref int next)
    {
        int start = next;
        if (call.Count < start + 3)
        {
            return null;
        }

        int levels = call.GetInt32(start + 2);
        if (levels == 0)
        {
            return Find(call, byId, ref next) as IIgi2Serialized;
        }

        GameTask? parent = byId.GetValueOrDefault(call.GetInt32(start));
        bool known = parent is not null && parent.Type?.Name == call.GetText(start + 1);
        for (int level = 0; level < levels - 1 && parent is not null; level++)
        {
            string stepType = call.GetText(start + 3 + (level * 2));
            int stepPlace = call.GetInt32(start + 4 + (level * 2));
            parent = parent.Children.Where(c => c.Type?.Name == stepType).Skip(stepPlace).FirstOrDefault();
        }

        int last = start + 3 + ((levels - 1) * 2);
        string type = call.GetText(last);
        int place = call.GetInt32(last + 1);
        next = last + 3;
        if (!known || parent is null)
        {
            return null;
        }

        return parent.Children.Where(c => c.Type?.Name == type).Skip(place).FirstOrDefault() is { } child
            ? child as IIgi2Serialized
            : (parent as IIgi2Serialized)?.SerializedParts.Where(p => p.TypeName == type).Skip(place).FirstOrDefault()?.State;
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
