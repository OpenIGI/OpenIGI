using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>One top-level statement of a mission script.</summary>
public abstract class MissionStatement
{
    private protected MissionStatement()
    {
    }
}

/// <summary>A <c>Task_DeclareParameters</c> statement.</summary>
/// <param name="declaration">The declaration.</param>
public sealed class MissionDeclarationStatement(TaskDeclaration declaration) : MissionStatement
{
    /// <summary>Gets the declaration.</summary>
    public TaskDeclaration Declaration { get; } = declaration;
}

/// <summary>A top-level <c>Task_New</c>: the root of one object tree.</summary>
/// <param name="task">The task.</param>
public sealed class MissionTaskStatement(MissionTask task) : MissionStatement
{
    /// <summary>Gets the task.</summary>
    public MissionTask Task { get; } = task;
}

/// <summary>Any other statement, carried verbatim.</summary>
/// <param name="statement">The statement.</param>
public sealed class MissionScriptStatement(QscStatement statement) : MissionStatement
{
    /// <summary>Gets the statement.</summary>
    public QscStatement Statement { get; } = statement;
}

/// <summary>
/// A mission's object script (<c>objects.qvm</c>) as an editable model: the task-type declarations and the
/// object trees, with everything else carried verbatim.
/// </summary>
/// <remarks>
/// <para>
/// It sits on the QSC syntax tree, which already survives image → tree → image byte for byte, and adds only
/// a split that it undoes exactly: <c>Task_DeclareParameters</c> calls become <see cref="TaskDeclaration"/>s
/// and <c>Task_New</c> calls become <see cref="MissionTask"/>s. Saving an unchanged document therefore gives
/// back the retail file, which is what lets a vanilla export load in the retail executable.
/// </para>
/// </remarks>
public sealed class MissionDocument
{
    /// <summary>The native that declares a task type's parameters.</summary>
    public const string DeclareFunction = "Task_DeclareParameters";

    /// <summary>The native that creates a task.</summary>
    public const string NewFunction = TaskScriptCommands.TaskNew;

    private readonly QscScript _header;

    private MissionDocument(QscScript header) => _header = header;

    /// <summary>Gets the bytecode version the document saves as: 5 for I.G.I., 7 for I.G.I. 2.</summary>
    public int Version => _header.Version;

    /// <summary>Gets the top-level statements, in order.</summary>
    public List<MissionStatement> Statements { get; } = [];

    /// <summary>Gets the declarations by task type name; when a type is declared twice the first one wins, as it does in the engine.</summary>
    public Dictionary<string, TaskDeclaration> Declarations { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads a compiled mission script.</summary>
    /// <param name="image">The QVM image.</param>
    /// <returns>The document.</returns>
    public static MissionDocument Read(ReadOnlyMemory<byte> image) => Read(QscDecompiler.Decompile(image));

    /// <summary>Reads a mission script from its syntax tree.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is <see langword="null"/>.</exception>
    public static MissionDocument Read(QscScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        MissionDocument document = new(script);

        foreach (QscStatement statement in script.Statements)
        {
            document.Statements.Add(document.ReadStatement(statement));
        }

        return document;
    }

    /// <summary>Enumerates every task of every tree, depth first in file order.</summary>
    /// <returns>The tasks.</returns>
    public IEnumerable<MissionTask> EnumerateTasks() =>
        Statements.OfType<MissionTaskStatement>().SelectMany(static s => s.Task.SelfAndSubtree());

    /// <summary>Gets the top-level tasks.</summary>
    public IEnumerable<MissionTask> RootTasks => Statements.OfType<MissionTaskStatement>().Select(static s => s.Task);

    /// <summary>Builds the syntax tree the document saves as.</summary>
    /// <returns>The script.</returns>
    public QscScript ToScript() =>
        new([.. Statements.Select(WriteStatement)], _header.Version)
        {
            RequestedStackSizeBytes = _header.RequestedStackSizeBytes,
            RequestedMaxLocals = _header.RequestedMaxLocals,
            FileInfo = _header.FileInfo,
        };

    /// <summary>Compiles the document to a QVM image.</summary>
    /// <returns>The image.</returns>
    public byte[] ToImage() => QscCompiler.Compile(ToScript());

    /// <summary>Gives a task the parameters its type declares, with zero values, ready to be filled in.</summary>
    /// <param name="task">The task, typically new.</param>
    /// <returns><see langword="false"/> when the document declares no such type.</returns>
    public bool ApplyDeclaration(MissionTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!Declarations.TryGetValue(task.TypeName, out TaskDeclaration? declaration))
        {
            return false;
        }

        task.Declaration = declaration;
        task.UnnamedArguments = null;
        task.Parameters.Clear();
        foreach (TaskParameterDeclaration parameter in declaration.Parameters)
        {
            // An AnimData parameter starts empty: a zero count and no entries.
            int width = parameter.Type is { } type ? ScriptParamTypeInfo.GetArgumentCount(type, leadingCount: 0) : 1;
            QscExpression zero = parameter.Type is { } t && ScriptParamTypeInfo.IsText(t)
                ? new QscStringLiteral(string.Empty)
                : new QscIntLiteral(0);
            task.Parameters.Add(new MissionParameter(parameter, Enumerable.Repeat(zero, width)));
        }

        return true;
    }

    private MissionStatement ReadStatement(QscStatement statement)
    {
        if (statement is QscExpressionStatement { Expression: QscCall call })
        {
            if (call.Name == DeclareFunction && TryReadDeclaration(call, out TaskDeclaration? declaration))
            {
                Declarations.TryAdd(declaration.TypeName, declaration);
                return new MissionDeclarationStatement(declaration);
            }

            if (call.Name == NewFunction && TryReadTask(call, out MissionTask? task))
            {
                return new MissionTaskStatement(task);
            }
        }

        return new MissionScriptStatement(statement);
    }

    private static bool TryReadDeclaration(QscCall call, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TaskDeclaration? declaration)
    {
        declaration = null;
        string[] texts = new string[call.Arguments.Count];
        for (int i = 0; i < texts.Length; i++)
        {
            if (!ScriptValues.TryGetString(call.Arguments[i], out texts[i]))
            {
                return false;
            }
        }

        if (texts.Length == 0 || texts.Length % 2 == 0)
        {
            return false;
        }

        TaskParameterDeclaration[] parameters = new TaskParameterDeclaration[(texts.Length - 1) / 2];
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i] = new TaskParameterDeclaration(texts[1 + (2 * i)], texts[2 + (2 * i)]);
        }

        declaration = new TaskDeclaration(texts[0], parameters);
        return true;
    }

    private bool TryReadTask(QscCall call, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MissionTask? task)
    {
        task = null;
        if (call.Arguments.Count < 3
            || !ScriptValues.TryGetString(call.Arguments[1], out string typeName)
            || !ScriptValues.TryGetString(call.Arguments[2], out string label))
        {
            return false;
        }

        task = new MissionTask(typeName, label) { IdExpression = call.Arguments[0] };
        Declarations.TryGetValue(typeName, out TaskDeclaration? declaration);
        task.Declaration = declaration;

        // Children are the trailing run of nested Task_New calls: a parameter value is never a call, so the run
        // cannot reach back into the values however the type is declared.
        int firstChild = call.Arguments.Count;
        while (firstChild > 3 && call.Arguments[firstChild - 1] is QscCall { Name: NewFunction })
        {
            firstChild--;
        }

        List<QscExpression> values = [.. call.Arguments.Take(firstChild).Skip(3)];
        if (declaration is null || !TrySplit(values, declaration, task.Parameters))
        {
            task.Parameters.Clear();
            task.UnnamedArguments = values;
        }

        for (int i = firstChild; i < call.Arguments.Count; i++)
        {
            QscCall nested = (QscCall)call.Arguments[i];
            if (TryReadTask(nested, out MissionTask? child))
            {
                task.AddChild(child);
            }
            else
            {
                // A Task_New too malformed to read (its type or label not a string) stays a verbatim argument,
                // so the run of children ends here and the rest are carried as they were.
                task.Parameters.Clear();
                task.Children.ForEach(static c => c.Parent = null);
                task.Children.Clear();
                task.UnnamedArguments = [.. call.Arguments.Skip(3)];
                break;
            }
        }

        return true;
    }

    private static bool TrySplit(List<QscExpression> values, TaskDeclaration declaration, List<MissionParameter> parameters)
    {
        int cursor = 0;
        foreach (TaskParameterDeclaration parameter in declaration.Parameters)
        {
            if (parameter.Type is not { } type)
            {
                return false;
            }

            int leadingCount = 0;
            if (ScriptParamTypeInfo.IsVariableLength(type)
                && (cursor >= values.Count || !ScriptValues.TryGetInteger(values[cursor], out leadingCount)))
            {
                return false;
            }

            int width = ScriptParamTypeInfo.GetArgumentCount(type, leadingCount);
            if (cursor + width > values.Count)
            {
                return false;
            }

            parameters.Add(new MissionParameter(parameter, values.Skip(cursor).Take(width)));
            cursor += width;
        }

        return cursor == values.Count;
    }

    private static QscStatement WriteStatement(MissionStatement statement) => statement switch
    {
        MissionDeclarationStatement declaration => new QscExpressionStatement(WriteDeclaration(declaration.Declaration)),
        MissionTaskStatement task => new QscExpressionStatement(WriteTask(task.Task)),
        MissionScriptStatement script => script.Statement,
        _ => throw new ArgumentException($"Unknown statement {statement.GetType().Name}.", nameof(statement)),
    };

    private static QscCall WriteDeclaration(TaskDeclaration declaration)
    {
        List<QscExpression> arguments = [new QscStringLiteral(declaration.TypeName)];
        foreach (TaskParameterDeclaration parameter in declaration.Parameters)
        {
            arguments.Add(new QscStringLiteral(parameter.Name));
            arguments.Add(new QscStringLiteral(parameter.TypeName));
        }

        return new QscCall(DeclareFunction, arguments);
    }

    private static QscCall WriteTask(MissionTask task)
    {
        List<QscExpression> arguments = [task.IdExpression, new QscStringLiteral(task.TypeName), new QscStringLiteral(task.Label)];
        if (task.UnnamedArguments is { } unnamed)
        {
            arguments.AddRange(unnamed);
        }
        else
        {
            foreach (MissionParameter parameter in task.Parameters)
            {
                arguments.AddRange(parameter.Arguments);
            }
        }

        arguments.AddRange(task.Children.Select(WriteTask));

        return new QscCall(NewFunction, arguments);
    }
}
