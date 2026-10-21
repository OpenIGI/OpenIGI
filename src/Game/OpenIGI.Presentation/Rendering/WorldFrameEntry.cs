using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;
using System.Runtime.InteropServices;

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
