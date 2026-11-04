using System.Globalization;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>One setting shown on a task node: its name and its value as it reads.</summary>
/// <param name="Label">The parameter's name.</param>
/// <param name="Value">Its value: a message's text, a number, a choice.</param>
/// <param name="Full">The whole value with where it came from, for the tooltip.</param>
public sealed record TaskNodeDetail(string Label, string Value, string Full);

/// <summary>
/// Which of a task's settings its node shows under its pins, as a node editor shows a node's defaults on it: the ones that say
/// what the task does (a message's text, a sound, a count, a choice), never the bulky ones (a position, a model, a curve),
/// which the Inspector edits.
/// </summary>
public static class TaskNodeDetails
{
    /// <summary>The most settings a node shows; the Inspector has the rest.</summary>
    public const int Most = 4;

    /// <summary>The settings a task's node shows, text first.</summary>
    /// <param name="node">The task's node.</param>
    /// <param name="text">Turns a text resource into the words it stands for, or <see langword="null"/> when it is not one.</param>
    /// <returns>The settings, at most <see cref="Most"/>.</returns>
    public static IReadOnlyList<TaskNodeDetail> For(TaskGraphNode node, Func<string, string?>? text = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        HashSet<string> pins = [.. node.Definition.Inputs.Where(static p => p.IsLinkable).Select(static p => p.Name)];
        List<(int Rank, TaskNodeDetail Detail)> shown = [];
        foreach (MissionParameter parameter in node.Task.Parameters)
        {
            if (pins.Contains(parameter.Name) || parameter.Declaration.Type is not { } declared || Rank(declared) is not { } rank || parameter.Arguments.Count != 1)
            {
                continue;
            }

            string written = Written(parameter.Arguments[0]);
            if (written.Length == 0)
            {
                continue;
            }

            // A text resource reads as its words, the key kept for the tooltip.
            if (text?.Invoke(written) is { Length: > 0 } words && !words.Equals(written, StringComparison.Ordinal))
            {
                shown.Add((0, new TaskNodeDetail(parameter.Name, $"“{words}”", $"{parameter.Name}: {written}\n“{words}”")));
                continue;
            }

            shown.Add((rank, new TaskNodeDetail(parameter.Name, written, $"{parameter.Name}: {written}")));
        }

        return [.. shown.OrderBy(static s => s.Rank).Take(Most).Select(static s => s.Detail)];
    }

    // Text first, then choices, then numbers; null for what a node does not show.
    private static int? Rank(ScriptParamType type) => type switch
    {
        // A VarString that is not a pin (a condition or action) is text: a status message's Text, a name.
        ScriptParamType.VarString or ScriptParamType.String16 or ScriptParamType.String32 or ScriptParamType.String256 or ScriptParamType.QTaskNote => 1,
        ScriptParamType.EnumString16 or ScriptParamType.EnumString32 or ScriptParamType.EnumString256 or ScriptParamType.EnumInt32
            or ScriptParamType.EnumReal32 or ScriptParamType.DropDownCombo or ScriptParamType.Bool8 => 2,
        ScriptParamType.Int32 or ScriptParamType.Int16 or ScriptParamType.Real32 or ScriptParamType.Real64 or ScriptParamType.RangeReal32
            or ScriptParamType.Angle or ScriptParamType.Degrees or ScriptParamType.WaterLevel or ScriptParamType.ScriptFile => 3,
        _ => null,
    };

    private static string Written(QscExpression argument) => argument switch
    {
        QscStringLiteral text => text.Value.Trim(),
        QscIntLiteral number => number.Value.ToString(CultureInfo.InvariantCulture),
        QscRealLiteral real => real.Value.ToString("0.###", CultureInfo.InvariantCulture),
        _ => QscPrinter.Print(argument),
    };
}
