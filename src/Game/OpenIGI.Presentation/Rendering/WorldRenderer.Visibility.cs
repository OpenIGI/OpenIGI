using OpenIGI.Core.Graphics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <content>The title's view culling: what it leaves out of the frame, and the window it sees the rest through.</content>
public sealed partial class WorldRenderer
{
    /// <inheritdoc/>
    /// <remarks>
    /// Asked as the frame is prepared, so its answer holds for every pass drawn from it -- the cascades, the lamps' maps, the
    /// geometry, the glass, the rotors and the traced world alike. That is the point: what the title leaves out of the picture
    /// must not go on casting shadows into it.
    /// </remarks>
    public IPlacementVisibility? Visibility { get => _frameState.Visibility; set => _frameState.Visibility = value; }

    /// <summary>Narrows the device to the window the title's culling sees a placement through, in a pass drawn from the eye.</summary>
    /// <param name="entry">The placement's frame entry.</param>
    /// <param name="fromEye">Whether the pass is drawn from the frame's eye.</param>
    /// <param name="applied">The clip the device holds, updated when this changes it.</param>
    /// <returns><see langword="false"/> when the window covers nothing and the placement draws nothing.</returns>
    /// <remarks>
    /// Project I.G.I. clips what lies beyond the building the camera stands in to the outside's window; a light's pass is never
    /// narrowed, because a light has no window onto anything.
    /// </remarks>
    private bool ApplyClip(in WorldFrameEntry entry, bool fromEye, ref ViewClip? applied)
    {
        ViewClip? clip = fromEye ? entry.Clip : null;
        if (clip is { IsEmpty: true })
        {
            return false;
        }

        if (clip != applied)
        {
            _device.SetDrawClip(clip);
            applied = clip;
        }

        return true;
    }
}
