using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// What one soldier looked like at the moment the developer inspector asked.
/// </summary>
/// <remarks>
/// A copy rather than a reference. The inspector draws on the far side of an interface that must not
/// see gameplay types, and a panel holding a live soldier across a mission change would be holding one
/// the loader had already thrown away.
/// </remarks>
/// <param name="Name">The level's own name for the soldier.</param>
/// <param name="Position">Where it stands, in metres — the unit every panel and command works in.</param>
/// <param name="IsDead">Whether it has been killed.</param>
/// <param name="Animation">The animation clip it is playing.</param>
/// <param name="HealthFraction">How much health remains, 0 to 1, or -1 when it has no health model.</param>
/// <param name="IsCutsceneActor">Whether it was staged for a cutscene rather than spawned for play.</param>
public readonly record struct SoldierSnapshot(
    string Name,
    Vector3d Position,
    bool IsDead,
    int Animation,
    float HealthFraction,
    bool IsCutsceneActor);
