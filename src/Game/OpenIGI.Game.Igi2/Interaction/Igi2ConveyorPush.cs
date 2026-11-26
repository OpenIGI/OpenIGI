using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// The level's conveyor belts as they push another game's human standing on them (<see cref="ISurfacePush"/>): by the rule they
/// push the level's own (<see cref="Igi2ConveyorBeltTask.Push"/>), the belt's own model alone and its belt material alone.
/// </summary>
/// <param name="belts">The level's belts, by level id.</param>
/// <param name="materials">The session's materials, which name a face's material row; <see langword="null"/> for none.</param>
public sealed class Igi2ConveyorPush(IReadOnlyDictionary<int, Igi2ConveyorBeltTask> belts, Func<Igi2GameMaterials?> materials)
    : ISurfacePush
{
    private readonly IReadOnlyDictionary<int, Igi2ConveyorBeltTask> _belts = belts ?? throw new ArgumentNullException(nameof(belts));
    private readonly Func<Igi2GameMaterials?> _materials = materials ?? throw new ArgumentNullException(nameof(materials));

    /// <inheritdoc/>
    /// <remarks>
    /// The placement is the belt's by its level id and its placing type; as <c>Human_Move</c> asks only the belt's own solid (depth
    /// nought), the models its attach points hang on it -- its rollers -- push nothing.
    /// </remarks>
    public Vector3f? PushOn(WorldPlacement placement, int material)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.IsAttached
            || placement.TaskName != Igi2ConveyorBeltTask.TypeName
            || !_belts.TryGetValue(placement.LevelId, out Igi2ConveyorBeltTask? belt)
            || !string.Equals(placement.Instance.Name, belt.Model, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Vector3f push = belt.Push(_materials()?[material]?.Name);
        return push == default ? null : push;
    }
}
