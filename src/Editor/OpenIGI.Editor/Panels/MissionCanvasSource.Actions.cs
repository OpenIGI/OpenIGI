using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A mission graph's action nodes on the canvas: event-style triggers, delays, branches, calls, drives and macros, wired by
/// exec pins, each drawn in the graph it was put in. They compile into the mission's own tasks when it is saved.
/// </summary>
internal sealed partial class MissionCanvasSource
{
    /// <summary>What an action node's canvas id starts with, so it never meets a task's or an expression's.</summary>
    public const string ActionPrefix = "x:";

    /// <summary>The exec input every action node but an event has.</summary>
    public const string ExecIn = "in";

    /// <summary>A drive node's output, wired to the condition pin it makes hold.</summary>
    public const string DrivesPin = "drives";

    /// <summary>The value pin holding an event's or a branch's condition.</summary>
    public const string ConditionPin = "condition";

    /// <summary>The value pin holding a delay's seconds.</summary>
    public const string SecondsPin = "seconds";

    /// <summary>The value pin holding a call's text.</summary>
    public const string CallPin = "call";

    /// <summary>What a macro node's argument pins start with, before the parameter's name.</summary>
    public const string ArgumentPrefix = "arg:";

    private static readonly Bgra32 ExecInk = Bgra32.FromRgb(0xF0, 0xF0, 0xF0);

    /// <summary>The canvas id of an action node.</summary>
    /// <param name="id">The node's id in the layer.</param>
    /// <returns>The canvas id.</returns>
    public static string CanvasIdOf(string id) => ActionPrefix + id;

    /// <summary>The layer id of an action node from its canvas id, or null for anything else.</summary>
    /// <param name="canvasId">A canvas node's id.</param>
    /// <returns>The layer id.</returns>
    public static string? ActionIdOf(string canvasId) =>
        canvasId.StartsWith(ActionPrefix, StringComparison.Ordinal) ? canvasId[ActionPrefix.Length..] : null;

    /// <summary>The title an action node of a kind is drawn and offered under.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The title.</returns>
    public static string TitleOf(ExecNodeKind kind) => kind switch
    {
        ExecNodeKind.OnBecomesTrue => "On Becomes True",
        ExecNodeKind.Delay => "Delay",
        ExecNodeKind.Branch => "Branch",
        ExecNodeKind.Call => "Call",
        ExecNodeKind.Drive => "Make Hold",
        ExecNodeKind.Macro => "Macro",
        _ => kind.ToString(),
    };

    // The graph's action nodes and their wires, and every drive's wire to the pin it makes hold where that task is drawn.
    private void AddActions(GraphLayout layout, string scope, string firstScope, HashSet<GraphNodeKey> drawn, EditorTheme theme)
    {
        ExecLayer actions = layout.Actions;
        HashSet<string> here = [];
        foreach (ExecNode node in actions.Nodes.Where(n => (n.Scope ?? firstScope) == scope))
        {
            here.Add(node.Id);
            string id = CanvasIdOf(node.Id);
            UiVec2 at = layout.Positions.TryGetValue(new GraphNodeKey(id), out GraphPoint point) ? new UiVec2((float)point.X, (float)point.Y) : default;
            _nodes.Add(ActionNode(node, id, at, actions, theme));
            if (node is { Kind: ExecNodeKind.Drive, Target: { } target } && drawn.Contains(target))
            {
                _wires.Add(new CanvasWire(id, DrivesPin, target.Value, node.TargetPin, PinColour(ScriptType.Bool, theme))
                {
                    Via = ViaOf(layout, id, DrivesPin, target.Value, node.TargetPin),
                });
            }
        }

        foreach (ExecWire wire in actions.Wires.Where(w => here.Contains(w.From) && here.Contains(w.To)))
        {
            string from = CanvasIdOf(wire.From), to = CanvasIdOf(wire.To);
            _wires.Add(new CanvasWire(from, wire.Pin, to, ExecIn, ExecInk)
            {
                IsExec = true,
                Via = ViaOf(layout, from, wire.Pin, to, ExecIn),
            });
        }
    }

    private static CanvasNode ActionNode(ExecNode node, string id, UiVec2 at, ExecLayer actions, EditorTheme theme)
    {
        bool wiredIn = actions.Wires.Any(w => w.To == node.Id);
        CanvasPin execIn = new(ExecIn, string.Empty, ExecInk, IsExec: true) { IsConnected = wiredIn, Tooltip = "Runs when the chain reaches it" };
        CanvasPin Exec(string pin, string label) =>
            new(pin, label, ExecInk, IsExec: true) { IsConnected = actions.Wires.Any(w => w.From == node.Id && w.Pin == pin) };
        CanvasPin Value(string pin, string label, string value, string tooltip) =>
            new(pin, label, PinColour(ScriptType.Text, theme)) { Value = value.Length > 0 ? value : "…", Tooltip = tooltip };

        (string title, Bgra32 header, CanvasPin[] inputs, CanvasPin[] outputs, string? subtitle) = node.Kind switch
        {
            ExecNodeKind.OnBecomesTrue => (TitleOf(node.Kind), Bgra32.FromRgb(0x9C, 0x2F, 0x2F),
                (CanvasPin[])[Value(ConditionPin, "When", node.Condition, "The condition: the chain starts the first tick it holds")],
                (CanvasPin[])[Exec(ExecNode.Then, string.Empty)], "Event"),
            ExecNodeKind.Delay => (TitleOf(node.Kind), Bgra32.FromRgb(0x4A, 0x55, 0x66),
                [execIn, Value(SecondsPin, "Seconds", node.Seconds.ToString("0.##", CultureInfo.InvariantCulture), "How long the chain waits here")],
                [Exec(ExecNode.Then, string.Empty)], null),
            ExecNodeKind.Branch => (TitleOf(node.Kind), Bgra32.FromRgb(0x4A, 0x55, 0x66),
                [execIn, Value(ConditionPin, "If", node.Condition, "Read once, when the chain reaches the branch")],
                [Exec(ExecNode.True, "True"), Exec(ExecNode.False, "False")], null),
            ExecNodeKind.Call => (node.Call.Length > 0 ? node.Call.Split('(')[0].Replace('_', ' ') : TitleOf(node.Kind), Bgra32.FromRgb(0x2F, 0x6C, 0xB5),
                [execIn, Value(CallPin, "Do", node.Call, "A call the game runs, such as Game_DisableMusic()")],
                [Exec(ExecNode.Then, string.Empty)], "Call"),
            ExecNodeKind.Drive => (TitleOf(node.Kind), Bgra32.FromRgb(0x6A, 0x3E, 0x9C),
                [execIn],
                [new CanvasPin(DrivesPin, "Holds", PinColour(ScriptType.Bool, theme)) { IsConnected = node.Target is not null, Tooltip = "Wire to a condition pin: it holds from when the chain gets here" }],
                node.Target is { } target ? $"Makes {target.Value} {node.TargetPin} hold" : "Wire Holds to a condition"),
            ExecNodeKind.Macro => (node.Macro, Bgra32.FromRgb(0x58, 0x3C, 0x8C),
                [execIn, .. node.Arguments.OrderBy(static a => a.Key, StringComparer.Ordinal).Select(a => Value(ArgumentPrefix + a.Key, a.Key, a.Value, $"What ${a.Key} stands for in the macro"))],
                [Exec(ExecNode.Then, string.Empty)], "Macro · double-click to expand"),
            _ => (TitleOf(node.Kind), Bgra32.FromRgb(0x5A, 0x5A, 0x66), [execIn], [Exec(ExecNode.Then, string.Empty)], null),
        };

        return new CanvasNode(id, title, at, header, inputs, outputs)
        {
            Subtitle = subtitle,
            IsEvent = node.Kind == ExecNodeKind.OnBecomesTrue,
            Tooltip = node.Kind == ExecNodeKind.OnBecomesTrue ? "Compiled into an EditVariable latch the original game runs" : null,
        };
    }
}
