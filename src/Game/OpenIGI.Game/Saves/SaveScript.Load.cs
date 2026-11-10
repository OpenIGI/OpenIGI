using System.Globalization;
using System.Text;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Saves;

/// <content>The read side: a save's header, and its state put back onto a level.</content>
public static partial class SaveScript
{
    /// <summary>
    /// Reads a save game file's header, <c>SaveGame_Check</c>: the file as retail leaves it, compiled in place
    /// (<c>Script_CompileEx</c>), is run until its <c>BreakScript</c>; one kept as text is read to the same statement.
    /// </summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="context">The format.</param>
    /// <returns>The header; not valid when the file has none.</returns>
    public static SaveInfo Check(ReadOnlyMemory<byte> file, SaveContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SaveInfo info = default;
        Run(file, context.Sections, headerOnly: true, (name, call) =>
        {
            if (name == SaveGameInfo)
            {
                info = ReadInfo(call, context);
            }
        });
        return info;
    }

    /// <summary>Reads the header of a save game kept as text.</summary>
    /// <param name="script">The script's text.</param>
    /// <param name="context">The format.</param>
    /// <returns>The header; not valid when the script has none.</returns>
    public static SaveInfo Check(string script, SaveContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Check(Encoding.Latin1.GetBytes(script), context);
    }

    /// <summary>
    /// Puts a save game file's state back onto a level's tasks, compiled or text (<c>SaveGame_LoadFile</c>): each
    /// <c>DefineTaskState</c> in turn, then the pass that tells every state the load is done.
    /// </summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="root">The task the load is for: the level's, or the player's for a carry-over.</param>
    /// <param name="context">The format, the level's states and the title's own statements.</param>
    /// <param name="misread">
    /// When given, takes each task whose state was not read to exactly the end of its arguments: its type, then how many it read
    /// of how many there were.
    /// </param>
    /// <returns>The header; not valid when nothing was loaded.</returns>
    public static SaveInfo Load(ReadOnlyMemory<byte> file, GameTask root, SaveContext context, ICollection<string>? misread = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);
        SaveInfo info = Check(file, context);
        if (!info.IsValid)
        {
            return info;
        }

        Dictionary<int, GameTask> byId = [];
        foreach ((GameTask task, int id) in Ids(LevelRoot(root)))
        {
            byId.TryAdd(id, task);
        }

        Reader reader = new(context.States, byId, misread);
        Run(file, context.Sections, headerOnly: false, (name, call) =>
        {
            if (name == DefineTaskState)
            {
                reader.Define(call);
            }
            else if (context.Sections.FirstOrDefault(s => s.CallName == name) is { } section)
            {
                section.Read(call);
            }
        });

        foreach (ISaveSection section in context.Sections)
        {
            section.LoadDone();
        }

        Done(root, context.States);
        return info;
    }

    /// <summary>Puts the state of a save game kept as text back onto a level's tasks.</summary>
    /// <param name="script">The script's text.</param>
    /// <param name="root">The task the load is for.</param>
    /// <param name="context">The format, the level's states and the title's own statements.</param>
    /// <param name="misread">When given, takes each task whose state was not read to exactly the end of its arguments.</param>
    /// <returns>The header.</returns>
    public static SaveInfo Load(string script, GameTask root, SaveContext context, ICollection<string>? misread = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Load(Encoding.Latin1.GetBytes(script), root, context, misread);
    }

    // SaveGame_SaveInfoScriptFunction: three arguments or none of it is taken; valid from the format's version on.
    private static SaveInfo ReadInfo(ISaveArguments call, SaveContext context) =>
        call.Count >= 3
            ? new SaveInfo(call.GetInt32(0), call.GetInt32(1), call.GetInt32(2), call.GetInt32(0) >= context.Version)
            : default;

    // Serialize_TriggerQTaskEventRecursivelyChildrenFirst with Serialize_eDeSerializeDoneQTaskEvent.
    private static void Done(GameTask task, ISaveStates states)
    {
        foreach (GameTask child in task.Children)
        {
            if (!child.IsDeleted)
            {
                Done(child, states);
            }
        }

        foreach (SavedPart part in states.PartsOf(task))
        {
            part.State.LoadDone();
        }

        states.StateOf(task)?.LoadDone();
    }

    // Runs a save's statements, compiled or text, handing each call that is the engine's or a section's to the caller. A run
    // for the header stops at BreakScript (Script_isBreakEnabled).
    private static void Run(ReadOnlyMemory<byte> file, IReadOnlyList<ISaveSection> sections, bool headerOnly, Action<string, ISaveArguments> onCall)
    {
        if (!QvmProgram.IsQvm(file.Span))
        {
            foreach (string statement in Statements(Encoding.Latin1.GetString(file.Span)))
            {
                if (!TextArguments.TryParse(statement, out string name, out TextArguments? arguments))
                {
                    continue;
                }

                if (name == BreakScript && headerOnly)
                {
                    return;
                }

                onCall(name, arguments);
            }

            return;
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(file);
        }
        catch (QvmFormatException)
        {
            return;
        }

        NativeRegistry calls = new();
        calls.RegisterConstant("TRUE", 1);
        calls.RegisterConstant("FALSE", 0);
        QvmInterpreter? machine = null;
        foreach (string name in sections.Select(static s => s.CallName).Append(SaveGameInfo).Append(DefineTaskState))
        {
            calls.RegisterFunction(name, c =>
            {
                onCall(name, new CompiledArguments(c));
                return QvmValue.Zero;
            });
        }

        calls.RegisterFunction(BreakScript, _ =>
        {
            if (headerOnly)
            {
                machine?.Break();
            }

            return QvmValue.Zero;
        });

        // A statement nothing here takes -- a title's own, read without its section -- is passed over rather than ending the run
        // on an unknown name.
        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is { Length: > 0 } && !calls.Contains(symbol))
            {
                calls.RegisterFunction(symbol, static _ => QvmValue.Zero);
            }
        }

        machine = new QvmInterpreter(program, calls);
        machine.Run();
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

    /// <summary>The read side: Serialize_DeSerializeScriptFunction and what it calls.</summary>
    private sealed class Reader(ISaveStates states, IReadOnlyDictionary<int, GameTask> byId, ICollection<string>? misread)
    {
        // Serialize_DeSerializeScriptFunction: the task found, its state read.
        public void Define(ISaveArguments call)
        {
            int next = 0;
            if (FindState(call, ref next) is not { } state)
            {
                return;
            }

            StateReader reader = new(call, next, r => FindReference(call, r));
            state.ReadState(reader);
            if (reader.Next != call.Count)
            {
                misread?.Add(string.Create(CultureInfo.InvariantCulture, $"{state.GetType().Name} read {reader.Next - next} of {call.Count - next}"));
            }
        }

        // Serialize_GetTaskFromArgList and Serialize_GetChildTaskFromArgList, to a state: the last step down may be a part of
        // the task above it, or a child that is made anew when the level has none there.
        private ISavedState? FindState(ISaveArguments call, ref int next)
        {
            if (call.Count < next + 3)
            {
                return null;
            }

            GameTask? at = Anchor(call, ref next);
            int levels = call.GetInt32(next++);
            ISavedState? state = levels == 0 && at is not null ? states.StateOf(at) : null;
            for (int level = 0; level < levels; level++)
            {
                string type = call.GetText(next++);
                int place = call.GetInt32(next++);
                if (at is null)
                {
                    continue;
                }

                GameTask parent = at;
                at = ChildrenOfType(parent, type, states).Skip(place).FirstOrDefault();
                if (level < levels - 1)
                {
                    continue;
                }

                if (at is not null)
                {
                    state = states.StateOf(at);
                    continue;
                }

                // Past the tasks of its type: a part, or one to be made.
                int index = place - ChildrenOfType(parent, type, states).Count();
                state = index >= 0 && states.PartsOf(parent).Where(p => p.TypeName == type).Skip(index).FirstOrDefault() is { } part
                    ? part.State
                    : states.Recreate(parent, type, call.GetInt32(next));
            }

            next++;
            return state;
        }

        // The task a hierarchy names, for a field that refers to one.
        private GameTask? Find(ISaveArguments call, ref int next)
        {
            if (call.Count < next + 3)
            {
                return null;
            }

            GameTask? task = Anchor(call, ref next);
            int levels = call.GetInt32(next++);
            for (int level = 0; level < levels; level++)
            {
                string type = call.GetText(next++);
                int place = call.GetInt32(next++);
                task = task is null ? null : ChildrenOfType(task, type, states).Skip(place).FirstOrDefault();
            }

            next++;
            return task;
        }

        // Serialize_GetTaskFromArgList: the ancestor by its id, which must be of the type the save names.
        private GameTask? Anchor(ISaveArguments call, ref int next)
        {
            int id = call.GetInt32(next++);
            string type = call.GetText(next++);
            return byId.GetValueOrDefault(id) is { } task && task.Type.Name == type ? task : null;
        }

        private GameTask? FindReference(ISaveArguments call, StateReader reader)
        {
            int next = reader.Next;
            GameTask? task = Find(call, ref next);
            while (reader.Next < next)
            {
                reader.Int();
            }

            return task;
        }
    }

    /// <summary>A compiled save's call.</summary>
    private sealed class CompiledArguments(QvmCallContext call) : ISaveArguments
    {
        public int Count => call.ArgumentCount;

        public int GetInt32(int index) => call.GetInt32(index);

        public double GetReal(int index) => call.GetSingle(index);

        public string GetText(int index) => call.GetString(index);
    }

    /// <summary>
    /// A text save's call: its arguments as the writer prints them (<see cref="StateWriter"/>) -- decimal integers, reals,
    /// <c>TRUE</c> and <c>FALSE</c>, hexadecimal words and quoted strings.
    /// </summary>
    private sealed class TextArguments : ISaveArguments
    {
        private readonly List<(string Text, bool IsQuoted)> _arguments = [];

        public int Count => _arguments.Count;

        public static bool TryParse(string statement, out string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TextArguments? arguments)
        {
            int open = statement.IndexOf('(', StringComparison.Ordinal);
            int close = statement.LastIndexOf(')');
            name = open > 0 ? statement[..open].Trim() : string.Empty;
            arguments = null;
            if (open <= 0 || close <= open)
            {
                return false;
            }

            arguments = new TextArguments();
            StringBuilder current = new();
            bool quoted = false, wasQuoted = false;
            for (int i = open + 1; i < close; i++)
            {
                char c = statement[i];
                if (quoted && c == '\\' && i + 1 < close)
                {
                    current.Append(statement[++i] switch
                    {
                        't' => '\t',
                        'n' => '\n',
                        'r' => '\r',
                        char other => other,
                    });
                }
                else if (c == '"')
                {
                    quoted = !quoted;
                    wasQuoted = true;
                }
                else if (c == ',' && !quoted)
                {
                    arguments.Add(current, wasQuoted);
                    wasQuoted = false;
                }
                else if (quoted || !char.IsWhiteSpace(c))
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0 || wasQuoted || arguments.Count > 0)
            {
                arguments.Add(current, wasQuoted);
            }

            return true;
        }

        public int GetInt32(int index)
        {
            if (!TryNumber(index, out string text))
            {
                return 0;
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint word) ? unchecked((int)word) : 0;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) ? (int)real : 0;
        }

        public double GetReal(int index)
        {
            if (!TryNumber(index, out string text))
            {
                return 0.0;
            }

            return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? GetInt32(index)
                : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
        }

        public string GetText(int index) => index >= 0 && index < _arguments.Count ? _arguments[index].Text : string.Empty;

        private void Add(StringBuilder current, bool isQuoted)
        {
            _arguments.Add((isQuoted ? current.ToString() : current.ToString().Trim(), isQuoted));
            current.Clear();
        }

        // An argument that is a number: TRUE and FALSE are one and nought.
        private bool TryNumber(int index, out string text)
        {
            text = string.Empty;
            if (index < 0 || index >= _arguments.Count || _arguments[index].IsQuoted)
            {
                return false;
            }

            text = _arguments[index].Text switch
            {
                "TRUE" => "1",
                "FALSE" => "0",
                string other => other,
            };
            return true;
        }
    }
}
