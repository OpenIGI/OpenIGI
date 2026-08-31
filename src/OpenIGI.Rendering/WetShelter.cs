using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>A box the rain does not get into: everything under it stays dry.</summary>
/// <remarks>
/// <para>
/// The level's building shells, which the weather already uses to decide whether a falling drop is
/// under a roof. Standing water needs the same answer for a different reason and in a different place:
/// a drop is culled on the CPU where the shell list lives, and a puddle is decided per pixel in the
/// lighting shader, which knows nothing about the level. So the boxes are handed to the device with
/// the rest of the wetness and tested there.
/// </para>
/// <para>
/// <b>Camera-relative metres, and that is not a detail.</b> The levels sit tens of kilometres from the
/// world origin, and the shader has the surface position only as a camera-relative vector — recovering
/// an absolute one to compare against an absolute box would throw away the precision that vector exists
/// to keep. The rebase is done on the game's side in double precision, exactly as the lamp positions are.
/// </para>
/// </remarks>
/// <param name="Minimum">The low corner, camera-relative, in metres.</param>
/// <param name="Maximum">The high corner, camera-relative, in metres.</param>
public readonly record struct WetShelter(Vector3f Minimum, Vector3f Maximum)
{
    /// <summary>The most that may be submitted in one frame.</summary>
    /// <remarks>
    /// A budget rather than a limit on the level: the nearest few are submitted and the rest are not,
    /// because a shell the player cannot see cannot be sheltering ground the player can see. Sixteen
    /// covers a courtyard surrounded by buildings with room to spare, and it is sixteen branches in a
    /// per-pixel loop, which is what the number is really governed by.
    /// </remarks>
    public const int Capacity = 16;
}
