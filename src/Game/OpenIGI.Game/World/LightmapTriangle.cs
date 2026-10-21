using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// One lightmapped triangle a gather ray can hit, carrying what a bounce needs: its world corners, its
/// albedo, and which target and lightmap UVs to read the light already on it.
/// </summary>
/// <param name="A">First corner, world metres.</param>
/// <param name="B">Second corner.</param>
/// <param name="C">Third corner.</param>
/// <param name="Target">Index into the targets list of the page this triangle draws into.</param>
/// <param name="U0">First corner's lightmap U.</param>
/// <param name="V0">First corner's lightmap V.</param>
/// <param name="U1">Second corner's lightmap U.</param>
/// <param name="V1">Second corner's lightmap V.</param>
/// <param name="U2">Third corner's lightmap U.</param>
/// <param name="V2">Third corner's lightmap V.</param>
/// <param name="Albedo">The surface's diffuse reflectance, per channel.</param>
public readonly record struct LightmapTriangle(
    Vector3d A, Vector3d B, Vector3d C, int Target,
    float U0, float V0, float U1, float V1, float U2, float V2, Vector3f Albedo);
