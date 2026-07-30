using OpenIGI.Core.Diagnostics;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Ai;

/// <summary>An action an archetype script asked for, as <c>AIAction_*</c> produced it.</summary>
/// <param name="Name">The native's name.</param>
/// <param name="Arguments">Its evaluated integer arguments, in order.</param>
/// <param name="Slot">
/// The dispatch position the action was created at — the original's <c>dword_5BDA64</c>, which becomes the
/// action's <c>+8</c> and so the interrupt cap while it runs (<c>ai.md</c> §3.5).
/// </param>
public readonly record struct AiProducedAction(string Name, IReadOnlyList<int> Arguments, int Slot)
{
    /// <summary>Gets an argument, or zero when the script passed fewer than that.</summary>
    /// <param name="index">The argument index.</param>
    /// <returns>The value.</returns>
    public int Argument(int index) => index >= 0 && index < Arguments.Count ? Arguments[index] : 0;
}

/// <summary>
/// Runs a soldier's archetype behaviour script for real — the 55 AI natives bound to a live soldier, so
/// <c>guard.qsc</c> decides what a guard does rather than the port deciding for it.
/// </summary>
/// <remarks>
/// <para>
/// The original passes the AI to its natives through <b>four globals</b> (<c>ai.md</c> §3.6):
/// <c>dword_5BDA58</c> the current AI, <c>5BDA5C</c> the list the <c>AIAction_*</c> calls append to,
/// <c>5BDA60</c> the queue the event is read from and <c>5BDA64</c> the dispatch position. That is why no AI
/// native takes the AI as an argument and <c>AIFunction_GetCurrentEventType()</c> has arity zero. This host is
/// those globals: <see cref="Bind"/> sets them, the script runs, and <see cref="Produced"/> is the result.
/// </para>
/// <para>
/// Every native in <see cref="AiScriptApi"/> is registered with the arity and return kind the registrar
/// declares, so a script cannot call one that is missing. The <c>AIAction_*</c> ones record what was asked for
/// rather than performing it, which is exactly what the original does — the action is taken by the pump
/// afterwards (<c>0x450E70</c> for one, <c>0x450F10</c> for several).
/// </para>
/// </remarks>
public sealed class AiScriptHost
{
    private readonly NativeRegistry _natives;
    private readonly IIgiLogger _logger;

    // Faults already reported, so a per-tick failure is logged once rather than for ever.
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<AiProducedAction> _produced = [];
    private readonly Dictionary<int, int> _scriptIntegers = [];
    private readonly Dictionary<int, float> _scriptReals = [];
    private AiSoldier? _current;
    private AiEvent _currentEvent;
    private int _slot;
    private QvmProgram? _archetypeScript;
    private QvmProgram? _runningScript;
    private uint _random = 0x1234_5678;

    /// <summary>Initializes a new instance of the <see cref="AiScriptHost"/> class.</summary>
    /// <param name="natives">The registry the archetype scripts resolve against.</param>
    /// <param name="logger">Where to report a script asking for something unmodelled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public AiScriptHost(NativeRegistry natives, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(natives);
        _natives = natives;
        _logger = logger ?? NullLogger.Instance;
        RegisterAll();
    }

    /// <summary>Gets the actions the last script run asked for, in the order it asked.</summary>
    public IReadOnlyList<AiProducedAction> Produced => _produced;

    /// <summary>Gets the event the running script is handling.</summary>
    public AiEvent CurrentEvent => _currentEvent;

    /// <summary>Runs a script for one soldier handling one event.</summary>
    /// <param name="script">The compiled script — typically the per-task override.</param>
    /// <param name="soldier">The soldier the natives act on.</param>
    /// <param name="handling">The event being dispatched.</param>
    /// <param name="slot">The event's position in the priority order.</param>
    /// <param name="archetype">
    /// The archetype script (e.g. <c>guard.qvm</c>), or <see langword="null"/> when this IS the archetype.
    /// When <c>AIFunction_DefaultHandler</c> is called, this script runs for the current event — which is
    /// how the original delegates from a per-task script to the shared archetype.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the script produced at least one action — the original's "handled", which
    /// is what stops a lower-priority event getting a turn this tick.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public bool Run(QvmProgram script, AiSoldier soldier, AiEvent handling, int slot, QvmProgram? archetype = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(soldier);

        Bind(soldier, handling, slot);
        _archetypeScript = archetype;
        _runningScript = script;
        try
        {
            QvmInterpreter interpreter = new(script, _natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                // Once per distinct fault. A script that names a task the level does not build faults on
                // EVERY tick of every soldier, which buries everything else in the log -- level 1's two
                // CutScene tasks are 58 warnings a second on their own.
                string fault = $"The archetype script returned {error} handling {handling}: {interpreter.ErrorMessage}";
                if (_reported.Add(fault))
                {
                    _logger.Warning("Ai", fault);
                }
            }
        }
        catch (EngineFatalException ex)
        {
            _logger.Warning("Ai", $"The archetype script faulted handling {handling}: {ex.Message}");
            return false;
        }
        finally
        {
            _archetypeScript = null;
            _runningScript = null;
        }

        return _produced.Count > 0;
    }

    private void Bind(AiSoldier soldier, AiEvent handling, int slot)
    {
        _current = soldier;
        _currentEvent = handling;
        _slot = slot;
        _produced.Clear();
    }

    private void RegisterAll()
    {
        foreach (AiNative native in AiScriptApi.Natives)
        {
            if (_natives.Contains(native.Name))
            {
                continue;
            }

            QvmValueKind kind = native.Return switch
            {
                AiNativeReturn.Real => QvmValueKind.Float,
                AiNativeReturn.Int => QvmValueKind.Integer,
                _ => QvmValueKind.Integer,
            };

            AiNative captured = native;
            _natives.RegisterFunction(
                native.Name,
                call => Invoke(captured, call),
                native.ArgumentCount,
                kind);
        }

        foreach ((string name, int value) in AiScriptApi.Constants)
        {
            if (!_natives.Contains(name))
            {
                _natives.RegisterConstant(name, value);
            }
        }
    }

    private QvmValue Invoke(AiNative native, QvmCallContext call)
    {
        if (native.IsAction)
        {
            int[] arguments = new int[call.ArgumentCount];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = call.GetInt32(i);
            }

            _produced.Add(new AiProducedAction(native.Name, arguments, _slot));
            return QvmValue.Zero;
        }

        AiSoldier? ai = _current;
        switch (native.Name)
        {
            case "AIFunction_GetCurrentEventType":
                return QvmValue.FromInt32((int)_currentEvent);

            case "AIFunction_DefaultHandler":
                if (_archetypeScript is not null && _archetypeScript != _runningScript && ai is not null)
                {
                    QvmProgram archScript = _archetypeScript;
                    QvmProgram? prevRunning = _runningScript;
                    _archetypeScript = null;
                    _runningScript = archScript;
                    try
                    {
                        QvmInterpreter inner = new(archScript, _natives);
                        inner.Run();
                    }
                    catch (EngineFatalException)
                    {
                    }
                    finally
                    {
                        _archetypeScript = archScript;
                        _runningScript = prevRunning;
                    }
                }

                return QvmValue.Zero;

            case "AIFunction_GetRandomValue":
                return QvmValue.FromSingle(NextRandom() * Math.Max(0f, call.GetSingle(0)));

            case "AIFunction_SetViewLength" when ai is not null:
                ai.Vision.Primary = ai.Vision.Primary.WithLengthInMetres(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetAlarmViewLength" when ai is not null:
                ai.Vision.Primary = ai.Vision.Primary.WithAlarmLengthInMetres(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetViewAlpha" when ai is not null:
                ai.Vision.Primary = ai.Vision.Primary.WithAlphaInDegrees(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetViewGamma" when ai is not null:
                ai.Vision.Primary = ai.Vision.Primary.WithGammaInDegrees(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetSecondaryViewLength" when ai is not null:
                ai.Vision.Secondary = ai.Vision.Secondary.WithLengthInMetres(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetSecondaryAlarmViewLength" when ai is not null:
                ai.Vision.Secondary = ai.Vision.Secondary.WithAlarmLengthInMetres(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetSecondaryViewAlpha" when ai is not null:
                ai.Vision.Secondary = ai.Vision.Secondary.WithAlphaInDegrees(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetSecondaryViewGamma" when ai is not null:
                ai.Vision.Secondary = ai.Vision.Secondary.WithGammaInDegrees(call.GetSingle(0));
                return QvmValue.Zero;

            case "AIFunction_SetScriptIntegerValue":
                _scriptIntegers[call.GetInt32(0)] = call.GetInt32(1);
                return QvmValue.Zero;

            case "AIFunction_GetScriptIntegerValue":
                return QvmValue.FromInt32(_scriptIntegers.GetValueOrDefault(call.GetInt32(0)));

            case "AIFunction_SetScriptRealValue":
                _scriptReals[call.GetInt32(0)] = call.GetSingle(1);
                return QvmValue.Zero;

            case "AIFunction_GetScriptRealValue":
                return QvmValue.FromSingle(_scriptReals.GetValueOrDefault(call.GetInt32(0)));

            case "AIFunction_GetEventDistance" when ai is not null:
                return QvmValue.FromSingle((float)ai.LastEventDistance);

            case "AIFunction_IsEventBehind":
                return QvmValue.FromInt32(0);

            case "AIFunction_GetAlarmControlStatus" when ai is not null:
                return QvmValue.FromInt32(ai.Alerted ? 1 : 0);

            case "AIFunction_GetAlarmTriggerDistance":
                // 0x44E9B0 returns -1.0 for every failure mode, and no trigger is configured here.
                return QvmValue.FromSingle(-1.0f);

            default:
                // Everything else is a setter or getter whose state nothing in this path reads yet. The
                // return kind still has to be right, because the VM types a call by its symbol's tag.
                return native.Return == AiNativeReturn.Real
                    ? QvmValue.FromSingle(0f)
                    : QvmValue.FromInt32(native.Name.EndsWith("ID", StringComparison.Ordinal) ? -1 : 0);
        }
    }

    // The engine's own 0..1 generator is IgiRandom; this only has to be deterministic and in range, because
    // the retail scripts use it for idle timing rather than anything that has to match tick for tick.
    private float NextRandom()
    {
        _random = (_random * 1664525u) + 1013904223u;
        return (_random >> 8) / (float)(1 << 24);
    }
}
