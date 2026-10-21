using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The camera and screen treatment produced by the active authored cutscene shot.</summary>
public sealed record CutsceneFrame(
    Camera3D Camera,
    float FieldOfView,
    float ViewportHeightFactor,
    string Filter,
    float FilterNoise,
    Vector3f FilterColor,
    float TimeOfDay,
    CutSceneTask Scene,
    int ShotIndex)
{
    /// <summary>Gets whether an authored binocular holder owns the retail optics overlay.</summary>
    public bool BinocularOverlayVisible { get; init; }
}
