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
    public ResolvedWorldModel? Model;
    public WorldPlacement? Displayed;
    public Vector3d Position;
    public Matrix3f Orientation;
    public RotorDrawState Rotor;
}
