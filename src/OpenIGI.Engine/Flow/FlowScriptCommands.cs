using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Flow;

/// <summary>
/// The script surface of the flow state machine: one command and five constants.
/// </summary>
/// <remarks>
/// Registered by the original at <c>0x401FC0</c>, alongside the <c>Flow</c> task type itself. This is
/// how mission and menu scripts drive the game between states — <c>Flow_RequestEvent(FLOW_EVENT_GAME)</c>
/// and friends.
/// </remarks>
public sealed class FlowScriptCommands
{
    /// <summary>The script name of the state-request command.</summary>
    public const string RequestEvent = "Flow_RequestEvent";

    private readonly FlowTask _flow;

    /// <summary>Initializes a new instance of the <see cref="FlowScriptCommands"/> class.</summary>
    /// <param name="flow">The flow task requests are routed to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="flow"/> is <see langword="null"/>.</exception>
    public FlowScriptCommands(FlowTask flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        _flow = flow;
    }

    /// <summary>The script-visible names of the five flow events that have one, paired with their values.</summary>
    /// <remarks>
    /// Events 6, 7 and 8 are deliberately absent: the original registers no constants for them and no
    /// script can request them.
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<string, FlowEvent>> Constants { get; } =
    [
        new("FLOW_EVENT_QUIT", FlowEvent.Quit),
        new("FLOW_EVENT_INTRO", FlowEvent.Intro),
        new("FLOW_EVENT_MAINMENU", FlowEvent.MainMenu),
        new("FLOW_EVENT_GAME", FlowEvent.Game),
        new("FLOW_EVENT_RESTART_GAME", FlowEvent.RestartGame),
    ];

    /// <summary>Registers the command and the constants in a script namespace.</summary>
    /// <param name="symbols">The namespace to register into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        symbols.RegisterFunction(RequestEvent, OnRequestEvent, argumentCount: 1);

        foreach ((string name, FlowEvent value) in Constants)
        {
            symbols.RegisterConstant(name, (int)value);
        }
    }

    private QvmValue OnRequestEvent(QvmCallContext call)
    {
        // The original passes the raw integer straight through to the pending slot without checking it,
        // and the switch that reads it back ignores anything outside 1..8. An unknown value therefore
        // clears any event already pending and then does nothing, which is worth reproducing.
        _flow.Request(call.ArgumentCount > 0 ? (FlowEvent)call.GetInt32(0) : FlowEvent.None);
        return QvmValue.Zero;
    }
}
