using System.Globalization;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>What the guide knows besides the mission itself.</summary>
/// <param name="Document">The mission.</param>
/// <param name="HasPlayed">Whether it has been played from the editor since the guide began.</param>
public readonly record struct GuideFacts(MissionDocument Document, bool HasPlayed);

/// <summary>One step of the guide: what to do, how, and whether the mission shows it done.</summary>
/// <param name="Title">What to do, as an instruction.</param>
/// <param name="Hint">How, in a line.</param>
/// <param name="Show">What the editor opens or runs for it: an action id, or <c>panel:</c> and a panel id.</param>
/// <param name="IsDone">Whether the mission shows it done.</param>
public sealed record GuideStep(string Title, string Hint, string Show, Func<GuideFacts, bool> IsDone);

/// <summary>
/// The first mission made with the guide: a guard, his patrol, an objective, the mission ending when it is met, and a play.
/// Each step is judged from the mission itself, so doing it any way at all ticks it, and undoing it takes the tick away.
/// </summary>
public static class MissionGuide
{
    /// <summary>Gets the steps of a Project I.G.I. mission, in order.</summary>
    public static IReadOnlyList<GuideStep> Igi1 { get; } =
    [
        new(
            "Add a guard",
            "Look where he should stand and use Tools > Add Guard Here.",
            "ai.addguard",
            static f => f.Document.EnumerateTasks().Any(IsSoldier)),
        new(
            "Give him a patrol",
            "With the guard selected, Tools > Patrol Tool: click the ground for each waypoint, or Patrol Round Selection.",
            "tool.patrol",
            static f => f.Document.EnumerateTasks().Where(IsSoldier).Any(static s => s.Children.Any(static c => c.TypeName == PatrolRoute.PathType && PatrolRoute.Read(c).Waypoints.Count > 0))),
        new(
            "Give the player an objective",
            "In Rules, add a rule whose action completes an objective when something happens (a guard is dead, a terminal hacked).",
            "panel:rules",
            static f => f.Document.EnumerateTasks().Any(static t => t.TypeName == "DefineComputerObjective" && t.Parameters.Any(IsSetObjective))),
        new(
            "End the mission when it is done",
            "In Rules, add a rule that completes the mission when the objective is complete.",
            "panel:rules",
            static f => f.Document.EnumerateTasks().Any(static t => t.TypeName == "LevelFlow" && Holds(t.FindParameter("Complete")))),
        new(
            "Play it",
            "Play (F5) runs the mission from where it starts; Esc comes back to the editor.",
            "play.start",
            static f => f.HasPlayed),
    ];

    /// <summary>How many of the steps the mission shows done.</summary>
    /// <param name="steps">The steps.</param>
    /// <param name="facts">The mission and what else is known.</param>
    /// <returns>The count.</returns>
    public static int Done(IReadOnlyList<GuideStep> steps, GuideFacts facts)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Count(s => s.IsDone(facts));
    }

    private static bool IsSoldier(MissionTask task) => task.TypeName.StartsWith("HumanSoldier", StringComparison.Ordinal);

    // An objective's complete expression that says something.
    private static bool IsSetObjective(MissionParameter parameter) =>
        parameter.Name.StartsWith("Objective ", StringComparison.Ordinal)
        && parameter.Name.EndsWith(" Complete Expression", StringComparison.Ordinal)
        && Holds(parameter);

    // A condition with something in it other than nothing or a constant false.
    private static bool Holds(MissionParameter? parameter)
    {
        if (parameter?.Arguments is not [QscStringLiteral { Value: { } text }])
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double constant) || constant != 0;
    }
}
