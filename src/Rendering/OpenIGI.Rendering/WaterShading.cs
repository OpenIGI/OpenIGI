using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// What a sheet of water is lit by: the constants I.G.I. 2's water vertex shader is handed
/// (<c>WaterRender_Draw</c>), all of them in the sheet's own axes, which are the world's.
/// </summary>
/// <param name="Eye">Where the eye is, in the sheet's space and in the mesh's units.</param>
/// <param name="SunDirection">Towards the level's sun (<c>c8</c>), unit length; zero where the level has none.</param>
/// <param name="SunColour">The sun's front colour (<c>c9</c>).</param>
/// <param name="Ambient">The objects' ambient light (<c>c10</c>, <c>Mesh3D_atAmbientColor[1]</c>).</param>
/// <param name="Diffuse">The water's own colour (<c>c20</c>, <c>Diffuse color</c>).</param>
/// <param name="Specular">The colour of the sun's glint on it (<c>c21</c>, <c>Specular color</c>).</param>
/// <param name="MinimumFacing">The smallest cosine the reflection is built with (<c>c14.z</c>, <c>Max reflection angle</c>).</param>
/// <param name="IsAboveWater">
/// Whether the eye is above the surface (<c>WaterRender_isAboveWater</c>): it culls the other facing, and below
/// the surface the normals are turned over.
/// </param>
public readonly record struct WaterShading(
    Vector3f Eye,
    Vector3f SunDirection,
    Vector3f SunColour,
    Vector3f Ambient,
    Vector3f Diffuse,
    Vector3f Specular,
    float MinimumFacing,
    bool IsAboveWater);
