using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// Places the free camera so a mission opens looking at the level rather than at the inside of whichever
/// object happens to sit at the origin.
/// </summary>
public static class FreeCameraFactory
{
    /// <summary>Builds a free camera backed off the centre of what the scene placed, looking slightly down.</summary>
    /// <param name="scene">The built scene whose placements decide the centre.</param>
    /// <param name="yaw">The yaw the camera opens at.</param>
    /// <param name="pitch">The pitch the camera opens at.</param>
    /// <param name="height">An extra height added above the ground.</param>
    /// <returns>A free camera positioned over the level.</returns>
    public static FreeCamera Create(WorldScene scene, float yaw, float pitch, float height)
    {
        if (scene.Placements.Count == 0)
        {
            return new FreeCamera(default, yaw, pitch);
        }

        // Medians, not a bounding box or a mean: a level is a dense cluster with a few far-flung
        // outliers, so a box centre lands in the empty part and a mean gets dragged towards whatever is
        // furthest away. The median height is the ground, because most objects sit on it.
        double[] xs = new double[scene.Placements.Count];
        double[] ys = new double[scene.Placements.Count];

        for (int i = 0; i < scene.Placements.Count; i++)
        {
            xs[i] = scene.Placements[i].Position.X;
            ys[i] = scene.Placements[i].Position.Y;
        }

        Array.Sort(xs);
        Array.Sort(ys);

        double centreX = xs[xs.Length / 2];
        double centreY = ys[ys.Length / 2];
        double ground = scene.GroundHeight;

        // Standing back a hundred metres, twenty-five up, looking north and slightly down.
        return new FreeCamera(
            new Vector3d(centreX, centreY - 100.0, ground + 25.0 + height),
            yaw,
            pitch);
    }
}
