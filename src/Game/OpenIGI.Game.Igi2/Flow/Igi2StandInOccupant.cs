using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>Another game's human, as the level's areas count it: where it stands, its team, and whether it is alive.</summary>
/// <param name="human">The human.</param>
/// <param name="isAi">Whether it is a soldier rather than the player.</param>
/// <remarks>
/// Its root is taken midway between its feet and its eyes, about where a body's root bone stands
/// (<c>BoneDynCubeObj_GetRootPosition</c>), which the other game's body does not say.
/// </remarks>
internal sealed class Igi2StandInOccupant(IAiHuman human, bool isAi) : IIgi2AreaOccupant
{
    /// <inheritdoc/>
    public int Team => human.Team;

    /// <inheritdoc/>
    public bool IsDead => human.IsDead;

    /// <inheritdoc/>
    public bool IsActive => human is not IAiDetectable { IsGone: true };

    /// <inheritdoc/>
    public bool IsAi => isAi;

    /// <inheritdoc/>
    public Vector3d RootPosition
    {
        get
        {
            Vector3d feet = human.Position;
            return new Vector3d(feet.X, feet.Y, (feet.Z + human.OriginalViewPosition.Z) * 0.5);
        }
    }
}
