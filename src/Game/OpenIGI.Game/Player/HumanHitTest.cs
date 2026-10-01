using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>Where a round met a human, and everything the damage rules need to score it.</summary>
/// <param name="Position">The point of contact, in world units.</param>
/// <param name="Distance">How far along the ray it sits, in world units.</param>
/// <param name="Zone">
/// The hit zone, from <see cref="HumanDamage"/>'s own rules — a row of the struck human's table.
/// </param>
/// <param name="LocalForward">The reversed travel direction along the human's facing.</param>
/// <param name="LocalSide">The same direction's sideways component.</param>
/// <param name="Bone">
/// The bone the round went through, or <c>-1</c> when the human was not posed and no bone could be
/// named. It is what the zone was read from, so it is worth carrying.
/// </param>
public readonly record struct HumanHit(
    Vector3d Position,
    double Distance,
    int Zone,
    float LocalForward,
    float LocalSide,
    int Bone = -1);

/// <summary>One face of a human's collision mesh that a round's line crosses.</summary>
/// <param name="Entering">
/// Whether the round passes into the body here (a front face) rather than out of it. The original's cast
/// reports the two in different slots of its record (<c>+76</c> entry, <c>+80</c> exit), and
/// <c>0x47B750</c> treats them differently: only an entry is a hit.
/// </param>
/// <param name="Material">The face's global game-material row, whose bullet resistance the round must beat.</param>
/// <param name="Hit">The contact, scored as <see cref="HumanHitTest.TryCastMesh"/> scores one.</param>
public readonly record struct HumanBodyCrossing(bool Entering, int Material, HumanHit Hit);

/// <summary>
/// Casts a round at a human and works out which zone it struck.
/// </summary>
/// <remarks>
/// <para>
/// The zone rules here are the original's, out of <see cref="HumanDamage"/>: the torso default, the
/// front and rear refinement by which way the round arrived, and the <b>614.4-unit proximity test
/// against bone 4</b> that makes a head shot a head shot. Every constant is read — the body's radius
/// and height are <see cref="HumanSeparation"/>'s cylinder, which the original uses for human-versus-
/// human collision.
/// </para>
/// <para>
/// <b>The zone comes from a bone.</b> <c>sub_489C60</c> reads an index off the hit record and maps it
/// through <see cref="HumanDamage.ZoneOfBone"/>, and that index is a <b>bone index</b> — the bone the
/// skinned vertex under the ray's triangle names. This resolves it by finding the <b>bone segment
/// closest to the round's line</b> in the posed skeleton the soldier is already drawn with, which
/// agrees with the original wherever the nearest bone is the one whose skin the round went through.
/// </para>
/// <para>
/// <b>Divergence, and a bounded one.</b> The gate is still
/// <see cref="HumanSeparation"/>'s upright collision cylinder rather than the mesh, so whether a round
/// hits at all is decided by a capsule and only <i>where</i> it hit is decided by the rig. A human
/// given no pose falls back to <see cref="HumanDamage.DefaultZone"/>, which is what the port did
/// everywhere before this.
/// </para>
/// </remarks>
public static class HumanHitTest
{
    /// <summary>How far up from a human's origin its body reaches, in world units.</summary>
    /// <remarks>
    /// <see cref="HumanSeparation.HeightInUnits"/> — 1.8 m, the half-height the separation test uses. A
    /// human's origin is at its feet, so this is the whole standing body.
    /// </remarks>
    public const double BodyHeightInUnits = HumanSeparation.HeightInUnits;

    /// <summary>How wide the body is, in world units.</summary>
    /// <remarks><see cref="HumanSeparation.RadiusInUnits"/> — 0.55 m.</remarks>
    public const double BodyRadiusInUnits = HumanSeparation.RadiusInUnits;

    /// <summary>Casts a segment at one human.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="position">The human's own position — its feet.</param>
    /// <param name="facing">
    /// The human's forward direction in world space, which the front and rear refinement is measured
    /// against.
    /// </param>
    /// <param name="pose">The human's posed skeleton, for the head proximity test.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <param name="rootHeight">
    /// How far the pose's root (the pelvis) sits above the feet, in world units — the same lift the
    /// renderer draws the body with (<c>SoldierView.RootSample</c>'s Z). Zero when the pose is already
    /// rooted at the feet, as the tests build it.
    /// </param>
    /// <returns><see langword="false"/> when the round misses.</returns>
    /// <remarks>
    /// The cylinder is upright and unrotated, as the separation test's is — a human that leans does not
    /// lean its collision.
    /// </remarks>
    public static bool TryCast(
        Vector3d origin,
        Vector3d end,
        Vector3d position,
        Vector3f facing,
        BonePose? pose,
        out HumanHit hit,
        double rootHeight = 0.0)
    {
        hit = default;

        // The pose is rooted at the skeleton's ROOT (the pelvis), with the feet about a metre below it —
        // the same pose the renderer draws with its origin lifted to feet + root height. So the bones live
        // that root height above the feet; placing them at the bare feet drops the whole hit skeleton,
        // head zone and all, a pelvis-height too low and every zone reads wrong. The collision cylinder
        // still runs from the feet (position), only the bones are lifted onto the root.
        Vector3d boneOrigin = new(position.X, position.Y, position.Z + rootHeight);

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        if (!TryCylinder(origin, direction, position, out double t))
        {
            return false;
        }

        Vector3d contact = new(
            origin.X + (direction.X * t),
            origin.Y + (direction.Y * t),
            origin.Z + (direction.Z * t));

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

        // The reversed travel direction, because the original measures where the round came FROM.
        Vector3f incoming = Normalise(new Vector3f(
            (float)-direction.X, (float)-direction.Y, (float)-direction.Z));

        Vector3f forward = Normalise(facing);
        float localForward = (incoming.X * forward.X) + (incoming.Y * forward.Y);

        // The sideways axis is the facing turned a quarter turn in the ground plane, so the two
        // components the refinement compares are measured in the human's own frame.
        float localSide = (incoming.X * -forward.Y) + (incoming.Y * forward.X);

        // The bone the round went through decides the zone; without a pose there is no bone to name and
        // the trunk default stands, which is what the original does for an index past its table too.
        int bone = pose is { } rig ? NearestBone(origin, direction, boneOrigin, rig) : -1;
        int zone = HumanDamage.RefineByDirection(HumanDamage.ZoneOfBone(bone), localForward, localSide);

        if (pose is { } posed && HumanDamage.ProximityBoneIndex < posed.BoneCount)
        {
            // Measured from the ray's CLOSEST APPROACH to the bone, not from the cylinder contact.
            //
            // The original casts at the human's mesh, so for a head shot its contact point lands on the
            // head itself, comfortably inside the 15 cm the proximity test allows. This port casts at
            // HumanSeparation's collision cylinder instead, whose radius is 0.55 m -- so a side-on
            // contact is at least 0.55 m from the head bone on the axis and the test could NEVER pass.
            // Zone 15 was unreachable and every head shot scored as a body shot, which is why a guard
            // took several rounds to the head. Closest approach is the faithful quantity: it is what
            // "the round passed through the head" means, and it agrees with the mesh cast wherever the
            // mesh cast is the one that would have hit.
            Vector3f head = posed.Positions[HumanDamage.ProximityBoneIndex];
            Vector3d boneWorld = new(boneOrigin.X + head.X, boneOrigin.Y + head.Y, boneOrigin.Z + head.Z);
            double distance = DistanceToSegment(origin, direction, boneWorld);

            if (HumanDamage.TakesProximityZone((float)distance, localForward))
            {
                zone = HumanDamage.ProximityZone;
            }
        }

        hit = new HumanHit(contact, t * length, zone, localForward, localSide, bone);
        return true;
    }

    /// <summary>Casts a round at a human's <b>skinned collision mesh</b> and scores it — the faithful path.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="feet">The human's feet, in world units.</param>
    /// <param name="facing">The human's forward direction in world space, for the trunk refinement.</param>
    /// <param name="mesh">The human model's collision mesh, whose vertices carry a rig bone each.</param>
    /// <param name="pose">The posed rig, already turned to the human's facing (<c>SoldierView.Pose()</c>).</param>
    /// <param name="rootHeight">The pose root's height above the feet — <c>SoldierView.RootSample</c>'s Z.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <returns><see langword="false"/> when the round misses the body.</returns>
    /// <remarks>
    /// The original (<c>BoneDynCubeObj_TraceLinesHandler</c>) skins every collision vertex by its one bone
    /// into the current pose, ray-triangle-traces the round against the deformed mesh, and takes the
    /// <b>bone of the struck triangle</b> as the hit part; <c>sub_489C60</c> maps that bone to a zone,
    /// refines the trunk by direction, and promotes a front hit within 15 cm of bone 4 to the
    /// lethal head zone. This reproduces all of it, so a round scores at the exact limb it passed
    /// through rather than at the nearest bone to a fat cylinder.
    /// </remarks>
    public static bool TryCastMesh(
        Vector3d origin,
        Vector3d end,
        Vector3d feet,
        Vector3f facing,
        MefCollisionMesh mesh,
        BonePose pose,
        double rootHeight,
        out HumanHit hit)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(pose);
        hit = default;

        int count = mesh.Vertices.Count;
        if (count == 0 || mesh.Faces.Length == 0)
        {
            return false;
        }

        // Skin the collision vertices into the pose, placed at the pelvis — feet lifted by the root height,
        // the same origin the renderer draws the body from. The pose is already turned to the facing.
        Vector3d boneOrigin = new(feet.X, feet.Y, feet.Z + rootHeight);
        Vector3d[] world = new Vector3d[count];
        for (int i = 0; i < count; i++)
        {
            int bone = mesh.VertexBones[i];
            if ((uint)bone >= (uint)pose.BoneCount)
            {
                bone = 0;
            }

            Vector3f local = mesh.Vertices[i];
            Matrix3f r = pose.Rotations[bone];
            Vector3f t = pose.Positions[bone];
            world[i] = new Vector3d(
                boneOrigin.X + (r.M11 * local.X) + (r.M12 * local.Y) + (r.M13 * local.Z) + t.X,
                boneOrigin.Y + (r.M21 * local.X) + (r.M22 * local.Y) + (r.M23 * local.Z) + t.Y,
                boneOrigin.Z + (r.M31 * local.X) + (r.M32 * local.Y) + (r.M33 * local.Z) + t.Z);
        }

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double nearest = double.PositiveInfinity;
        int hitBone = -1;
        Vector3d contact = default;

        foreach (MefCollisionFace face in mesh.Faces)
        {
            if (!RayTriangle.Intersect(origin, direction, world[face.I0], world[face.I1], world[face.I2], out double t)
                || t >= nearest)
            {
                continue;
            }

            nearest = t;

            // The struck triangle's bone: its first vertex's, unless the other two name a different one
            // together — the original's rule when a face spans a joint (0x5655A0's v233 read).
            int b0 = mesh.VertexBones[face.I0];
            int b1 = mesh.VertexBones[face.I1];
            hitBone = b1 != b0 && b1 == mesh.VertexBones[face.I2] ? b1 : b0;
            contact = new Vector3d(
                origin.X + (direction.X * t), origin.Y + (direction.Y * t), origin.Z + (direction.Z * t));
        }

        if (hitBone < 0)
        {
            return false;
        }

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));

        Vector3f incoming = Normalise(new Vector3f((float)-direction.X, (float)-direction.Y, (float)-direction.Z));
        Vector3f forward = Normalise(facing);
        float localForward = (incoming.X * forward.X) + (incoming.Y * forward.Y);
        float localSide = (incoming.X * -forward.Y) + (incoming.Y * forward.X);

        int zone = HumanDamage.RefineByDirection(HumanDamage.ZoneOfBone(hitBone), localForward, localSide);

        // The head promotion, on the actual posed bone 4 — a front hit whose CONTACT lands within 15 cm of
        // it is lethal (0x489C60 measures the hit record's point, not the ray). The ray's closest approach
        // made a forearm raised in front of the face a headshot: the round struck the arm, but its line
        // ran on through the head.
        if (HumanDamage.ProximityBoneIndex < pose.BoneCount)
        {
            Vector3f head = pose.Positions[HumanDamage.ProximityBoneIndex];
            Vector3d headWorld = new(boneOrigin.X + head.X, boneOrigin.Y + head.Y, boneOrigin.Z + head.Z);
            double distance = Distance(contact, headWorld);
            if (HumanDamage.TakesProximityZone((float)distance, localForward))
            {
                zone = HumanDamage.ProximityZone;
            }
        }

        hit = new HumanHit(contact, nearest * length, zone, localForward, localSide, hitBone);
        return true;
    }

    /// <summary>Every face of a human's skinned collision mesh a round's line crosses, nearest first.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="feet">The human's feet, in world units.</param>
    /// <param name="facing">The human's forward direction in world space.</param>
    /// <param name="mesh">The human model's collision mesh.</param>
    /// <param name="pose">The posed rig, already turned to the human's facing.</param>
    /// <param name="rootHeight">The pose root's height above the feet.</param>
    /// <returns>The crossings in order along the ray; empty when the line misses the body.</returns>
    /// <remarks>
    /// A round that penetrates the body carries on through it (<c>0x47B750</c>), so the first contact alone
    /// cannot say what it meets next. The line starts outside the body, so its first crossing is an entry,
    /// and the entries are the crossings that face the same way; this holds whichever way the mesh is wound.
    /// </remarks>
    public static IReadOnlyList<HumanBodyCrossing> CastMeshCrossings(
        Vector3d origin,
        Vector3d end,
        Vector3d feet,
        Vector3f facing,
        MefCollisionMesh mesh,
        BonePose pose,
        double rootHeight)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(pose);
        int count = mesh.Vertices.Count;
        if (count == 0 || mesh.Faces.Length == 0)
        {
            return [];
        }

        Vector3d boneOrigin = new(feet.X, feet.Y, feet.Z + rootHeight);
        Vector3d[] world = new Vector3d[count];
        for (int i = 0; i < count; i++)
        {
            int bone = mesh.VertexBones[i];
            if ((uint)bone >= (uint)pose.BoneCount)
            {
                bone = 0;
            }

            Vector3f local = mesh.Vertices[i];
            Matrix3f r = pose.Rotations[bone];
            Vector3f t = pose.Positions[bone];
            world[i] = new Vector3d(
                boneOrigin.X + (r.M11 * local.X) + (r.M12 * local.Y) + (r.M13 * local.Z) + t.X,
                boneOrigin.Y + (r.M21 * local.X) + (r.M22 * local.Y) + (r.M23 * local.Z) + t.Y,
                boneOrigin.Z + (r.M31 * local.X) + (r.M32 * local.Y) + (r.M33 * local.Z) + t.Z);
        }

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        List<(double T, double Facing, int Bone, int Material)> found = [];
        foreach (MefCollisionFace face in mesh.Faces)
        {
            Vector3d a = world[face.I0], b = world[face.I1], c = world[face.I2];
            if (!RayTriangle.Intersect(origin, direction, a, b, c, out double t))
            {
                continue;
            }

            Vector3d normal = RayTriangle.Normal(a, b, c);
            double facingSign = (normal.X * direction.X) + (normal.Y * direction.Y) + (normal.Z * direction.Z);
            int b0 = mesh.VertexBones[face.I0];
            int b1 = mesh.VertexBones[face.I1];
            int bone = b1 != b0 && b1 == mesh.VertexBones[face.I2] ? b1 : b0;
            found.Add((t, facingSign, bone, mesh.GlobalMaterial(face.Material)));
        }

        if (found.Count == 0)
        {
            return [];
        }

        found.Sort(static (x, y) => x.T.CompareTo(y.T));
        bool entrySign = found[0].Facing > 0.0;
        HumanBodyCrossing[] crossings = new HumanBodyCrossing[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            (double t, double facingSign, int bone, int material) = found[i];
            Vector3d contact = new(origin.X + (direction.X * t), origin.Y + (direction.Y * t), origin.Z + (direction.Z * t));
            crossings[i] = new HumanBodyCrossing(
                (facingSign > 0.0) == entrySign,
                material,
                Score(contact, t * length, bone, direction, facing, pose, boneOrigin));
        }

        return crossings;
    }

    // The zone of a mesh contact: the bone's zone, the trunk refined by direction, and a front hit within 15 cm
    // of bone 4 promoted to the head (0x489C60, measured at the contact).
    private static HumanHit Score(
        Vector3d contact,
        double distance,
        int bone,
        Vector3d direction,
        Vector3f facing,
        BonePose pose,
        Vector3d boneOrigin)
    {
        Vector3f incoming = Normalise(new Vector3f((float)-direction.X, (float)-direction.Y, (float)-direction.Z));
        Vector3f forward = Normalise(facing);
        float localForward = (incoming.X * forward.X) + (incoming.Y * forward.Y);
        float localSide = (incoming.X * -forward.Y) + (incoming.Y * forward.X);
        int zone = HumanDamage.RefineByDirection(HumanDamage.ZoneOfBone(bone), localForward, localSide);
        if (HumanDamage.ProximityBoneIndex < pose.BoneCount)
        {
            Vector3f head = pose.Positions[HumanDamage.ProximityBoneIndex];
            Vector3d headWorld = new(boneOrigin.X + head.X, boneOrigin.Y + head.Y, boneOrigin.Z + head.Z);
            if (HumanDamage.TakesProximityZone((float)Distance(contact, headWorld), localForward))
            {
                zone = HumanDamage.ProximityZone;
            }
        }

        return new HumanHit(contact, distance, zone, localForward, localSide, bone);
    }

    /// <summary>Traces a round through the linearly blended collision mesh the renderer is presenting.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where the round stops.</param>
    /// <param name="root">The displayed model origin (pelvis), in world units.</param>
    /// <param name="facing">The displayed forward direction in world space.</param>
    /// <param name="mesh">The collision mesh whose vertices are rigidly bound to the supplied poses.</param>
    /// <param name="currentPose">The pose in the current animated GPU vertex stream, in model space.</param>
    /// <param name="previousPose">The pose in the previous animated GPU vertex stream, in model space.</param>
    /// <param name="orientation">The displayed model-space-to-world rotation.</param>
    /// <param name="previousShare">How much of the previous stream the displayed vertex contains.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <returns><see langword="false"/> when the round misses.</returns>
    /// <remarks>
    /// The desktop renderer submits <c>current + previousShare × (previous - current)</c> for every
    /// skinned vertex before applying one interpolated model transform. A hitscan fired while a target is
    /// smoothed must trace that exact triangle, not a convenient interpolated bone matrix: the two are not
    /// equivalent for a rotating limb.
    /// </remarks>
    public static bool TryCastMeshBlended(
        Vector3d origin,
        Vector3d end,
        Vector3d root,
        Vector3f facing,
        MefCollisionMesh mesh,
        BonePose currentPose,
        BonePose previousPose,
        Matrix3f orientation,
        float previousShare,
        out HumanHit hit)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(currentPose);
        ArgumentNullException.ThrowIfNull(previousPose);
        hit = default;

        int count = mesh.Vertices.Count;
        if (count == 0 || mesh.Faces.Length == 0)
        {
            return false;
        }

        float share = Math.Clamp(previousShare, 0f, 1f);
        Vector3d[] world = new Vector3d[count];
        for (int i = 0; i < count; i++)
        {
            Vector3f current = SkinCollisionVertex(mesh, i, currentPose);
            Vector3f previous = SkinCollisionVertex(mesh, i, previousPose);
            Vector3f local = new(
                current.X + (share * (previous.X - current.X)),
                current.Y + (share * (previous.Y - current.Y)),
                current.Z + (share * (previous.Z - current.Z)));
            Vector3f turned = orientation.Transposed().Transform(local);
            world[i] = new Vector3d(root.X + turned.X, root.Y + turned.Y, root.Z + turned.Z);
        }

        Vector3d direction = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        double nearest = double.PositiveInfinity;
        int hitBone = -1;
        Vector3d contact = default;

        foreach (MefCollisionFace face in mesh.Faces)
        {
            if (!RayTriangle.Intersect(origin, direction, world[face.I0], world[face.I1], world[face.I2], out double t)
                || t >= nearest)
            {
                continue;
            }

            nearest = t;
            int b0 = mesh.VertexBones[face.I0];
            int b1 = mesh.VertexBones[face.I1];
            hitBone = b1 != b0 && b1 == mesh.VertexBones[face.I2] ? b1 : b0;
            contact = new Vector3d(
                origin.X + (direction.X * t),
                origin.Y + (direction.Y * t),
                origin.Z + (direction.Z * t));
        }

        if (hitBone < 0)
        {
            return false;
        }

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        Vector3f incoming = Normalise(new Vector3f((float)-direction.X, (float)-direction.Y, (float)-direction.Z));
        Vector3f forward = Normalise(facing);
        float localForward = (incoming.X * forward.X) + (incoming.Y * forward.Y);
        float localSide = (incoming.X * -forward.Y) + (incoming.Y * forward.X);
        int zone = HumanDamage.RefineByDirection(HumanDamage.ZoneOfBone(hitBone), localForward, localSide);

        if (HumanDamage.ProximityBoneIndex < currentPose.BoneCount
            && HumanDamage.ProximityBoneIndex < previousPose.BoneCount)
        {
            int head = HumanDamage.ProximityBoneIndex;
            Vector3f current = currentPose.Positions[head];
            Vector3f previous = previousPose.Positions[head];
            Vector3f local = new(
                current.X + (share * (previous.X - current.X)),
                current.Y + (share * (previous.Y - current.Y)),
                current.Z + (share * (previous.Z - current.Z)));
            Vector3f turned = orientation.Transposed().Transform(local);
            Vector3d headWorld = new(root.X + turned.X, root.Y + turned.Y, root.Z + turned.Z);

            // From the contact, as 0x489C60 measures it (see TryCastMesh).
            double distance = Distance(contact, headWorld);
            if (HumanDamage.TakesProximityZone((float)distance, localForward))
            {
                zone = HumanDamage.ProximityZone;
            }
        }

        hit = new HumanHit(contact, nearest * length, zone, localForward, localSide, hitBone);
        return true;
    }

    private static Vector3f SkinCollisionVertex(MefCollisionMesh mesh, int vertex, BonePose pose)
    {
        int bone = mesh.VertexBones[vertex];
        if ((uint)bone >= (uint)pose.BoneCount)
        {
            bone = 0;
        }

        Vector3f local = mesh.Vertices[vertex];
        Matrix3f rotation = pose.Rotations[bone];
        Vector3f translation = pose.Positions[bone];
        return new Vector3f(
            (rotation.M11 * local.X) + (rotation.M12 * local.Y) + (rotation.M13 * local.Z) + translation.X,
            (rotation.M21 * local.X) + (rotation.M22 * local.Y) + (rotation.M23 * local.Z) + translation.Y,
            (rotation.M31 * local.X) + (rotation.M32 * local.Y) + (rotation.M33 * local.Z) + translation.Z);
    }

    /// <summary>Finds the bone whose segment passes closest to a round's line.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="direction">Its travel, origin to end.</param>
    /// <param name="position">The human's own position, which the pose is relative to.</param>
    /// <param name="pose">The posed skeleton.</param>
    /// <returns>The bone index, or <c>-1</c> when the skeleton has no bones to consider.</returns>
    /// <remarks>
    /// <para>
    /// A bone's flesh lies between <b>itself and its children</b>, not between itself and its parent:
    /// the skin over a thigh is weighted to the thigh bone at the hip, not to the knee below it. So each
    /// bone is measured as the set of segments running out to its children, and a leaf is measured as a
    /// point.
    /// </para>
    /// <para>
    /// The <b>weapon chain</b> is skipped as a candidate — bones 20 to 22 run out in front of the chest
    /// and carry no skin, so a round cannot go through them and letting one win would score a chest hit
    /// as a limb. Its root is still reachable as a <i>child</i> segment of the spine, which is where the
    /// six models that bind anything to it put their vertices.
    /// </para>
    /// </remarks>
    public static int NearestBone(Vector3d origin, Vector3d direction, Vector3d position, BonePose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);

        int best = -1;
        double bestDistance = double.PositiveInfinity;

        for (int bone = 0; bone < pose.BoneCount; bone++)
        {
            if (HumanDamage.BodyPartOfBone(bone) < 0)
            {
                continue;
            }

            Vector3d a = World(position, pose.Positions[bone]);
            double distance = double.PositiveInfinity;
            bool leaf = true;

            for (int child = bone + 1; child < pose.BoneCount; child++)
            {
                if (child >= pose.Parents.Length || pose.Parents[child] != bone)
                {
                    continue;
                }

                leaf = false;
                double toChild = SegmentToSegment(origin, direction, a, World(position, pose.Positions[child]));
                if (toChild < distance)
                {
                    distance = toChild;
                }
            }

            if (leaf)
            {
                distance = DistanceToSegment(origin, direction, a);
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = bone;
            }
        }

        return best;
    }

    private static Vector3d World(Vector3d position, Vector3f local) =>
        new(position.X + local.X, position.Y + local.Y, position.Z + local.Z);

    // The shortest distance between the round's segment and a bone's, both clamped to their own extent.
    // Sampling the bone is enough for the accuracy this needs and avoids the degenerate cases a closed
    // form has to special-case when the two segments are parallel or one of them has zero length.
    private static double SegmentToSegment(Vector3d origin, Vector3d direction, Vector3d a, Vector3d b)
    {
        const int Samples = 8;

        double best = double.PositiveInfinity;
        for (int i = 0; i <= Samples; i++)
        {
            double s = (double)i / Samples;
            Vector3d point = new(
                a.X + ((b.X - a.X) * s),
                a.Y + ((b.Y - a.Y) * s),
                a.Z + ((b.Z - a.Z) * s));

            double distance = DistanceToSegment(origin, direction, point);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    // The shortest distance from a point to the segment origin..origin+direction, clamped to the segment
    // so a bone behind the shooter or beyond the round's range is not treated as close.
    private static double DistanceToSegment(Vector3d origin, Vector3d direction, Vector3d point)
    {
        double lengthSquared = (direction.X * direction.X)
            + (direction.Y * direction.Y)
            + (direction.Z * direction.Z);

        double t = 0.0;
        if (lengthSquared > 0.0)
        {
            t = (((point.X - origin.X) * direction.X)
                + ((point.Y - origin.Y) * direction.Y)
                + ((point.Z - origin.Z) * direction.Z)) / lengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);
        }

        double dx = point.X - (origin.X + (direction.X * t));
        double dy = point.Y - (origin.Y + (direction.Y * t));
        double dz = point.Z - (origin.Z + (direction.Z * t));
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    // An upright capped cylinder from the feet to BodyHeightInUnits, radius BodyRadiusInUnits. Only the
    // segment counts, as every other cast in this engine does.
    private static bool TryCylinder(Vector3d origin, Vector3d direction, Vector3d position, out double t)
    {
        t = 0.0;

        double ox = origin.X - position.X;
        double oy = origin.Y - position.Y;

        double a = (direction.X * direction.X) + (direction.Y * direction.Y);
        double b = 2.0 * ((ox * direction.X) + (oy * direction.Y));
        double c = (ox * ox) + (oy * oy) - (BodyRadiusInUnits * BodyRadiusInUnits);

        double low = 0.0;
        double high = 1.0;

        if (a <= 0.0)
        {
            // Straight down the axis: inside the radius or nothing.
            if (c > 0.0)
            {
                return false;
            }
        }
        else
        {
            double discriminant = (b * b) - (4.0 * a * c);
            if (discriminant < 0.0)
            {
                return false;
            }

            double root = Math.Sqrt(discriminant);
            double enter = (-b - root) / (2.0 * a);
            double exit = (-b + root) / (2.0 * a);

            low = Math.Max(low, Math.Min(enter, exit));
            high = Math.Min(high, Math.Max(enter, exit));
        }

        // Then clip against the two caps.
        double footZ = position.Z;
        double headZ = position.Z + BodyHeightInUnits;

        if (Math.Abs(direction.Z) < 1e-9)
        {
            if (origin.Z < footZ || origin.Z > headZ)
            {
                return false;
            }
        }
        else
        {
            double toFoot = (footZ - origin.Z) / direction.Z;
            double toHead = (headZ - origin.Z) / direction.Z;

            low = Math.Max(low, Math.Min(toFoot, toHead));
            high = Math.Min(high, Math.Max(toFoot, toHead));
        }

        if (low > high)
        {
            return false;
        }

        t = low;
        return true;
    }

    private static Vector3f Normalise(Vector3f v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return length <= 0f ? new Vector3f(0f, 1f, 0f) : new Vector3f(v.X / length, v.Y / length, v.Z / length);
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
