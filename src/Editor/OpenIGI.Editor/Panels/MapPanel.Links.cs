using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Rules;
using OpenIGI.Editor.Viewport;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;

namespace OpenIGI.Editor.Panels;

/// <content>
/// Wiring one object to another by dragging between them with Ctrl held: a guard to an alarm panel or a mounted gun becomes
/// one of the guard's orders; anything else is a rule -- what the second object does, then which state of the first starts
/// it -- picked from lists said in the title's words, and kept in the Rules panel with every other rule.
/// </content>
internal sealed partial class MapPanel
{
    /// <summary>Offers the ways one object can be wired to another, as dragging between them with Ctrl held does.</summary>
    /// <param name="from">The object dragged from.</param>
    /// <param name="to">The object dragged to.</param>
    public void OfferLinks(MissionTask from, MissionTask to)
    {
        if (Root is not { } root || _workspace.Document is not { } document || _workspace.Logic.Graph is not { } graph)
        {
            return;
        }

        RuleText text = new(document, graph, _host.Vocabulary, _host.MissionText);
        List<PaletteEntry> entries = [];

        // A guard's own orders first: the alarm it answers, the gun it mans.
        if (PatrolPaths.IsSoldier(from) && to.Id >= 0)
        {
            if (to.TypeName == "AlarmControl")
            {
                entries.Add(new PaletteEntry($"{text.Name(from)} answers this alarm", () => GiveOrders(from, o => o with { AlarmControlId = to.Id })) { Icon = KitIcon.Person, Detail = "guard's orders" });
            }
            else if (to.TypeName == "StationaryGun")
            {
                entries.Add(new PaletteEntry($"{text.Name(from)} mans this gun", () => GiveOrders(from, o => o with { GunnerId = to.Id })) { Icon = KitIcon.Person, Detail = "guard's orders" });
            }
        }

        // Then what the second object can be made to do, the title's words first.
        if (graph.TryGetNode(to, out TaskGraphNode? target) && graph.TryGetNode(from, out TaskGraphNode? source))
        {
            foreach (TaskPin pin in target.Definition.Inputs.Where(static p => p.Role == TextRole.Condition)
                .OrderByDescending(p => _host.Vocabulary.ActionOf(to.TypeName, p.Name) is not null))
            {
                string name = pin.Name;
                entries.Add(new PaletteEntry(text.Action(to, name), () => PickTrigger(text, source, target, name)) { Icon = KitIcon.ArrowRight, Detail = "rule" });
            }
        }

        if (entries.Count == 0)
        {
            _notify?.Invoke($"Nothing about {text.Name(to)} can be started by {text.Name(from)}.", false);
            return;
        }

        CommandPalette.Open(root, entries, $"Link {text.Name(from)} to {text.Name(to)}: what should happen?");
    }

    // The second half of a rule made by dragging: which state of the first object starts it.
    private void PickTrigger(RuleText text, TaskGraphNode source, TaskGraphNode target, string pin)
    {
        if (Root is not { } root)
        {
            return;
        }

        List<PaletteEntry> entries = [];
        foreach (TaskPin field in source.Definition.Outputs)
        {
            string read = string.Create(CultureInfo.InvariantCulture, $"{source.Task.TypeName}_{source.Task.Id}.{field.Name}");
            string when = field.Type.Kind == ScriptTypeKind.Bool ? read : read + " > 0";
            entries.Add(new PaletteEntry($"when {text.Condition(when)}", () => MakeRule(text, when, target, pin)) { Icon = KitIcon.Check, Detail = when });
        }

        if (entries.Count == 0 || source.Task.Id < 0)
        {
            _notify?.Invoke($"{text.Name(source.Task)} says nothing of itself a rule can wait for.", false);
            return;
        }

        CommandPalette.Open(root, entries, $"{text.Action(target.Task, pin)} -- when {text.Name(source.Task)}…");
    }

    private void MakeRule(RuleText text, string when, TaskGraphNode target, string pin)
    {
        ExecLayer layer = _workspace.Logic.Layout.Actions;
        string id = RuleBook.NewId(layer);
        string step = RuleBook.NewId(layer, [id]);
        Rule rule = new(id, when, [new DriveStep(step, target.Key, pin)]) { Name = $"{text.Action(target.Task, pin)}, when {text.Condition(when)}" };
        _workspace.Execute(new SetActionsCommand("Link on Map", _workspace.Logic.Layout, ExecLayerState.Of(layer), RuleBook.With(layer, rule)));
        _notify?.Invoke($"Made the rule \"{rule.Name}\"; it is in the Rules panel, and runs once the mission is saved.", false);
    }

    private void GiveOrders(MissionTask soldier, Func<AiGuardOrders, AiGuardOrders> change)
    {
        if (_guards.Set(soldier, change(_guards.Of(soldier)), out string? refusal) is { } order)
        {
            _workspace.Execute(order);
        }
        else if (refusal is not null)
        {
            _notify?.Invoke(refusal, true);
        }
    }
}
