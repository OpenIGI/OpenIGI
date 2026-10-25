using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>How a body the binoculars marked is highlighted, for whichever title draws it.</summary>
/// <remarks>
/// <b>Not retail</b>: retail marks a contact with a rectangle on the detector. Two looks, by <see cref="HudCVars.BinocularSilhouette"/>:
/// the silhouette highlight, a fill inside a soft outline laid over the finished picture by the marker's group, or a glow in
/// the marker's colour drawn into the scene. Both seen through walls.
/// </remarks>
public static class BinocularMarking
{
    /// <summary>The glow a marker takes when the highlight is not drawn in the silhouette style.</summary>
    /// <param name="marker">The detector's class of contact.</param>
    /// <returns>Red for a hostile, green for anyone else, blue for one out of the fight.</returns>
    public static Bgra32 Glow(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgba(96, 220, 120, 200),
        BinocularMarkerColor.Disabled => Bgra32.FromRgba(96, 150, 255, 200),
        _ => Bgra32.FromRgba(255, 72, 72, 210),
    };

    /// <summary>The silhouette group a marker is highlighted as.</summary>
    /// <param name="marker">The detector's class of contact.</param>
    /// <returns>Its group.</returns>
    public static SilhouetteGroup Group(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => SilhouetteGroup.Team,
        BinocularMarkerColor.Disabled => SilhouetteGroup.Disabled,
        _ => SilhouetteGroup.Hostile,
    };

    /// <summary>Highlights one marked body in the look the settings ask for.</summary>
    /// <param name="device">The device.</param>
    /// <param name="mesh">The body's mesh.</param>
    /// <param name="ranges">The index ranges that make up the body.</param>
    /// <param name="modelView">The transform it was drawn with; its pose is the one currently set on the device.</param>
    /// <param name="marker">The detector's class of contact.</param>
    /// <param name="radiusPixels">The glow's screen-space radius, where it is a glow.</param>
    public static void Draw(
        IMeshDrawDevice device,
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        BinocularMarkerColor marker,
        float radiusPixels)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (HudCVars.BinocularSilhouette.Value)
        {
            device.DrawSilhouette(mesh, ranges, modelView, Group(marker));
        }
        else
        {
            device.DrawOutline(mesh, ranges, modelView, Glow(marker), radiusPixels, throughWalls: true);
        }
    }
}
