using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// One human the detection scan offers an AI this tick — what the human's descriptor (<c>0x45FE30</c>) tells
/// <c>0x489750</c> about itself.
/// </summary>
/// <param name="Id">
/// Who it is: <see cref="AiDetectionCandidate.PlayerId"/> for the player, a soldier's runtime id otherwise. Event
/// records name the humans they concern by it.
/// </param>
/// <param name="OtherSide">Whether it is on the other side from the AI being offered it.</param>
/// <param name="Alive">Whether it is alive.</param>
/// <param name="Mark">
/// The point the cones and the sight ray test — its position raised mid-body (<c>sub_465820</c>).
/// </param>
/// <param name="Feet">Its position — the feet, which combat aims at and events report.</param>
/// <param name="Crouching">Whether it is crouching, which halves the aim raise.</param>
/// <param name="Record">The shared target record every AI fighting it holds, or <see langword="null"/>.</param>
/// <param name="Origin">
/// Its origin, <c>Human+32</c>: the feet raised by the pose's root height. <see langword="null"/> takes the
/// standing or crouched origin above the feet.
/// </param>
/// <remarks>
/// The scan registers a candidate that is on the other side and alive into the AI's enemy list
/// (<c>+5648</c>), and one on its own side and dead into its list of fallen friends (<c>+5660</c>); anything
/// else is unregistered. Candidates are offered in the global human list's order, which is the order they
/// register in, and so the order their detection events queue in.
/// </remarks>
public readonly record struct AiDetectionCandidate(
    int Id,
    bool OtherSide,
    bool Alive,
    Vector3d Mark,
    Vector3d Feet,
    bool Crouching,
    AiCombatTarget? Record,
    Vector3d? Origin = null)
{
    /// <summary>
    /// Gets the point <c>0x451380</c> hands the graph's nearest-node search: the origin raised half a metre, a
    /// quarter when crouching.
    /// </summary>
    public Vector3d NodeQueryPoint
    {
        get
        {
            Vector3d origin = Origin ?? new Vector3d(
                Feet.X,
                Feet.Y,
                Feet.Z + (Crouching ? Igi1HumanAi.HumanOriginCrouchedInUnits : Igi1HumanAi.HumanOriginStandingInUnits));
            return new Vector3d(origin.X, origin.Y, origin.Z + (Crouching ? 1024.0 : 2048.0));
        }
    }

    /// <summary>The id the player goes by in candidates and event records.</summary>
    public const int PlayerId = -1;

    /// <summary>Gets whether this is the player.</summary>
    public bool IsPlayer => Id == PlayerId;
}
