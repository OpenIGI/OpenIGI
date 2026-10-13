using System.Diagnostics.CodeAnalysis;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>A native that both a compiled script and a task's script text can call.</summary>
/// <param name="call">The call, whichever way it was made.</param>
/// <returns>The value the call leaves.</returns>
public delegate QvmValue Igi2ScriptFunction(Igi2ScriptCall call);

/// <summary>One call of an <see cref="Igi2ScriptFunction"/>: its arguments, read as the native asks for them, and its context.</summary>
/// <remarks>
/// A native in <c>igi2.exe</c> is handed an argument list that is either a QVM call's or a parsed expression's, and asks
/// which (<c>ArgList_s.isQVM</c>) before it reads each argument — <c>QVM_EvalArg</c> for the one,
/// <c>Expression_EvalString</c> or <c>Expression_EvalInt</c> for the other (<c>Human_AddWeaponCB</c>, <c>0x4FB740</c>).
/// This is that question asked once. Either way an argument is evaluated only when it is read.
/// </remarks>
public sealed class Igi2ScriptCall
{
    private readonly QvmCallContext? _compiled;
    private readonly ScriptArguments? _expression;

    /// <summary>Initializes a new instance of the <see cref="Igi2ScriptCall"/> class for a compiled script's call.</summary>
    /// <param name="call">The call.</param>
    public Igi2ScriptCall(QvmCallContext call)
    {
        ArgumentNullException.ThrowIfNull(call);
        _compiled = call;
        Count = call.ArgumentCount;
        Context = call.Context;
    }

    /// <summary>Initializes a new instance of the <see cref="Igi2ScriptCall"/> class for an expression's call.</summary>
    /// <param name="arguments">The unevaluated arguments.</param>
    /// <param name="context">The object bound to the native's symbol.</param>
    public Igi2ScriptCall(ScriptArguments arguments, object? context)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        _expression = arguments;
        Count = arguments.Count;
        Context = context;
    }

    /// <summary>Gets the number of arguments passed.</summary>
    public int Count { get; }

    /// <summary>Gets the object bound to the native's symbol (<c>Script_SetSymbolContext</c>), or <see langword="null"/>.</summary>
    public object? Context { get; }

    /// <summary>Evaluates an argument as an integer.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The integer, or zero.</returns>
    public int GetInt32(int index) => _compiled?.GetInt32(index) ?? _expression!.GetInt32(index);

    /// <summary>Evaluates an argument as a real number, <c>Script_EvalArgFloat</c> (<c>igi2 0x40DE00</c>).</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The number, or zero.</returns>
    public double GetReal(int index) => _compiled?.GetSingle(index) ?? _expression!.GetReal(index);

    /// <summary>Evaluates an argument as text.</summary>
    /// <param name="index">The zero-based argument position.</param>
    /// <returns>The text, or empty.</returns>
    public string GetText(int index) => _compiled?.GetString(index) ?? _expression!.GetText(index);

    /// <summary>Reads an argument as a float, as a compiled call's <c>GetSingle</c> does.</summary>
    /// <param name="index">The argument.</param>
    /// <returns>Its value.</returns>
    public float GetSingle(int index) => (float)GetReal(index);

    /// <summary>Reads an argument as text, as a compiled call's <c>GetString</c> does.</summary>
    /// <param name="index">The argument.</param>
    /// <returns>Its value.</returns>
    public string GetString(int index) => GetText(index);

    /// <summary>
    /// Registers a native both for compiled scripts and for level expressions, or only for compiled scripts when there is no function
    /// table: the one call shape the natives both reach are written against.
    /// </summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The function table level expressions call, or <see langword="null"/>.</param>
    /// <param name="name">The name.</param>
    /// <param name="function">The body.</param>
    /// <param name="argumentCount">The declared argument count.</param>
    public static void Register(NativeRegistry natives, Igi2ScriptFunctions? functions, string name, Igi2ScriptFunction function, int argumentCount)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(function);
        if (functions is not null)
        {
            functions.Register(natives, name, function, argumentCount);
        }
        else
        {
            natives.RegisterFunction(name, call => function(new Igi2ScriptCall(call)), argumentCount);
        }
    }
}

/// <summary>The natives a task's script text may call, by name, alongside their registration for compiled scripts.</summary>
/// <remarks>
/// A compiled script reaches a native through <see cref="NativeRegistry"/>, which hands it a <see cref="QvmCallContext"/>;
/// script text evaluated as an expression (<see cref="Igi2LineScript"/>) has no such context to give. Registering a native
/// here does both, so one body serves both callers as it does in the original.
/// </remarks>
public sealed class Igi2ScriptFunctions
{
    private readonly Dictionary<string, Igi2ScriptFunction> _functions = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the level whose tasks the natives find by their level id, <c>Level_atLevelQTaskID</c>, or
    /// <see langword="null"/> before a level runs.
    /// </summary>
    public TaskTree? Level { get; set; }

    /// <summary>Registers a native for both kinds of caller.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="name">The script-visible name.</param>
    /// <param name="function">The body.</param>
    /// <param name="argumentCount">The declared argument count.</param>
    public void Register(NativeRegistry natives, string name, Igi2ScriptFunction function, int argumentCount)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(function);

        _functions[name] = function;
        natives.RegisterFunction(name, call => function(new Igi2ScriptCall(call)), argumentCount);
    }

    /// <summary>Finds a native by name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="function">Receives the body.</param>
    /// <returns><see langword="true"/> when one is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out Igi2ScriptFunction? function) => _functions.TryGetValue(name, out function);
}
