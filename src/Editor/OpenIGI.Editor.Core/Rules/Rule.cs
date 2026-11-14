using OpenIGI.Editor.Core.Graphs;

namespace OpenIGI.Editor.Core.Rules;

/// <summary>One thing a rule does, in the order its rule does them.</summary>
/// <param name="Id">Its identity, stable across saves: the tasks it is compiled to are kept by it.</param>
public abstract record RuleStep(string Id);

/// <summary>Calls a native for what it does (<c>Game_DisableMusic()</c>).</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Call">The call, as level-expression text.</param>
public sealed record CallStep(string Id, string Call) : RuleStep(Id);

/// <summary>Makes a task's condition hold from this point on: shows a message, completes an objective, opens a door.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Target">The task, by its graph key.</param>
/// <param name="Pin">The condition parameter driven (<c>Send</c>, <c>Complete</c>).</param>
public sealed record DriveStep(string Id, GraphNodeKey Target, string Pin) : RuleStep(Id);

/// <summary>Waits before the steps after it.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Seconds">How long, in seconds of game time.</param>
public sealed record WaitStep(string Id, double Seconds) : RuleStep(Id);

/// <summary>
/// Does one list of steps or another by a condition read once, when the rule reaches it. A wait inside delays only what is
/// inside with it; the steps after the block do not wait for it.
/// </summary>
/// <param name="Id">Its identity.</param>
/// <param name="Condition">The condition, as level-expression text.</param>
/// <param name="Then">The steps done when it holds.</param>
/// <param name="Otherwise">The steps done when it does not.</param>
public sealed record IfStep(string Id, string Condition, IReadOnlyList<RuleStep> Then, IReadOnlyList<RuleStep> Otherwise) : RuleStep(Id);

/// <summary>Runs a project macro in place, its parameters filled in.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Macro">The macro's name.</param>
/// <param name="Arguments">The values given to its parameters, by name.</param>
public sealed record MacroStep(string Id, string Macro, IReadOnlyDictionary<string, string> Arguments) : RuleStep(Id);

/// <summary>
/// One piece of mission logic as a sentence: when something happens, if some things hold, do these steps in order, once.
/// </summary>
/// <remarks>
/// The games have no events: every task reads its conditions again each tick. A rule is the event-style reading of the
/// idiom their own levels use for "once, when": a latch that counts to one the first tick its condition holds, with
/// everything that follows waiting on the latch. A rule is kept as that chain of action nodes
/// (<see cref="Graphs.Exec.ExecLayer"/>), and compiled into the same tasks a retail level would hold.
/// </remarks>
/// <param name="Id">Its identity, stable across saves.</param>
/// <param name="When">What starts it, as level-expression text: the rule runs the first tick this holds.</param>
/// <param name="Steps">What it does, in order.</param>
public sealed record Rule(string Id, string When, IReadOnlyList<RuleStep> Steps)
{
    /// <summary>Gets what the rule is called in the list; empty for one not named.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the group it is listed under, or <see langword="null"/>.</summary>
    public string? Group { get; init; }

    /// <summary>Gets a value indicating whether the rule is switched on; one switched off is kept but compiled to nothing.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets what else must hold, the tick <see cref="When"/> does, for the rule to run: each as level-expression text.</summary>
    public IReadOnlyList<string> Conditions { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the rule's chain was wired in the graph in a way a list of steps cannot say (two
    /// waits side by side, a step two chains run into): it is listed, with no steps, and edited in the graph.
    /// </summary>
    public bool IsFreeform { get; init; }

    /// <summary>Gets every step of the rule, those inside its blocks too, in the order they are written.</summary>
    /// <returns>The steps.</returns>
    public IEnumerable<RuleStep> AllSteps() => Flatten(Steps);

    private static IEnumerable<RuleStep> Flatten(IReadOnlyList<RuleStep> steps)
    {
        foreach (RuleStep step in steps)
        {
            yield return step;
            if (step is IfStep block)
            {
                foreach (RuleStep inner in Flatten(block.Then).Concat(Flatten(block.Otherwise)))
                {
                    yield return inner;
                }
            }
        }
    }
}
