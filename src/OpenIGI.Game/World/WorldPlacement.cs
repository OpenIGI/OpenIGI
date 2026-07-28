using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>One object placed in the world: where it is, and which pack instance draws it.</summary>
/// <remarks>
/// Geometry and placement come from different files. A pack's <c>INST</c> record binds a model to its
/// textures under a name like <c>435_01_1</c>; <c>objects.qvm</c> places that name somewhere with a task
/// carrying <c>Position</c>, <c>Orientation</c> and <c>Model</c>. This is the join.
/// </remarks>
/// <param name="Position">The world position, in metres.</param>
/// <param name="Orientation">The world orientation.</param>
/// <param name="Instance">The pack instance, which names the model and its per-slot textures.</param>
/// <param name="Pack">The pack the instance belongs to.</param>
/// <param name="Model">The model's geometry.</param>
/// <param name="TaskName">The placing task's name, for diagnostics.</param>
/// <param name="LightmapSet">
/// The baked-lightmap set this object belongs to — a <c>LightmapInfo</c> task's <c>Filename</c> — or an
/// empty string when nothing bakes it.
/// </param>
/// <param name="LightmapIndex">
/// The entry within <paramref name="LightmapSet"/> holding this object's bake, or <c>-1</c> when it has
/// none. Entry <c>LightmapIndex + n</c> belongs to detail level <c>n</c>; only level 0 is drawn.
/// </param>
public sealed record WorldPlacement(
    Vector3d Position,
    Matrix3f Orientation,
    MtpInstance Instance,
    LoadedPack Pack,
    MefModel Model,
    string TaskName,
    string LightmapSet = "",
    int LightmapIndex = -1)
{
    /// <summary>Gets a value indicating whether a baked lightmap covers this object.</summary>
    public bool HasLightmap => LightmapIndex >= 0 && LightmapSet.Length > 0;

    /// <summary>Gets the archive entry name holding this object's bake.</summary>
    /// <returns>The entry name, or an empty string when <see cref="HasLightmap"/> is false.</returns>
    public string LightmapEntryName() =>
        HasLightmap ? $"{LightmapSet}_{LightmapIndex:D5}{ObjectLightmap.Extension}" : string.Empty;

    /// <inheritdoc/>
    public override string ToString() => $"{Instance.Name} at {Position}";
}
