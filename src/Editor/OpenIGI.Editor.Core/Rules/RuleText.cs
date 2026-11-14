using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Rules;

/// <summary>A rule's lines said in plain words, from the mission's own names and the title's words for what its tasks do.</summary>
/// <remarks>
/// Only the saying changes: the text kept and compiled is the level expression. A condition that is one field read, or one
/// compared with a value, is said as the task's name and what the field says of it (<c>Terminal_12.isHacked</c> as
/// <c>Main terminal is hacked</c>); anything longer is shown as written.
/// </remarks>
/// <param name="document">The mission, for its tasks' names.</param>
/// <param name="graph">The mission's graph, for the tasks steps drive.</param>
/// <param name="vocabulary">The title's vocabulary, for its words.</param>
/// <param name="missionText">Gives the words a text resource stands for (<c>M1_MSG_3</c>), or null for none; null to name nothing by its text.</param>
public sealed partial class RuleText(MissionDocument document, MissionGraph graph, ScriptVocabulary vocabulary, Func<string, string?>? missionText = null)
{
    private const string TextParameter = "Text";
    private const int LongestQuote = 48;

    /// <summary>
    /// What a task is called in a sentence: its label; or, for one with none that shows a text, the text's own words in
    /// quotes (a message is known by what it says); or its type and id.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The name.</returns>
    public string Name(MissionTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Label.Length == 0 && missionText is not null
            && task.FindParameter(TextParameter) is { Arguments: [Scripting.Qsc.QscStringLiteral { Value: { Length: > 0 } key }] }
            && missionText(key) is { Length: > 0 } words)
        {
            return words.Length > LongestQuote ? $"“{words[..LongestQuote].TrimEnd()}…”" : $"“{words}”";
        }

        return NameOf(task);
    }

    /// <summary>What a task is called in a sentence: its label, or its type and id when it has none.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The name.</returns>
    public static string NameOf(MissionTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Label.Length > 0
            ? task.Label
            : task.Id >= 0 ? string.Create(CultureInfo.InvariantCulture, $"{task.TypeName} {task.Id}") : task.TypeName;
    }

    /// <summary>A condition in plain words.</summary>
    /// <param name="expression">The level-expression text.</param>
    /// <returns>The words, or the text itself when it is more than one field read.</returns>
    /// <remarks>
    /// Levels write their conditions over several lines and in brackets; the words are on one line. A condition made of
    /// field reads joined by <c>||</c> or <c>&amp;&amp;</c> is said part by part; ticks counted against
    /// <c>GAME_FREQUENCY</c> are said in seconds.
    /// </remarks>
    public string Condition(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        string text = Spaces().Replace(expression, " ").Trim();
        if (text.Length == 0)
        {
            return "(nothing yet)";
        }

        return Say(text) ?? text;
    }

    // The words for a condition, or null when some part of it is more than a field read.
    private string? Say(string text)
    {
        text = Unbracket(text);
        switch (text)
        {
            case "1" or "TRUE":
                return "always";
            case "0" or "FALSE":
                return "never";
        }

        foreach ((string op, string word) in (ReadOnlySpan<(string, string)>)[("||", " or "), ("&&", " and ")])
        {
            List<string> parts = SplitTopLevel(text, op);
            if (parts.Count > 1)
            {
                List<string> said = [];
                foreach (string part in parts)
                {
                    if (Say(part) is not { } words)
                    {
                        return null;
                    }

                    said.Add(words);
                }

                return string.Join(word, said);
            }
        }

        return SayRead(text);
    }

    private string? SayRead(string text)
    {
        Match match = FieldRead().Match(text);
        if (!match.Success || !int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            || document.EnumerateTasks().FirstOrDefault(t => t.Id == id && t.TypeName == match.Groups["type"].Value) is not { } task)
        {
            return null;
        }

        string field = match.Groups["field"].Value;
        string said = $"{Name(task)} {vocabulary.StateOf(field) ?? field}";
        if (match.Groups["op"].Success)
        {
            string value = match.Groups["value"].Value;
            Match seconds = Seconds().Match(value);
            said = seconds.Success
                ? $"{said} {match.Groups["op"].Value} {seconds.Groups["n"].Value} s"
                : $"{said} {match.Groups["op"].Value} {value}";
        }

        return match.Groups["not"].Success ? $"not: {said}" : said;
    }

    // The text without brackets that hold all of it.
    private static string Unbracket(string text)
    {
        text = text.Trim();
        while (text.Length > 1 && text[0] == '(' && text[^1] == ')' && Closes(text) == text.Length - 1)
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    // Where the bracket that opens the text closes.
    private static int Closes(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // The parts an operator joins outside any bracket.
    private static List<string> SplitTopLevel(string text, string op)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0 && string.CompareOrdinal(text, i, op, 0, op.Length) == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + op.Length;
                i += op.Length - 1;
            }
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    /// <summary>A step in plain words.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The words.</returns>
    public string Step(RuleStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step switch
        {
            CallStep call => call.Call.Length == 0 ? "Call (nothing yet)" : $"Call {call.Call}",
            DriveStep drive => Drive(drive),
            WaitStep wait => string.Create(CultureInfo.InvariantCulture, $"Wait {wait.Seconds:0.###} s"),
            IfStep block => $"If {Condition(block.Condition)}",
            MacroStep macro => $"Run {macro.Macro}",
            _ => step.ToString() ?? string.Empty,
        };
    }

    /// <summary>What making a task's condition hold does, in plain words, with the task's name.</summary>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The condition parameter.</param>
    /// <returns>The words: the title's (<c>Show the message: Welcome</c>), or the parameter's name where it has none.</returns>
    public string Action(MissionTask task, string parameter)
    {
        ArgumentNullException.ThrowIfNull(task);
        return vocabulary.ActionOf(task.TypeName, parameter) is { } verb
            ? $"{verb}: {Name(task)}"
            : $"Make '{parameter}' hold: {Name(task)}";
    }

    /// <summary>A rule's title in the list: its name, or else what starts it.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The title.</returns>
    public string Title(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Name.Length > 0 ? rule.Name : $"When {Condition(rule.When)}";
    }

    private string Drive(DriveStep drive) =>
        graph.Nodes.GetValueOrDefault(drive.Target) is TaskGraphNode node
            ? Action(node.Task, drive.Pin)
            : "Make a condition hold (its task is gone)";

    // One field read, perhaps negated, perhaps compared with one value.
    [GeneratedRegex(@"^(?<not>!\s*)?(?<type>\w+)_(?<id>\d+)\.(?<field>\w+)(\s*(?<op>==|!=|>=|<=|>|<)\s*(?<value>[\w\.\-]+(\s*\*\s*GAME_FREQUENCY)?))?$")]
    private static partial Regex FieldRead();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // A count of ticks written as seconds of the game's 30 a second.
    [GeneratedRegex(@"^(?<n>\d+(\.\d+)?)\s*\*\s*GAME_FREQUENCY$")]
    private static partial Regex Seconds();
}
