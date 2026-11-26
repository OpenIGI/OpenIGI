namespace OpenIGI.Editor.Core.Rules;

/// <summary>Where in a rule a new step goes.</summary>
/// <param name="Anchor">The step it goes after, or the block it goes into; <see langword="null"/> for the end of the rule.</param>
/// <param name="Inside">For a block anchor: whether it goes into the block (at its end) rather than after it.</param>
/// <param name="Otherwise">For a step going into a block: whether into its "otherwise" steps.</param>
public readonly record struct StepPlace(string? Anchor, bool Inside = false, bool Otherwise = false);

/// <summary>Changes to a rule's steps, each giving the rule as it then stands.</summary>
public static class RuleEdits
{
    /// <summary>Adds a step.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="step">The step.</param>
    /// <param name="place">Where it goes.</param>
    /// <returns>The rule with the step; at its end when the anchor is not one of its steps.</returns>
    public static Rule Insert(Rule rule, RuleStep step, StepPlace place)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(step);
        if (place.Anchor is not { } anchor || !rule.AllSteps().Any(s => s.Id == anchor))
        {
            return rule with { Steps = [.. rule.Steps, step] };
        }

        return rule with
        {
            Steps = Map(rule.Steps, list =>
            {
                int at = IndexOf(list, anchor);
                if (at < 0)
                {
                    return list;
                }

                if (place.Inside && list[at] is IfStep block)
                {
                    IfStep grown = place.Otherwise ? block with { Otherwise = [.. block.Otherwise, step] } : block with { Then = [.. block.Then, step] };
                    return [.. list.Take(at), grown, .. list.Skip(at + 1)];
                }

                return [.. list.Take(at + 1), step, .. list.Skip(at + 1)];
            }),
        };
    }

    /// <summary>Takes a step out, with everything inside it.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="stepId">The step.</param>
    /// <returns>The rule without it.</returns>
    public static Rule Remove(Rule rule, string stepId)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule with { Steps = Map(rule.Steps, list => IndexOf(list, stepId) < 0 ? list : [.. list.Where(s => s.Id != stepId)]) };
    }

    /// <summary>Puts another step in a step's place.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="stepId">The step.</param>
    /// <param name="change">Makes the step that takes its place.</param>
    /// <returns>The rule with the step changed.</returns>
    public static Rule Replace(Rule rule, string stepId, Func<RuleStep, RuleStep> change)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(change);
        return rule with { Steps = Map(rule.Steps, list => IndexOf(list, stepId) < 0 ? list : [.. list.Select(s => s.Id == stepId ? change(s) : s)]) };
    }

    /// <summary>Moves a step earlier or later among the steps beside it.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="stepId">The step.</param>
    /// <param name="by">How many places: negative for earlier.</param>
    /// <returns>The rule with the step moved, as far as its list goes.</returns>
    public static Rule Move(Rule rule, string stepId, int by)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule with
        {
            Steps = Map(rule.Steps, list =>
            {
                int at = IndexOf(list, stepId);
                if (at < 0)
                {
                    return list;
                }

                List<RuleStep> moved = [.. list];
                RuleStep step = moved[at];
                moved.RemoveAt(at);
                moved.Insert(Math.Clamp(at + by, 0, moved.Count), step);
                return moved;
            }),
        };
    }

    /// <summary>Finds a step by its id, inside blocks too.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="stepId">The step.</param>
    /// <returns>The step, or <see langword="null"/>.</returns>
    public static RuleStep? Find(Rule rule, string stepId)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.AllSteps().FirstOrDefault(s => s.Id == stepId);
    }

    private static int IndexOf(IReadOnlyList<RuleStep> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    // Every list of steps of a rule put through a change, the innermost first.
    private static IReadOnlyList<RuleStep> Map(IReadOnlyList<RuleStep> steps, Func<IReadOnlyList<RuleStep>, IReadOnlyList<RuleStep>> change)
    {
        IReadOnlyList<RuleStep> inner = [.. steps.Select(step => step is IfStep block
            ? block with { Then = Map(block.Then, change), Otherwise = Map(block.Otherwise, change) }
            : step)];
        return change(inner);
    }
}
