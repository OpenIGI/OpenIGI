using System.Text.RegularExpressions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Flow;

/// <summary>
/// The placed things an objective is done by, each with its own test: the tanks to mine, the papers to take, read from the
/// expression that completes the objective.
/// </summary>
/// <remarks>
/// <para>
/// Not retail. A row links to one place only (<c>LinkToPosition</c>, where the map computer's button looks), so an objective
/// with three tanks to mine shows one of them. The targets are the tasks the row's <c>Complete</c> slot asks about with a
/// field that says a thing was done to them -- <c>GenericTBA_282.isFinished</c> -- and, where the slot reads a counter
/// (<c>EditVariable_301.nValue == 1</c>), the ones the counter's own <c>Add</c> slot asks about, one counter deep, as levels
/// latch each part of an objective in one. A word read negated (<c>!HumanSoldier_423.isDead</c>) is a condition, not a
/// target, and humans, who walk away from where the level placed them, are none.
/// </para>
/// <para>
/// One target is the link's own place told another way, so only an objective of two or more has its targets marked.
/// </para>
/// </remarks>
public sealed partial class ComputerObjectiveTargets
{
    /// <summary>Fewest targets that are marked in place of the link.</summary>
    public const int Fewest = 2;

    private static readonly HashSet<string> DoneFields = new(StringComparer.Ordinal)
    {
        "isFinished", "isPickedUp", "isHacked", "isOpen", "isClosed", "isPressed", "isLastPressed", "isExploded",
        "isDestroyed", "isInUse",
    };

    private readonly List<(GameTask Task, LevelExpression Done)> _targets = [];
    private readonly List<GameTask> _open = [];

    private ComputerObjectiveTargets()
    {
    }

    /// <summary>Gets a value indicating whether the row is done by targets of its own, not marked at its link.</summary>
    public bool Any => _targets.Count > 0;

    /// <summary>Gets the targets not yet done, as of the last <see cref="Update"/>.</summary>
    public IReadOnlyList<GameTask> Open => _open;

    /// <summary>Reads the targets a row's <c>Complete</c> slot names.</summary>
    /// <param name="complete">The slot's text.</param>
    /// <param name="owner">The objective definition, the slot's owner.</param>
    /// <param name="level">The level, whose tasks the words name.</param>
    /// <returns>The targets; none for a row of fewer than <see cref="Fewest"/>.</returns>
    public static ComputerObjectiveTargets Read(string complete, GameTask owner, LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(complete);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(level);
        ComputerObjectiveTargets targets = new();
        if (owner.HasTree)
        {
            targets.Collect(complete, owner, level, counterDepth: 1);
            if (targets._targets.Count < Fewest)
            {
                targets._targets.Clear();
            }
        }

        return targets;
    }

    /// <summary>Tests each target again.</summary>
    /// <param name="level">The level.</param>
    /// <param name="settled">Whether the objective is complete or failed, when none is open.</param>
    public void Update(LevelRun level, bool settled)
    {
        ArgumentNullException.ThrowIfNull(level);
        _open.Clear();
        if (settled)
        {
            return;
        }

        foreach ((GameTask task, LevelExpression done) in _targets)
        {
            if (!task.IsDeleted && !LevelExpression.Flag(level.Evaluate(done)))
            {
                _open.Add(task);
            }
        }
    }

    private void Collect(string source, GameTask owner, LevelRun level, int counterDepth)
    {
        TaskTree tree = owner.Tree;
        foreach (Match word in TaskFieldWord().Matches(source))
        {
            if (word.Groups["not"].Success
                || !int.TryParse(word.Groups["id"].ValueSpan, System.Globalization.CultureInfo.InvariantCulture, out int id)
                || tree.FromId(id) is not { } task
                || !string.Equals(task.Name, word.Groups["name"].Value, StringComparison.Ordinal)
                || ReferenceEquals(task, owner))
            {
                continue;
            }

            string field = word.Groups["field"].Value;
            if (task is EditVariableTask counter)
            {
                if (counterDepth > 0)
                {
                    Collect(counter.Add, counter, level, counterDepth - 1);
                }

                continue;
            }

            if (!DoneFields.Contains(field) || IsHuman(task) || _targets.Exists(t => ReferenceEquals(t.Task, task)))
            {
                continue;
            }

            _targets.Add((task, level.Compile(string.Concat(task.Name, ".", field), owner)));
        }
    }

    // A human walks off from where the level put it; its place is no target's.
    private static bool IsHuman(GameTask task) =>
        task.HasType && task.Type.Name.StartsWith("Human", StringComparison.Ordinal);

    // Type_id.field, the way a slot names a task's variable; a '!' before it reads it negated.
    [GeneratedRegex(@"(?<not>!\s*)?\b(?<name>[A-Za-z][A-Za-z0-9]*_(?<id>\d+))\.(?<field>[A-Za-z]\w*)", RegexOptions.CultureInvariant)]
    private static partial Regex TaskFieldWord();
}
