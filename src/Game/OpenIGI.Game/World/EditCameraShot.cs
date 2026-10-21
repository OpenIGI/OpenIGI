using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One authored <c>EditCamera</c> knot of a cutscene, as the scene reads it at load.</summary>
internal sealed record EditCameraShot(
    Vector3d Position,
    float Alpha,
    float Beta,
    float Gamma,
    float FieldOfView,
    float Duration,
    int LinkTaskId,
    bool UpdateLink,
    int TargetTaskId,
    bool UpdateTarget,
    bool SmoothToNext,
    float TimeOfDay,
    string Filter,
    float Noise,
    Vector3f FilterColor,
    float Shake,
    Vector3d LinkOrigin,
    Matrix3f LinkOriginOrientation);
