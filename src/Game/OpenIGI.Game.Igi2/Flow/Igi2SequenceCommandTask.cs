using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>One step of a sequence of scripts, I.G.I. 2's <c>SequenceCommand</c>.</summary>
/// <remarks>
/// <para>
/// The sequences under a task are its <c>SequenceCommand</c> children, and only the first of them drives
/// (<c>SequenceCommand_RunHandler</c>, <c>igi2 0x492080</c>): unless it has been stopped, each tick it runs the step it is on —
/// itself, when it is on none or the one it was on is gone — with <c>SequenceCommand_SetCommand</c> pointed at it.
/// </para>
/// <para>
/// A step (<c>SequenceCommand_RunSequenceCommandHandler</c>, <c>0x491F70</c>): when its <c>IsRun Expression</c> holds, its
/// <c>Run Expression</c> runs, and then when its <c>IsNext Expression</c> holds its <c>Next Expression</c> runs — which is where
/// a step names the one after it.
/// </para>
/// </remarks>
public sealed partial class Igi2SequenceCommandTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SequenceCommand";

    /// <summary>The native a step names the next one with.</summary>
    public const string SetCommandNative = "SequenceCommand_SetCommand";

    /// <summary>The id that moves the sequence to the step after the one it is on.</summary>
    public const int NextStep = -2;

    private Igi2LevelExpression? _isRun;
    private Igi2LevelExpression? _run;
    private Igi2LevelExpression? _isNext;
    private Igi2LevelExpression? _next;

    /// <summary>Gets or sets the script that says whether the step runs.</summary>
    [ScriptParam(ScriptParamType.VarString, "IsRun Expression")]
    public string IsRunScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the step's script.</summary>
    [ScriptParam(ScriptParamType.VarString, "Run Expression")]
    public string RunScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that says whether the step is done.</summary>
    [ScriptParam(ScriptParamType.VarString, "IsNext Expression")]
    public string IsNextScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script run when it is done, which names the next step.</summary>
    [ScriptParam(ScriptParamType.VarString, "Next Expression")]
    public string NextScript { get; set; } = string.Empty;

    /// <summary>Gets the step a driving command is on, <c>ptQTaskRef</c>, or <see langword="null"/>.</summary>
    public Igi2SequenceCommandTask? Current { get; private set; }

    /// <summary>Gets a value indicating whether a driving command has stopped for good, <c>isStopped</c>.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>Registers <c>SequenceCommand_SetCommand</c>, which steers the sequence its symbol's context drives.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    /// <remarks>
    /// <c>SequenceCommand_SetNextCommandScriptFunction</c> (<c>igi2 0x4921A0</c>): a level task's id that is a step moves the sequence to
    /// it; <see cref="NextStep"/>, or no argument, moves it to the step after the one it is on, and off the end to none — so
    /// back to the first; any other id stops it.
    /// </remarks>
    public static void Register(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        functions.Register(natives, SetCommandNative, static call =>
        {
            if (call.Context is Igi2SequenceCommandTask driver)
            {
                driver.SetCommand(call.Count > 0 ? call.GetInt32(0) : NextStep);
            }

            return QvmValue.Zero;
        }, 1);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Parent is not { } parent || FirstStep(parent) != this || IsStopped)
        {
            return;
        }

        if (Current is null || Current.IsDeleted)
        {
            Current = this;
        }

        level.Natives.SetSymbolContext(SetCommandNative, this);
        try
        {
            Current.Step(level);
        }
        finally
        {
            level.Natives.SetSymbolContext(SetCommandNative, null);
        }
    }

    private static Igi2SequenceCommandTask? FirstStep(GameTask parent) => parent.Children.OfType<Igi2SequenceCommandTask>().FirstOrDefault();

    private void Step(Igi2LevelRun level)
    {
        _isRun ??= new Igi2LevelExpression(IsRunScript, Name, level.Natives);
        _run ??= new Igi2LevelExpression(RunScript, Name, level.Natives);
        _isNext ??= new Igi2LevelExpression(IsNextScript, Name, level.Natives);
        _next ??= new Igi2LevelExpression(NextScript, Name, level.Natives);
        if (_isRun.IsEmpty || (uint)Igi2LevelExpression.Truncate(level.Evaluate(_isRun)) == 0)
        {
            return;
        }

        if (!_run.IsEmpty)
        {
            level.Evaluate(_run);
        }

        if (!_isNext.IsEmpty && (uint)Igi2LevelExpression.Truncate(level.Evaluate(_isNext)) != 0 && !_next.IsEmpty)
        {
            level.Evaluate(_next);
        }
    }

    private void SetCommand(int id)
    {
        if (id >= 0 && id < TaskTree.IdCapacity)
        {
            if (Tree.FromId(id) is Igi2SequenceCommandTask step)
            {
                Current = step;
            }
            else
            {
                IsStopped = true;
            }

            return;
        }

        if (id != NextStep)
        {
            IsStopped = true;
            return;
        }

        GameTask? next = Current?.NextSibling;
        while (next is not null and not Igi2SequenceCommandTask)
        {
            next = next.NextSibling;
        }

        Current = next as Igi2SequenceCommandTask;
    }
}
