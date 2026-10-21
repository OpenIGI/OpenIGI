using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Ai;

/// <summary>
/// One AI event as the engine sends it to the level's humans: the <c>HumanAISendEvent</c> task event's context
/// (Project I.G.I.'s 28-byte record, I.G.I. 2's <c>HumanAIEventContext_t</c>).
/// </summary>
/// <param name="Type">
/// The event, as <see cref="AiEvent"/> numbers it (Project I.G.I.'s <c>AIEVENT_*</c>), or -1 for an event only its sending game
/// reads; a game that numbers its events otherwise translates.
/// </param>
/// <param name="Team">The team it concerns, or -1 for every team.</param>
/// <param name="Flags">
/// The admission tests a brain applies to it, the engine's bits both games test alike: in view, in range, of a human, of a
/// living or a dead one, of the receiver's side or the other.
/// </param>
/// <param name="Radius">How far it reaches, in world units.</param>
/// <param name="Position">Where it happens, or <see langword="null"/> when nowhere.</param>
/// <param name="Source">The task that caused it, or <see langword="null"/>.</param>
/// <param name="Subject">The task it is about, or <see langword="null"/>.</param>
/// <param name="Context">The sending game's own context, whole: a brain of the same game reads it, another game's the fields.</param>
public readonly record struct HumanAiEvent(
    int Type,
    int Team,
    int Flags,
    float Radius,
    Vector3d? Position,
    GameTask? Source,
    GameTask? Subject,
    object? Context);

/// <summary>A human that takes the engine's AI events and hands them to its brain: a type with a <c>HumanAISendEvent</c> handler.</summary>
public interface IHumanAiEventTarget
{
    /// <summary>An AI event reaches the human.</summary>
    /// <param name="aiEvent">The event.</param>
    void SendEvent(in HumanAiEvent aiEvent);
}

/// <summary>The engine's broadcast of an AI event to every human: Project I.G.I. <c>0x450CE0</c>, I.G.I. 2 <c>HumanAI_SendEvent</c>.</summary>
public static class HumanAiEvents
{
    /// <summary>Sends an event to every human on the list, in its order.</summary>
    /// <param name="humans">The level's humans, in the order they joined the list (their creation order).</param>
    /// <param name="aiEvent">The event.</param>
    /// <remarks>
    /// Both games walk their human list (<c>0x460C60</c>, <c>Human_ptQTaskList</c>) sending each the task event; a human
    /// that is both the event's source and its subject does not take it (<c>Human</c>'s handler, <c>0x45FDA0</c>), and each
    /// brain then applies the event's own admission tests.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="humans"/> is <see langword="null"/>.</exception>
    public static void Send(IEnumerable<IHumanAiEventTarget> humans, in HumanAiEvent aiEvent)
    {
        ArgumentNullException.ThrowIfNull(humans);
        foreach (IHumanAiEventTarget human in humans)
        {
            if (ReferenceEquals(human, aiEvent.Source) && ReferenceEquals(human, aiEvent.Subject))
            {
                continue;
            }

            human.SendEvent(aiEvent);
        }
    }
}
