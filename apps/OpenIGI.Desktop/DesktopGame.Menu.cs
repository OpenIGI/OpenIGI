using OpenIGI.Core.Numerics;

namespace OpenIGI.Desktop;

internal sealed partial class DesktopGame
{
    // Rebuilds the orbit around the currently-loaded showcase scene. The scene's placements decide the
    // centre (medians, so a few far outliers do not drag it off the level), and the mission's own entry
    // in menu-cameras.json decides the orbit.
    private void ApplyShowcaseCamera(int missionId)
    {
        Vector3d centre = default;
        if (_scene is { Placements.Count: > 0 } showcaseScene)
        {
            double[] centreXs = new double[showcaseScene.Placements.Count];
            double[] centreYs = new double[showcaseScene.Placements.Count];
            for (int i = 0; i < showcaseScene.Placements.Count; i++)
            {
                centreXs[i] = showcaseScene.Placements[i].Position.X;
                centreYs[i] = showcaseScene.Placements[i].Position.Y;
            }

            Array.Sort(centreXs);
            Array.Sort(centreYs);
            centre = new Vector3d(centreXs[centreXs.Length / 2], centreYs[centreYs.Length / 2], showcaseScene.GroundHeight + 15.0);
        }

        _showcaseCamera = new MenuShowcaseCamera(centre, _menuCameras.For(missionId));
    }
}
