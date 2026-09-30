using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Pipeline.Targets;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The motion of what moves under its own power: soldiers, doors, the held weapon.</summary>
/// <remarks>
/// <para>
/// The velocity the temporal passes read is derived from depth and the two cameras, which describes only
/// what the camera did. Every draw of something that moves on its own is noted as it is drawn, paired
/// with the same draw's transform from the frame before, and stamped over that field once the frame's
/// depth is final. See <see cref="ObjectVelocityPass"/>.
/// </para>
/// <para>
/// A draw is paired with last frame's by its mesh and which occurrence of that mesh it is within the
/// frame, so a mesh drawn several times pairs each draw with its own transform -- as long as the draw
/// order is stable, which a renderer iterating its own lists gives for free.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    // A draw whose view-space position jumped further than this since the draw it was paired with is a
    // mispairing, not motion: nothing this pass serves moves two metres in a frame. Squared, in metres.
    private const float MaxPairedJumpSquared = 4f;

    private readonly List<ObjectVelocityDraw> _objectVelocityDraws = [];
    private readonly Dictionary<long, ObjectMotion> _objectMotionPrevious = [];
    private readonly Dictionary<long, ObjectMotion> _objectMotionCurrent = [];
    private readonly Dictionary<uint, int> _objectOccurrence = [];
    private ObjectVelocityPass? _objectVelocity;
    private Matrix4f _temporalProjection = Matrix4f.Identity;
    private float _previousPoseBlend;
    private bool _objectMotion;

    /// <inheritdoc/>
    public void SetObjectMotion(bool moving) => _objectMotion = moving;

    /// <inheritdoc/>
    /// <remarks>
    /// Every mesh pipeline takes the mesh's previous-pose stream as a second vertex binding, so the blend
    /// is one number per draw and the vertex stage slides each vertex along its own delta: the frame draws
    /// a soldier between the two poses either side of it rather than snapping onto the last one. The
    /// previous blend is the one that drew last frame's position, which the velocity stamp needs to
    /// measure the pose's own motion.
    /// </remarks>
    public void SetPoseInterpolation(float currentBlend, float previousBlend)
    {
        _poseBlend = Math.Clamp(currentBlend, 0f, 1f);
        _previousPoseBlend = Math.Clamp(previousBlend, 0f, 1f);
    }

    /// <inheritdoc/>
    public void ClearPoseInterpolation()
    {
        _poseBlend = 0f;
        _previousPoseBlend = 0f;
    }

    /// <summary>Notes a scene draw for the velocity stamp, when it is one that moves on its own.</summary>
    /// <param name="mesh">The mesh's handle, which keys the pairing.</param>
    /// <param name="resource">The mesh.</param>
    /// <param name="indexOffset">The first index drawn.</param>
    /// <param name="indexCount">How many indices were drawn.</param>
    /// <param name="modelView">The draw's transform into view space.</param>
    /// <param name="blend">How it was blended.</param>
    /// <remarks>
    /// <para>
    /// An animated mesh in the G-buffer, and the first-person rig's opaque forward draws. The rig for a
    /// different reason: its parts are immutable meshes whose per-frame transforms carry the recoil, and
    /// the foreground layer deliberately gives them no camera motion, so without a stamp the resolve lays
    /// the old weapon pose over the new one. The additive muzzle flame neither owns the stored depth nor
    /// wants history.
    /// </para>
    /// <para>
    /// Only a draw that WRITES depth may stamp, because the stamp is tested against the stored depth and
    /// a mesh that never wrote it cannot own it. The sky's cloud layers are animated meshes drawn with
    /// depth off; stamped, their far-plane depth plus the stamp's bias would beat every distant surface
    /// and write the clouds' drift over the true motion of everything beyond some twenty metres.
    /// </para>
    /// </remarks>
    private void NoteObjectMotion(
        MeshHandle mesh,
        RhiMesh resource,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        BlendMode blend)
    {
        if (_depthState != DepthState.TestAndWrite)
        {
            return;
        }

        bool firstPerson = CurrentPass is ScenePass.Forward && _firstPerson && blend == BlendMode.Opaque;
        // A rigid body moving on its own -- a vehicle, a door, a spinning head -- is paired exactly as a skinned
        // one is: its own transform last frame against this one.
        bool animated = CurrentPass is ScenePass.Geometry && (resource.Animated || _objectMotion);

        if (!firstPerson && !animated)
        {
            return;
        }

        // Rasterised with this frame's own projection, jitter included, so the depth test covers the
        // fragments the draw did. The motion itself is measured between UNJITTERED positions, as the
        // camera's is: the current jitter is applied to the previous end below as well, which keeps the
        // coverage and cancels the jitter out of the vector. The rig is drawn without a jitter at all.
        Matrix4f rasterNow = _projection * modelView;
        Matrix4f motionNow = (firstPerson ? _projection : _temporalProjection) * modelView;
        Vector3f translation = new(modelView.M14, modelView.M24, modelView.M34);

        _objectOccurrence.TryGetValue(mesh.Id, out int occurrence);
        _objectOccurrence[mesh.Id] = occurrence + 1;
        long key = ((long)mesh.Id << 16) | (uint)(occurrence & 0xFFFF);

        // With nothing to pair against, the draw reports no motion of its own -- and no pose motion
        // either, or the two halves would disagree about which frame they measure from.
        Matrix4f previous = rasterNow;
        float previousPoseBlend = _poseBlend;

        if (_objectMotionPrevious.TryGetValue(key, out ObjectMotion before)
            && (translation - before.ViewTranslation).LengthSquared <= MaxPairedJumpSquared
            && _scene is { } scene)
        {
            Matrix4f currentJitter = firstPerson
                ? Matrix4f.Identity
                : Matrix4f.CreateTranslation(
                    _taa.JitterX * 2f / Math.Max(1, scene.Width),
                    _taa.JitterY * 2f / Math.Max(1, scene.Height),
                    0f);
            previous = currentJitter * before.MotionTransform;
            previousPoseBlend = _previousPoseBlend;
        }

        _objectMotionCurrent[key] = new ObjectMotion(motionNow, translation);
        _objectVelocityDraws.Add(new ObjectVelocityDraw(
            resource.VertexBuffer,
            resource.PreviousVertexBuffer,
            resource.IndexBuffer,
            indexOffset,
            indexCount,
            rasterNow,
            previous,
            firstPerson,
            _poseBlend,
            previousPoseBlend));
    }

    /// <summary>Stamps the frame's moving draws over the camera-derived velocity.</summary>
    /// <param name="scene">The frame's targets, whose depth is final.</param>
    private void RecordObjectVelocity(SceneTargetSet scene)
    {
        if (_objectVelocityDraws.Count == 0)
        {
            return;
        }

        _objectVelocity ??= new ObjectVelocityPass(_device);
        _objectVelocity.Record(Encoder(), scene, _objectVelocityDraws, _profiler.Time(RenderStage.TemporalResolve));
    }

    /// <summary>Makes this frame's transforms the ones the next frame pairs against.</summary>
    private void RollObjectMotion()
    {
        _objectMotionPrevious.Clear();

        foreach (KeyValuePair<long, ObjectMotion> entry in _objectMotionCurrent)
        {
            _objectMotionPrevious[entry.Key] = entry.Value;
        }

        _objectMotionCurrent.Clear();
        _objectOccurrence.Clear();
        _objectVelocityDraws.Clear();
    }

    private void DisposeObjectVelocity()
    {
        _objectVelocity?.Dispose();
        _objectVelocity = null;
    }

    /// <summary>One draw's transform as the next frame measures from it.</summary>
    /// <param name="MotionTransform">Its unjittered model-view-projection.</param>
    /// <param name="ViewTranslation">Where it stood in view space, for the pairing's plausibility guard.</param>
    private readonly record struct ObjectMotion(Matrix4f MotionTransform, Vector3f ViewTranslation);
}
