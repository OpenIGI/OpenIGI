using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The player's live body, as any game's AI perceives the player's task.</summary>
/// <param name="mission">The mission whose player it is.</param>
/// <remarks>
/// Its eyes are its detection mark, where Project I.G.I.'s own detection looks for it: its origin above its feet and
/// <c>sub_465820</c>'s raise, by whether it crouches. It does not lie flat, so it keeps low only in a settled crouch.
/// </remarks>
public sealed class Igi1PlayerBody(MissionWorld mission) : IHumanBodyState
{
    /// <inheritdoc/>
    public Vector3d Feet => mission.Player?.Body.Position ?? default;

    /// <inheritdoc/>
    public Vector3d Eyes
    {
        get
        {
            Vector3d feet = Feet;
            bool crouched = mission.Player?.Body.IsCrouched == true;
            return new Vector3d(feet.X, feet.Y, feet.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouched));
        }
    }

    /// <inheritdoc/>
    public bool IsKeepingLow => mission.Player?.Body is { IsCrouched: true } body && body.EyeHeight <= PlayerBody.CrouchingEyeHeight;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human+3318</c> is set and cleared as the task is put in the world and taken out of it (<c>0x460950</c>,
    /// <c>0x4609A0</c>, and the player's own <c>0x410900</c>), as its conditional containers start and stop: a player whose
    /// container holds it out of the world, as an intro's does, is nobody's target.
    /// </remarks>
    public bool IsActive => mission.Player is not null && ConditionalContainerGate.IsTaskActive(mission.PlayerTask);
}

/// <summary>A soldier's live body, as any game's AI perceives the soldier's task.</summary>
/// <param name="view">The soldier as it stands.</param>
/// <param name="crouching">Whether its brain has it crouching now.</param>
/// <remarks>Its eyes are its detection mark, as for the player; it takes part while every container above its task is open.</remarks>
public sealed class Igi1SoldierBody(SoldierView view, Func<bool> crouching) : IHumanBodyState
{
    /// <inheritdoc/>
    public Vector3d Feet => view.Position;

    /// <inheritdoc/>
    public Vector3d Eyes
    {
        get
        {
            Vector3d feet = view.Position;
            return new Vector3d(feet.X, feet.Y, feet.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching()));
        }
    }

    /// <inheritdoc/>
    public bool IsKeepingLow => crouching();

    /// <inheritdoc/>
    public bool IsActive => ConditionalContainerGate.IsTaskActive(view.Soldier);
}
