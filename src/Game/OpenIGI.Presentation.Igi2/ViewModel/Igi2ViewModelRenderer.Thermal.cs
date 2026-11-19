using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>
/// The bodies as the remastered thermal sensor sees them, and the walk over the drawn bodies it shares with the original's
/// infrared view.
/// </content>
public sealed partial class Igi2ViewModelRenderer
{
    // How fast the title's world ticks, which a body's cooling is timed by.
    private const float TicksPerSecond = 30f;

    private readonly BodyCooling _cooling = new();

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Not retail.</b> Every boned body the world draw drew, handed to the sensor in the pose it was drawn in: hot while alive,
    /// cooling once dead (<see cref="BodyCooling"/>). The sensor decides what of each shows through what stands before it.
    /// </remarks>
    public void DrawWorldThermal(IReadOnlyList<TitleWorldModel> models, in Camera3D camera, in Matrix4f projection, float blend = 1f)
    {
        ArgumentNullException.ThrowIfNull(models);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_models is null || models.Count == 0)
        {
            return;
        }

        foreach ((Part part, Matrix4f modelView, TitleWorldModel wanted) in PosedBodies(models, camera, projection, blend))
        {
            float heat = wanted.Instance is Igi2Human human
                ? _cooling.HeatOf(human, human.IsDead, wanted.Tick / TicksPerSecond)
                : BodyCooling.LivingHeat;
            _device.DrawThermalBody(part.Mesh, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_outlineRanges), modelView, heat);
        }

        _device.ClearPoseInterpolation();
    }

    /// <summary>
    /// Every boned body the world draw drew this tick, at the level it was drawn at, posed as it was drawn -- its ranges in the
    /// outline's list and the device's pose blend set -- with its transform into view space.
    /// </summary>
    /// <remarks>The caller draws each as it comes, before the next is posed, and clears the pose blend after the last.</remarks>
    private IEnumerable<(Part Part, Matrix4f ModelView, TitleWorldModel Wanted)> PosedBodies(
        IReadOnlyList<TitleWorldModel> models,
        Camera3D camera,
        Matrix4f projection,
        float blend)
    {
        float between = Math.Clamp(blend, 0f, 1f);

        // The levels the world draw drew each body at, from the same eye.
        foreach (TitleWorldModel wanted in LevelsOf(models, camera, projection))
        {
            if (wanted.Pose is null || DrawnBody(wanted) is not { } part || !PoseDrawnBody(part, wanted, between))
            {
                continue;
            }

            (Vector3d at, Matrix3f orientation, _) = Placed(wanted, between);
            Matrix4f modelView = camera.CreateModelView(WorldScene.ToMetres(at), orientation);
            if (wanted.Scale != 1f)
            {
                modelView = modelView.WithAxesScaled(wanted.Scale);
            }

            yield return (part, modelView, wanted);
        }
    }
}
