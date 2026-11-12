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
    ICutsceneScene Scene,
    int ShotIndex)
{
    /// <summary>Gets whether an authored binocular holder owns the retail optics overlay.</summary>
    public bool BinocularOverlayVisible { get; init; }
}

/// <summary>The scene a cut-scene frame is a shot of, as whatever draws the frame reads it.</summary>
public interface ICutsceneScene
{
    /// <summary>Gets the scene's task id.</summary>
    int Id { get; }

    /// <summary>Gets how many ticks the scene has played.</summary>
    int Ticks { get; }
}
