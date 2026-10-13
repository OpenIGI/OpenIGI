using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Everything one I.G.I. 2 mission's scripts reach for that the title does not yet implement.
/// </summary>
/// <remarks>
/// <para>
/// <b>The script set is the retail engine's, not a guess at it.</b> <c>igi2.exe</c> names every script
/// it runs: the level's <c>%s/objects.qsc</c> and <c>MISSION:AI/%d.qsc</c>,
/// <c>LOCAL:common/ai/{default,squaddefault,settings}.qsc</c>, <c>LOCAL:humanplayer/humanplayer.qsc</c>,
/// <c>LOCAL:weapons/{weapon,ammo}.qsc</c>, <c>LOCAL:material/material.qsc</c>,
/// <c>LOCAL:menusystem/ingamemenu.qsc</c>, <c>LOCAL:config.qsc</c>, <c>magicobjconfig.qsc</c>, the
/// <c>sounds.qsc</c> of <c>LOCAL:common/sounds/</c> and <c>MISSION:sounds/</c>, and every script under
/// <c>LOCAL:physicsobj</c> and <c>LOCAL:animtrigger</c> (the <c>*.qvm</c> directory scan). The
/// front end's <c>mainmenu.qsc</c> is not a mission script and is left out; so is <c>lod.qvm</c>,
/// which no string in the game binary names.
/// </para>
/// <para>
/// Three kinds of gap are reported, because a script can depend on the engine in three ways:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>type X</c>: a <c>Task_New</c> spawns a type that has no class, so it loads as a
/// <see cref="DeclaredTask"/> placeholder and does nothing.
/// </description></item>
/// <item><description>
/// <c>name X</c>: a symbol a script's own table names, or an identifier inside a <c>VarString</c>
/// expression such as a <c>LevelFlow</c> condition, resolves to nothing once the level is loaded. That
/// covers natives, constants and the variables tasks export (<c>AreaActivate_150.nActive</c>), because
/// all three live in the same namespace.
/// </description></item>
/// <item><description>
/// <c>survey X</c>: a script could not even be run far enough to read its spawns, which hides every
/// other gap inside it.
/// </description></item>
/// </list>
/// <para>
/// A name that does not resolve does not fail in either engine: a call does nothing and a value reads
/// as zero. That is why the census exists — every gap here is behaviour the mission silently lacks.
/// </para>
/// </remarks>
internal static class Igi2MissionCensus
{
    /// <summary>Surveys one campaign level.</summary>
    /// <param name="location">The location number, e.g. 1 for <c>location1</c>.</param>
    /// <param name="level">The level number within it.</param>
    /// <returns>Every gap, sorted, each with the scripts it was found in.</returns>
    public static SortedDictionary<string, SortedSet<string>> Survey(int location, int level)
    {
        string root = GameDataSource.Igi2.RequireRoot();
        string levelDirectory = Path.Combine(root, "MISSIONS", $"location{location}", $"level{level}");
        string objects = Path.Combine(levelDirectory, "objects.qvm");

        // The tree is loaded for real, through the title's own session: that is what exports each
        // task's variables into the namespace, and what tells a class from a placeholder. The session's
        // tables are loaded first, as the original loads them before any level: they register names --
        // the ammunition's and the weapons' ids -- that only exist once their scripts have run.
        RetailIgi2LevelLoadTests.Harness harness = new(objects, loadTables: true);
        new World.Igi2MissionCatalogue(NullLogger.Instance).Register(harness.Loop.Natives);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

        NativeRegistry natives = harness.Loop.Natives;
        TaskTypeRegistry types = harness.Loop.Types;
        SortedDictionary<string, SortedSet<string>> gaps = new(StringComparer.Ordinal);

        foreach (string script in ScriptsOf(root, levelDirectory))
        {
            string label = Path.GetRelativePath(root, script).Replace('\\', '/');
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(script));

            foreach (string? name in program.SymbolNames)
            {
                if (!string.IsNullOrEmpty(name) && !natives.Contains(name) && !RetailUnresolved.ContainsKey(name))
                {
                    Add(gaps, $"name {name}", label);
                }
            }

            if (!program.SymbolNames.Contains("Task_New"))
            {
                continue;
            }

            Spawns spawns;
            try
            {
                spawns = SpawnsOf(program, natives, types);
            }
            catch (Exception ex) when (ex is InvalidOperationException or EngineFatalException)
            {
                Add(gaps, $"survey {label}", ex.Message);
                continue;
            }

            foreach (string type in spawns.Types)
            {
                if (!types.TryGet(type, out TaskType? registered) || registered.IsDeclarationOnly)
                {
                    Add(gaps, $"type {type}", label);
                }
            }

            foreach (string expression in spawns.Expressions)
            {
                foreach (string name in NamesIn(expression, gaps, label))
                {
                    if (!natives.Contains(name) && !RetailUnresolved.ContainsKey(name))
                    {
                        Add(gaps, $"name {name}", label);
                    }
                }
            }
        }

        return gaps;
    }

    /// <summary>Gets every script the retail engine runs for a level, in a stable order.</summary>
    /// <param name="root">The installation root.</param>
    /// <param name="levelDirectory">The level's directory.</param>
    /// <returns>The scripts that exist on disk.</returns>
    public static IEnumerable<string> ScriptsOf(string root, string levelDirectory)
    {
        string[] fixedScripts =
        [
            Path.Combine(levelDirectory, "objects.qvm"),
            Path.Combine(levelDirectory, "mission.qvm"),
            Path.Combine(levelDirectory, "sounds", "sounds.qvm"),
            Path.Combine(root, "COMMON", "ai", "default.qvm"),
            Path.Combine(root, "COMMON", "ai", "squaddefault.qvm"),
            Path.Combine(root, "COMMON", "ai", "settings.qvm"),
            Path.Combine(root, "COMMON", "sounds", "sounds.qvm"),
            Path.Combine(root, "HUMANPLAYER", "humanplayer.qvm"),
            Path.Combine(root, "WEAPONS", "weapon.qvm"),
            Path.Combine(root, "WEAPONS", "ammo.qvm"),
            Path.Combine(root, "MATERIAL", "material.qvm"),
            Path.Combine(root, "MENUSYSTEM", "ingamemenu.qvm"),
            Path.Combine(root, "config.qvm"),
            Path.Combine(root, "magicobjconfig.qvm"),
        ];

        string[] scannedDirectories =
        [
            Path.Combine(levelDirectory, "ai"),
            Path.Combine(root, "PHYSICSOBJ"),
            Path.Combine(root, "ANIMTRIGGER"),
        ];

        IEnumerable<string> scanned = scannedDirectories
            .Where(Directory.Exists)
            .SelectMany(static d => Directory.EnumerateFiles(d, "*.qvm", SearchOption.AllDirectories))
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase);

        return fixedScripts.Where(File.Exists).Concat(scanned);
    }

    private static Spawns SpawnsOf(QvmProgram program, NativeRegistry live, TaskTypeRegistry types)
    {
        // A scratch namespace: the script runs to its end, but builds nothing and changes nothing in the
        // session being surveyed. Every other symbol the script names is mirrored from the live session
        // so constants carry their real values, and a name the session lacks reads as zero -- as it would
        // in the engine -- rather than stopping the survey.
        Spawns spawns = new();
        Dictionary<string, List<TaskParameter>> declared = new(StringComparer.Ordinal);
        NativeRegistry scratch = new();

        scratch.RegisterFunction("Task_DeclareParameters", call =>
        {
            List<TaskParameter> parameters = [];
            for (int i = 1; i + 1 < call.ArgumentCount; i += 2)
            {
                string name = call.GetString(i);
                if (ScriptParamTypeInfo.TryParseName(call.GetString(i + 1), out ScriptParamType type))
                {
                    parameters.Add(new TaskParameter(name, type));
                }
            }

            declared[call.GetString(0)] = parameters;
            return QvmValue.Zero;
        });

        scratch.RegisterFunction("Task_New", call =>
        {
            string type = call.GetString(1);
            spawns.Types.Add(type);

            IReadOnlyList<TaskParameter> parameters =
                declared.TryGetValue(type, out List<TaskParameter>? local) ? local
                : types.TryGet(type, out TaskType? known) ? known.Parameters
                : [];

            // Every argument is evaluated exactly once: a nested Task_New is itself an argument, so
            // skipping one would skip a whole subtree. VarString parameters are read as text and kept.
            HashSet<int> expressionArguments = [];
            int offset = 3;
            foreach (TaskParameter parameter in parameters)
            {
                if (ScriptParamTypeInfo.IsVariableLength(parameter.Type))
                {
                    break;
                }

                if (parameter.Type == ScriptParamType.VarString && !NotCode.Contains((type, parameter.Name)))
                {
                    expressionArguments.Add(offset);
                }

                offset += parameter.ArgumentCount;
            }

            for (int i = 2; i < call.ArgumentCount; i++)
            {
                if (expressionArguments.Contains(i))
                {
                    string text = call.GetString(i);
                    if (!ScriptExpressionParser.IsBlank(text))
                    {
                        spawns.Expressions.Add(text);
                    }
                }
                else
                {
                    call.GetValue(i);
                }
            }

            return QvmValue.Zero;
        });

        foreach (string? name in program.SymbolNames)
        {
            if (string.IsNullOrEmpty(name) || scratch.Contains(name))
            {
                continue;
            }

            if (live.TryResolve(name, out QvmSymbol? symbol) && symbol.Kind == QvmSymbolKind.Function)
            {
                scratch.RegisterFunction(name, static _ => QvmValue.Zero);
            }
            else if (symbol is not null && symbol.Kind == QvmSymbolKind.Constant)
            {
                QvmValue value = symbol.Read();
                if (value.Kind == QvmValueKind.Float)
                {
                    scratch.RegisterRealConstant(name, value.Number);
                }
                else
                {
                    scratch.RegisterConstant(name, value.Integer);
                }
            }
            else
            {
                scratch.RegisterConstant(name, 0);
            }
        }

        QvmInterpreter interpreter = new(program, scratch);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new InvalidOperationException($"{error}: {interpreter.ErrorMessage}");
        }

        return spawns;
    }

    private static List<string> NamesIn(
        string script,
        SortedDictionary<string, SortedSet<string>> gaps,
        string label)
    {
        // A VarString holding code is a statement list, not one expression: the soldiers' "Weapon"
        // parameter reads "Human_AddWeapon(...), Human_AddWeapon(...); // Sniper", with line comments
        // and lines commented out entirely. Comments are stripped to the end of the line and the text is
        // split at statement ends; each piece is then an expression the engine's parser reads.
        List<string> names = [];
        foreach (string statement in Statements(script))
        {
            ScriptExpression parsed;
            try
            {
                parsed = ScriptExpressionParser.Parse(statement);
            }
            catch (ScriptExpressionException ex)
            {
                if (!RetailUnparsable.Contains(Flatten(statement)))
                {
                    Add(gaps, $"survey expression {Flatten(statement)}", $"{label}: {ex.Message}");
                }

                continue;
            }

            Collect(parsed, names);
        }

        return names;
    }

    private const char LineBreak = '\n';

    private static readonly char[] Whitespace = ['\r', '\n', '\t'];

    private static IEnumerable<string> Statements(string script)
    {
        System.Text.StringBuilder code = new();
        foreach (string line in script.Split(LineBreak))
        {
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            code.Append(comment >= 0 ? line[..comment] : line).Append(' ');
        }

        // Statement ends outside string literals. No literal in the corpus holds a semicolon, but
        // splitting inside one would invent a syntax error.
        List<string> statements = [];
        System.Text.StringBuilder current = new();
        bool quoted = false;
        foreach (char c in code.ToString())
        {
            if (c == '"')
            {
                quoted = !quoted;
            }

            if (c == ';' && !quoted)
            {
                statements.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        statements.Add(current.ToString());
        return statements.Where(static st => !ScriptExpressionParser.IsBlank(st));
    }

    /// <summary>An expression's text with its whitespace runs collapsed to single spaces, as the ledger lists it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The flattened text.</returns>
    internal static string Flatten(string text) =>
        string.Join(' ', text.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static void Collect(ScriptExpression? node, List<string> names)
    {
        if (node is null)
        {
            return;
        }

        // "this.x" names a field of the task evaluating the expression, not a symbol in the namespace.
        if (node.Kind == ScriptExpressionKind.Identifier
            && !node.Text.StartsWith("this.", StringComparison.Ordinal))
        {
            names.Add(node.Text);
        }

        Collect(node.Left, names);
        Collect(node.Right, names);
        Collect(node.Extra, names);
        foreach (ScriptExpression argument in node.Arguments)
        {
            Collect(argument, names);
        }
    }

    private static void Add(SortedDictionary<string, SortedSet<string>> gaps, string gap, string where)
    {
        if (!gaps.TryGetValue(gap, out SortedSet<string>? sources))
        {
            gaps[gap] = sources = new SortedSet<string>(StringComparer.Ordinal);
        }

        sources.Add(where);
    }

    /// <summary>
    /// Names the shipped scripts reach for that the retail engine never defines either, each with the reason: no engine module
    /// registers the native, or no task of the level carries the id the exported variable is named after. Both engines read them as
    /// nought and call them as nothing, so they are not gaps. <see cref="RetailIgi2CampaignCensusTests"/> checks every task id.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RetailUnresolved = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AIFunction_SetAccuracy"] = "humanai.c registers only AIFunction_SetAccuracyMedRange and AIFunction_SetAccuracyMaxRange",
        ["Human_Weapon"] = "human.c registers no such symbol",
        ["priboy_bed_talking"] = "a clip name written where a symbol is read; nothing registers it",
        ["ColumnView_TestFunctionCB"] = "columnview.c registers no such symbol",
        ["xxx"] = "a placeholder left in two of location3/level4's AI scripts",
        ["AreaActivate_1701.nActive"] = "location1/level7",
        ["AreaActivate_1703.nActive"] = "location1/level7",
        ["AreaActivate_1704.nActive"] = "location1/level7",
        ["AreaActivate_1705.nActive"] = "location1/level7",
        ["AreaActivate_2106.nActive"] = "location1/level5",
        ["EditVariable_118.nValue"] = "location3/level6",
        ["EditVariable_127.nValue"] = "location3/level6",
        ["EditVariable_133.nValue"] = "location3/level6",
        ["EditVariable_134.nValue"] = "location3/level6",
        ["EditVariable_135.nValue"] = "location3/level6",
        ["EditVariable_1504.nValue"] = "location2/level1",
        ["EditVariable_162.nValue"] = "location3/level1",
        ["LevelTimer_1740.nTick"] = "location2/level5",
    };

    /// <summary>
    /// Level expressions, flattened, that the retail parser rejects as the title's does (<c>Expression_Parse</c> reports a missing
    /// <c>)</c> or operand through <c>Script_Error</c>): neither engine compiles them, so the task treats them as absent, which
    /// <see cref="RetailIgi2CampaignCensusTests"/> checks.
    /// </summary>
    internal static readonly IReadOnlySet<string> RetailUnparsable = new HashSet<string>(StringComparer.Ordinal)
    {
        "! (  (AreaActivate_3275.nActive &&   (EditVariable_3243.nValue == 0 || EditVariable_3244.nValue == 0)  ||  AreaActivate_3272.nActive  ||  (AreaActivate_3276.nActive && EditVariable_3244.nValue == 0) )",
        "( EditVariable_322.nValue == 0 && Door_330.isOpen  ) ||  ( EditVariable_322.nValue == 1 && LevelTimer_313.nTick > 2 * GAME_FREQUENCY  ) ||  ( EditVariable_322.nValue == 2 && LevelTimer_313.nTick > 4 * GAME_FREQUENCY  ) ||  ( EditVariable_322.nValue == 3 && LevelTimer_313.nTick > 14 * GAME_FREQUENCY  )  || ( EditVariable_322.nValue == 4 && (  LevelTimer_313.nTick > 18 * GAME_FREQUENCY   ||  EditVariable_324.nValue == 1  )  || ( EditVariable_322.nValue == 5 && LevelTimer_315.nTick > 2 * GAME_FREQUENCY  )",
        "|1",
    };

    /// <summary>VarString parameters whose text is data rather than script.</summary>
    /// <remarks>
    /// <c>StatusMessage.Text</c> is a list of localized-string keys, split at commas, tabs and newlines
    /// with <c>//</c> comments skipped, each key looked up through
    /// <c>Config_GetLocalizedStringForLanguage</c> — <c>StatusMessage_UpdateText</c>
    /// (<c>igi2 0x50F830</c>). It is never compiled, so its words are not names.
    /// <para>
    /// <c>EditBoneObj</c>'s <c>Animation list</c> is a list of clip names, cut at <c>", \t\n"</c> and
    /// handed one at a time to <c>EditBoneObj_SetAnimation</c> — <c>EditBoneObj_UpdateAnimation</c>
    /// (<c>igi2 0x5AA7C0</c>).
    /// </para>
    /// </remarks>
    private static readonly HashSet<(string Type, string Parameter)> NotCode =
    [
        ("StatusMessage", "Text"),
        ("EditBoneObj", "Animation list"),
    ];

    private sealed class Spawns
    {
        public HashSet<string> Types { get; } = new(StringComparer.Ordinal);

        public List<string> Expressions { get; } = [];
    }
}
