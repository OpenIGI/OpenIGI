using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The level's bird flocks, drawn as their <c>PolyListDynCubeObj</c> draws them.</content>
public sealed partial class Igi2TitlePlayer
{
    private readonly List<TitleWorldPolygons> _flockPolygons = [];

    /// <summary>Gets or sets the level's bird flocks.</summary>
    public IReadOnlyList<Igi2BirdFlockTask> Flocks { get; set; } = [];

    /// <inheritdoc/>
    /// <remarks>
    /// <c>BirdFlock_EnterHandler</c>, each frame for each flock in the world: its depth before the eye (<c>TransContext_ProjectWorldPos2</c>)
    /// says whether it is seen, and so flies; a seen flock's birds are its wings' triangles about its position. One a container holds
    /// out of the world is off the redraw list (<c>BirdFlock_RemoveHandler</c>).
    /// </remarks>
    public IReadOnlyList<TitleWorldPolygons> WorldPolygons
    {
        get
        {
            _flockPolygons.Clear();
            if (Flocks.Count == 0)
            {
                return _flockPolygons;
            }

            Vector3d eye = EyePosition;
            Matrix3f view = ViewOrientation;
            foreach (Igi2BirdFlockTask flock in Flocks)
            {
                if (!OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(flock))
                {
                    continue;
                }

                Vector3d at = flock.Position;
                double dx = at.X - eye.X, dy = at.Y - eye.Y, dz = at.Z - eye.Z;
                flock.Project((float)((view.M31 * dx) + (view.M32 * dy) + (view.M33 * dz)));
                if (flock.IsSeen && flock.Birds.Count > 0)
                {
                    _flockPolygons.Add(new TitleWorldPolygons(at, flock.Wings()));
                }
            }

            return _flockPolygons;
        }
    }
}
