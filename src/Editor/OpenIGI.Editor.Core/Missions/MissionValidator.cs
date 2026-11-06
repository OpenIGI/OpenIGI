using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Projects;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>How serious a problem is.</summary>
public enum IssueSeverity
{
    /// <summary>Worth knowing; the mission still works.</summary>
    Warning,

    /// <summary>The mission will not work as written in the retail game.</summary>
    Error,
}

/// <summary>One way to put a problem right, as an undoable step.</summary>
/// <param name="Label">What the button says (<c>Give It a Free Id</c>).</param>
/// <param name="Make">Makes the step, or null when it can no longer be done.</param>
public sealed record IssueFix(string Label, Func<IEditorCommand?> Make);

/// <summary>One problem found in a mission.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Task">The task it is about, or <see langword="null"/> for the mission as a whole.</param>
/// <param name="Message">What is wrong, in a sentence.</param>
public sealed record MissionIssue(IssueSeverity Severity, MissionTask? Task, string Message)
{
    /// <summary>Gets the check that found it, by name (<c>Duplicate ids</c>).</summary>
    public string Check { get; init; } = string.Empty;

    /// <summary>Gets the ways to put it right, the first the one a "fix all" uses.</summary>
    public IReadOnlyList<IssueFix> Fixes { get; init; } = [];
}

/// <summary>What a check can ask about the running level, beyond the mission's own script.</summary>
public interface IMissionFacts
{
    /// <summary>Gets what the mission is made to run in.</summary>
    MissionTarget Target { get; }

    /// <summary>Gets whether the level knows a model by the name level data gives it, or null when that cannot be told.</summary>
    /// <param name="model">The model's name.</param>
    /// <returns>Whether it exists.</returns>
    bool? HasModel(string model);

    /// <summary>Gets the ground's height under a point, as the game stands on it, or null where there is none.</summary>
    /// <param name="pointMetres">The point, in world metres.</param>
    /// <returns>The height, in world metres.</returns>
    double? GroundUnder(Vector3d pointMetres);

    /// <summary>Gets the title's script vocabulary, which the graph check reads expressions against, or null without one.</summary>
    Scripting.Vocabulary.ScriptVocabulary? Vocabulary => null;

    /// <summary>Gets which game's level expression rules the mission's text follows.</summary>
    Scripting.Expressions.LevelExpressionDialect Dialect => Scripting.Expressions.LevelExpressionDialect.Igi1;

    /// <summary>Gets the step that makes the mission OpenIGI only, or null.</summary>
    IEditorCommand? MakeOpenIgiOnly();
}

/// <summary>One kind of check, with what it looks for, for the analysis's list of checks.</summary>
/// <param name="Name">Its name (<c>Duplicate ids</c>).</param>
/// <param name="Explanation">What it looks for and why it matters.</param>
public sealed record MissionCheck(string Name, string Explanation);

/// <summary>
/// Checks a mission against the rules the retail game enforces silently, so a mistake is found in the editor rather than
/// as a mission that loads and then misbehaves; each problem comes with what it means and, where there is one, a fix.
/// </summary>
/// <remarks>
/// Checks that need the running level (models, the ground, the mission's target) run only when given its facts; the
/// rest read the script alone, so they run in tests and before an export too.
/// </remarks>
public static partial class MissionValidator
{
    /// <summary>Gets every check, in the order they report.</summary>
    public static IReadOnlyList<MissionCheck> Checks { get; } =
    [
        new(IdsCheck, "An id past the game's 4096-slot table, or used twice: the game makes the task but nothing can find it by id."),
        new(DeclarationCheck, "Values that do not match the task type's declaration: kept as written, but they cannot be edited."),
        new(ReferenceCheck, "A condition, a field read or a reference naming a task the mission does not have, or the wrong kind: it reads 0, or drives nothing."),
        new(LogicCheck, "An expression the game cannot read, or a call or name it does not know: the game treats it as empty, or reads 0."),
        new(ModelCheck, "A model the level does not have: the object is not drawn and nothing collides with it."),
        new(GroundCheck, "A soldier or pickup well above or below the ground: it falls when the level starts, or is buried."),
        new(TargetCheck, "Something the mission's target cannot run: OpenIGI features in a mission made for the original game."),
    ];

    /// <summary>Checks a mission from its script alone.</summary>
    /// <param name="document">The mission.</param>
    /// <returns>The problems, errors first.</returns>
    public static IReadOnlyList<MissionIssue> Validate(MissionDocument document) => Validate(document, facts: null);

    /// <summary>Checks a mission, with what the running level can say about it.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="facts">The running level's facts, or null to check the script alone.</param>
    /// <returns>The problems, errors first, then by check.</returns>
    public static IReadOnlyList<MissionIssue> Validate(MissionDocument document, IMissionFacts? facts)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<MissionIssue> issues = [];
        CheckIds(document, issues);
        CheckDeclarations(document, issues);
        CheckGraph(document, facts, issues);
        if (facts is not null)
        {
            CheckModels(document, facts, issues);
            CheckGround(document, facts, issues);
        }

        return [.. issues.OrderByDescending(static i => i.Severity).ThenBy(static i => CheckOrder(i.Check))];
    }

    private static int CheckOrder(string check)
    {
        for (int i = 0; i < Checks.Count; i++)
        {
            if (Checks[i].Name == check)
            {
                return i;
            }
        }

        return Checks.Count;
    }
}
