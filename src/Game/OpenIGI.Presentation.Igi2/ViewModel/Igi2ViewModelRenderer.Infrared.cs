using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>The bodies as the thermal goggles see them (<c>Direct3DRender_DrawBoneMesh</c> under <c>isDrawInfra</c>).</content>
public sealed partial class Igi2ViewModelRenderer
{
    /// <summary>The heat ramp the infrared view colours a body by, <c>tInfraHumanTexture</c>.</summary>
    public const string InfraredRamp = "LOCAL:common/textures/infra_scale2.tex";

    // How far nearer the eye the see-through draw is, at most, and how near it ever comes, in metres: 40960 and 819.2 world
    // units in the original.
    private const float SeenThroughPullMetres = 10f;
    private const float SeenThroughNearestMetres = 0.2f;

    // How far nearer the eye the body's own draw is, in metres, so the depth its world draw left passes it, not z-fights it.
    private const float BodyPullMetres = 0.02f;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Every boned body the world draw drew, drawn twice in the ramp's colours, unlit and unfogged: where it stands, over its
    /// lit self; then nearer the eye by up to ten metres, but never nearer than a fifth of one, its size shrunk with its
    /// distance so the picture of it is the same, and blended at half coverage -- which is what shows a soldier through the
    /// wall it stands behind. The ramp is looked up by how squarely each part faces the eye.
    /// </para>
    /// <para>
    /// The original draws its translucent face groups not at all while infrared; a body here is drawn whole.
    /// </para>
    /// </remarks>
    public void DrawWorldInfrared(IReadOnlyList<TitleWorldModel> models, in Camera3D camera, in Matrix4f projection, float blend = 1f)
    {
        ArgumentNullException.ThrowIfNull(models);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_textures is not { } textures || _models is null || models.Count == 0)
        {
            return;
        }

        TextureHandle ramp = textures.Resolve(InfraredRamp);
        if (!ramp.IsValid)
        {
            return;
        }

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

            ReadOnlySpan<MeshDrawRange> ranges = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_outlineRanges);
            float distance = MathF.Sqrt((modelView.M14 * modelView.M14) + (modelView.M24 * modelView.M24) + (modelView.M34 * modelView.M34));
            _device.DrawInfrared(part.Mesh, ranges, TowardEye(modelView, distance, BodyPullMetres), ramp, InfraredDraw.Body);
            _device.DrawInfrared(part.Mesh, ranges, TowardEye(modelView, distance, SeenThroughPull(distance)), ramp, InfraredDraw.SeenThrough);
        }

        _device.ClearPoseInterpolation();
    }

    // How far the see-through draw comes nearer a body this far off: ten metres, short of the nearest it may come.
    private static float SeenThroughPull(float distance) =>
        distance < SeenThroughNearestMetres ? 0f : MathF.Min(SeenThroughPullMetres, distance - SeenThroughNearestMetres);

    // A transform into view space brought nearer the eye along the line to it, everything it carries shrunk alike, so it covers
    // the same picture; the original scales its whole world matrix by the ratio of the distances.
    private static Matrix4f TowardEye(in Matrix4f modelView, float distance, float pull)
    {
        if (distance <= pull || pull <= 0f)
        {
            return modelView;
        }

        float k = (distance - pull) / distance;
        return new Matrix4f(
            modelView.M11 * k, modelView.M12 * k, modelView.M13 * k, modelView.M14 * k,
            modelView.M21 * k, modelView.M22 * k, modelView.M23 * k, modelView.M24 * k,
            modelView.M31 * k, modelView.M32 * k, modelView.M33 * k, modelView.M34 * k,
            modelView.M41, modelView.M42, modelView.M43, modelView.M44);
    }
}
