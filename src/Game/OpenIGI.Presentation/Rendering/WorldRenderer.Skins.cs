using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A subset drawn whole, its coincident skins culled where the rest of it is drawn two-sided. See <see cref="CoincidentSkins"/>.</summary>
public sealed partial class WorldRenderer
{
    // A subset's draw: under no culling, its skins -- the last of its indices -- culled after the rest, so a sheet's two faces never
    // fight over one pixel; under culling already, the whole of it at once.
    private void DrawSubset(MeshHandle mesh, in WorldSubsetDraw subset, in Matrix4f modelView, BlendMode blend, CullMode applied)
    {
        int skins = applied == CullMode.None ? Math.Clamp(subset.SkinIndexCount, 0, subset.IndexCount) : 0;
        int open = subset.IndexCount - skins;
        if (open > 0)
        {
            _device.DrawMesh(mesh, subset.IndexOffset, open, modelView, subset.Diffuse, subset.Lightmap, blend);
        }

        if (skins > 0)
        {
            _device.SetCullMode(CullMode.CounterClockwise);
            _device.DrawMesh(mesh, subset.IndexOffset + open, skins, modelView, subset.Diffuse, subset.Lightmap, blend);
            _device.SetCullMode(applied);
        }
    }
}
