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
    // A draw whose position jumped further than this since the draw it was paired with is a mispairing, not
    // motion: nothing this pass serves moves two metres in a frame. Squared, in metres. Measured in the WORLD
    // (camera-relative axes, absolute origin), never in view space: there a camera that turned -- a shaken
    // cutscene camera, a quick look round -- swings everything a few metres off by more than this, and every
    // draw in the frame was refused its pairing.
    private const float MaxPairedJumpSquared = 4f;

    private readonly List<ObjectVelocityDraw> _objectVelocityDraws = [];
    private readonly Dictionary<MotionKey, ObjectMotion> _objectMotionPrevious = [];
    private readonly Dictionary<MotionKey, ObjectMotion> _objectMotionCurrent = [];
    private readonly Dictionary<(long Identity, uint Mesh), int> _objectOccurrence = [];
    private ObjectVelocityPass? _objectVelocity;
    private Matrix4f _temporalProjection = Matrix4f.Identity;
    private float _previousPoseBlend;
    private bool _objectMotion;
    private long _objectIdentity;

    /// <inheritdoc/>
    public void SetObjectMotion(bool moving, long identity = 0)
    {
        _objectMotion = moving;
        _objectIdentity = moving ? identity : 0;
    }

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
        Vector3f viewTranslation = new(modelView.M14, modelView.M24, modelView.M34);

        // The rig's own position is meaningful only relative to the eye it moves with.
        Vector3f translation = firstPerson ? viewTranslation : WorldPosition(viewTranslation);

        // Paired within the object the renderer named, when it named one: the n-th draw of a mesh is then
        // the n-th draw of it within that object, whatever else was skipped this frame.
        long identity = _objectMotion ? _objectIdentity : 0;
        _objectOccurrence.TryGetValue((identity, mesh.Id), out int occurrence);
        _objectOccurrence[(identity, mesh.Id)] = occurrence + 1;
        MotionKey key = new(identity, mesh.Id, occurrence);

        // The first-person rig with nothing to pair against reports no motion of its own -- and no pose motion
        // either, or the two halves would disagree about which frame they measure from -- which is right for a
        // rig that moves with the camera.
        Matrix4f previous = rasterNow;
        float previousPoseBlend = _poseBlend;

        if (_objectMotionPrevious.TryGetValue(key, out ObjectMotion before)
            && (translation - before.GuardPosition).LengthSquared <= MaxPairedJumpSquared
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
        else if (!firstPerson)
        {
            // Anything else is left to the camera's reprojection. A stamp of "no motion of its own" says the
            // surface moved WITH the camera, and the temporal passes then fetched every unpaired draw's history
            // from the wrong place -- a whole frame of it whenever the camera moved and the draw order shifted.
            _objectMotionCurrent[key] = new ObjectMotion(motionNow, translation);
            return;
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

    /// <summary>A view-space point in the world, relative to the world origin rather than the camera.</summary>
    private Vector3f WorldPosition(Vector3f view) => new(
        (_viewToWorld.M11 * view.X) + (_viewToWorld.M12 * view.Y) + (_viewToWorld.M13 * view.Z) + _cameraPosition.X,
        (_viewToWorld.M21 * view.X) + (_viewToWorld.M22 * view.Y) + (_viewToWorld.M23 * view.Z) + _cameraPosition.Y,
        (_viewToWorld.M31 * view.X) + (_viewToWorld.M32 * view.Y) + (_viewToWorld.M33 * view.Z) + _cameraPosition.Z);

    /// <summary>Stamps the frame's moving draws over the camera-derived velocity.</summary>
    /// <param name="scene">The frame's targets, whose depth is final.</param>
    private void RecordObjectVelocity(SceneTargetSet scene)
    {
        if (_objectVelocityDraws.Count == 0)
        {
            return;
        }

        _objectVelocity ??= new ObjectVelocityPass(_device);
        _objectVelocity.Record(Encoder(), scene, _objectVelocityDraws, _profiler.Time(RenderStage.TemporalResolve, "object-velocity"));
    }

    /// <summary>Makes this frame's transforms the ones the next frame pairs against.</summary>
    private void RollObjectMotion()
    {
        _objectMotionPrevious.Clear();

        foreach (KeyValuePair<MotionKey, ObjectMotion> entry in _objectMotionCurrent)
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
    /// <param name="GuardPosition">Where it stood in the world (in view space for the rig), for the pairing's plausibility guard.</param>
    private readonly record struct ObjectMotion(Matrix4f MotionTransform, Vector3f GuardPosition);

    /// <summary>What pairs a draw with its own from the frame before.</summary>
    /// <param name="Identity">The object the renderer named, or nought.</param>
    /// <param name="Mesh">The mesh drawn.</param>
    /// <param name="Occurrence">Which draw of that mesh it is, within the object.</param>
    private readonly record struct MotionKey(long Identity, uint Mesh, int Occurrence);
}
