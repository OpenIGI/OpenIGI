using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Rules;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;

namespace OpenIGI.Editor.Panels;

/// <summary>What a row of the rules list is.</summary>
internal enum RuleRowKind
{
    /// <summary>A rule: its title.</summary>
    Rule,

    /// <summary>What starts the rule.</summary>
    When,

    /// <summary>Something else that must hold.</summary>
    Condition,

    /// <summary>A step.</summary>
    Step,

    /// <summary>The "otherwise" half of an If step.</summary>
    Otherwise,
}

/// <summary>One row of the rules list, kept by its key across rebuilds.</summary>
/// <param name="key">Its key: the rule's or step's id with what kind of row it is.</param>
internal sealed class RuleRow(string key)
{
    public string Key { get; } = key;

    public RuleRowKind Kind { get; set; }

    public Rule? Rule { get; set; }

    public RuleStep? Step { get; set; }

    public int? Index { get; set; }

    public string Label { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public KitIcon Icon { get; set; }

    public bool Dimmed { get; set; }

    public RuleRow? Parent { get; set; }

    public List<RuleRow> Children { get; } = [];
}

/// <content>
/// The lists a rule's lines are picked from: what the mission's objects can say of themselves, what its tasks can be made
/// to do, and the game's script functions. Each also takes what is typed, as the game's own expression.
/// </content>
internal sealed partial class RulesPanel
{
    // A condition: every field the mission's tasks export, said in plain words; or the expression typed.
    private void PickCondition(string placeholder, string current, Action<string> picked)
    {
        if (Root is not { } root || _workspace.Logic.Graph is not { } graph || _text is not { } text)
        {
            return;
        }

        List<PaletteEntry> entries = [];
        foreach (TaskGraphNode node in graph.TaskNodes.Where(static n => n.Task.Id >= 0 && !n.IsGenerated))
        {
            foreach (TaskPin field in node.Definition.Outputs)
            {
                string read = string.Create(CultureInfo.InvariantCulture, $"{node.Task.TypeName}_{node.Task.Id}.{field.Name}");
                // A truth is the condition as it is; a count is "more than none", the reading the levels' own conditions use.
                string expression = field.Type.Kind == ScriptTypeKind.Bool ? read : read + " > 0";
                string said = $"{text.Name(node.Task)} {_host.Vocabulary.StateOf(field.Name) ?? field.Name}";
                entries.Add(new PaletteEntry(field.Type.Kind == ScriptTypeKind.Bool ? said : said + " > 0", () => picked(expression))
                {
                    Detail = expression,
                    Icon = KitIcon.Check,
                });
            }
        }

        CommandPalette palette = CommandPalette.Open(root, entries, $"{placeholder}: pick, or type the game's own expression");
        palette.Typed = text => text.Trim().Length == 0 ? null : new PaletteEntry($"Use: {text.Trim()}", () => picked(text.Trim())) { Detail = "expression" };
        palette.Query = current;
    }

    // Something to make happen: every condition of the mission's tasks that can be made to hold, the title's words first.
    private void PickAction(Action<GraphNodeKey, string> picked)
    {
        if (Root is not { } root || _workspace.Logic.Graph is not { } graph || _text is not { } text)
        {
            return;
        }

        List<(bool Named, PaletteEntry Entry)> entries = [];
        foreach (TaskGraphNode node in graph.TaskNodes.Where(static n => !n.IsGenerated))
        {
            foreach (TaskPin pin in node.Definition.Inputs.Where(static p => p.Role == TextRole.Condition))
            {
                GraphNodeKey key = node.Key;
                string name = pin.Name;
                bool named = _host.Vocabulary.ActionOf(node.Task.TypeName, name) is not null;
                entries.Add((named, new PaletteEntry(text.Action(node.Task, name), () => picked(key, name))
                {
                    Detail = node.Task.TypeName,
                    Icon = KitIcon.ArrowRight,
                }));
            }
        }

        if (entries.Count == 0)
        {
            _notify("This mission has nothing a rule can make happen yet: add a message, an objective or a door first.", true);
            return;
        }

        CommandPalette.Open(root, [.. entries.OrderByDescending(static e => e.Named).Select(static e => e.Entry)], "What to make happen: type to search");
    }

    // A call: the game's script functions that do something, each with what it does; or the call typed.
    private void PickCall(string current, Action<string> picked)
    {
        if (Root is not { } root)
        {
            return;
        }

        List<PaletteEntry> entries =
        [
            .. _host.Vocabulary.Natives.Values
                .Where(static n => n.Effect == NativeEffect.Action)
                .OrderBy(static n => n.Name, StringComparer.Ordinal)
                .Select(n => new PaletteEntry(
                    $"{n.Name}({string.Join(", ", n.Parameters.Select(static p => p.Name))})",
                    () => picked($"{n.Name}({string.Join(", ", n.Parameters.Select(static p => p.Name))})"))
                {
                    Detail = n.Doc,
                }),
        ];
        CommandPalette palette = CommandPalette.Open(root, entries, "A script function to call: pick, or type the call");
        palette.Typed = text => text.Trim().Length == 0 ? null : new PaletteEntry($"Call: {text.Trim()}", () => picked(text.Trim())) { Detail = "expression" };
        palette.Query = current;
    }

    private void PickSeconds(double current, Action<double> picked)
    {
        if (Root is not { } root)
        {
            return;
        }

        CommandPalette palette = CommandPalette.Open(root, [], "Seconds to wait");
        palette.Typed = text => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0
            ? new PaletteEntry(string.Create(CultureInfo.InvariantCulture, $"Wait {seconds:0.###} s"), () => picked(seconds))
            : null;
        palette.Query = current.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
