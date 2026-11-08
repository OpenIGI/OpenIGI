using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One placement's camera-independent state for the current frame.</summary>
internal struct WorldFrameEntry
{
    public bool Skip;
    public bool WasDestroyed;
    public int Variant;
    public bool HasRotor;
    public bool IsRotor;

    /// <summary>Whether the placement moves on its own, so its draws stamp their own motion.</summary>
    public bool Moving;

    /// <summary>
    /// Whether the title's view culling leaves the placement out of the eye's picture this frame. Only the eye's passes drop it: a
    /// room the portal walk did not reach still stands between the sun and the room the eye is in, so it keeps casting and tracing.
    /// </summary>
    public bool EyeCulled;

    /// <summary>The window the title's view culling sees the placement through this frame, or null for the whole view.</summary>
    public ViewClip? Clip;
    public ResolvedWorldModel? Model;
    public WorldPlacement? Displayed;

    // Whether nothing claims Displayed, as of PlacementClaims.Epoch ClaimEpoch, for Displayed as it was then (ClaimFor).
    public bool Unclaimed;
    public int ClaimEpoch;
    public WorldPlacement? ClaimFor;
    public Vector3d Position;
    public Matrix3f Orientation;
    public RotorDrawState Rotor;
}
