using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A grid laid over part of a level's ground so that part can be shaped, and the task that saves it with the level: its
/// type, where it stands, the parameters the type declares and the values the grid needs.
/// </summary>
/// <param name="Id">The grid's id, which the task names.</param>
/// <param name="TaskType">The task's type.</param>
/// <param name="PositionMetres">Where the task stands, in world metres.</param>
/// <param name="Declaration">The type's parameters in declaration order, each with its script type name.</param>
/// <param name="Values">The task's whole-number parameters, by name; the rest stay zero.</param>
/// <param name="Name">A name for the grid, for lists and messages.</param>
public sealed record TerrainArea(
    int Id,
    string TaskType,
    Vector3d PositionMetres,
    IReadOnlyList<KeyValuePair<string, string>> Declaration,
    IReadOnlyList<KeyValuePair<string, int>> Values,
    string Name);
