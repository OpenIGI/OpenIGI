using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>Poses a skinned model's vertices against a bone palette, as <c>0x49B700</c> does.</summary>
/// <remarks>
/// <para>
/// The rig is <b>one bone per vertex plus a list of extra weighted influences</b>, not a fixed
/// four-weights-per-vertex layout. The source array is two runs — <see cref="MefModel.BaseVertexCount"/>
/// base vertices followed by <see cref="MefModel.ExtraInfluenceCount"/> extras — and the same 40 bytes
/// are read two different ways: for a base vertex the trailing eight are
/// <c>{ f32 scale, u16 _, u16 bone }</c>, and for an extra they are
/// <c>{ f32 weight, u16 target, u16 bone }</c>.
/// </para>
/// <para>
/// Two details are the original's and would not be guessed. A base vertex's position is scaled by its
/// own <c>+32</c> float <b>after</b> the bone transform, and its normal is rotated but neither
/// translated nor scaled. An extra influence is <b>not</b> scaled at all, and it accumulates only into
/// the position — never the normal.
/// </para>
/// </remarks>
public static class MefSkinner
{
    /// <summary>The vertex count above which the original refuses to draw a bone model.</summary>
    /// <remarks><c>"Bone model too large. Model %s has %d vertices."</c> — and it is fatal, not a warning.</remarks>
    public const int MaximumVertices = 4000;

    /// <summary>Reports whether a model can be skinned — that it is the skinned format and has a rig.</summary>
    /// <param name="model">The model.</param>
    /// <returns><see langword="true"/> when <see cref="Skin"/> will do something.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static bool IsSkinned(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Format == MefMeshFormat.Skinned && model.SkinInfluences.Length > 0;
    }

    /// <summary>Gets the highest bone index the model's rig names, or <c>-1</c> when it has none.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The highest bone index used.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Worth checking before posing: a model's only constraint on its skeleton is this number, and
    /// exactly two shipped models — <c>005_01_1</c> and <c>005_01_2</c> — pass 32 and so need the
    /// 56-bone set.
    /// </remarks>
    public static int HighestBone(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.HighestSkinBone;
    }

    /// <summary>Skins a model into a vertex buffer.</summary>
    /// <param name="model">The skinned model.</param>
    /// <param name="pose">The posed skeleton, in the object's own space.</param>
    /// <param name="scale">A factor applied to every position, for converting units.</param>
    /// <param name="destination">
    /// Receives the posed vertices. Must hold at least <see cref="MefModel.BaseVertexCount"/> entries;
    /// anything past that is left alone, which is where an unskinned builder's extras sit.
    /// </param>
    /// <returns>The number of vertices written.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The model is not skinned, <paramref name="destination"/> is too small, or the rig names a bone
    /// the pose does not have.
    /// </exception>
    public static int Skin(MefModel model, BonePose pose, float scale, Span<Vertex3D> destination)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(pose);

        if (!IsSkinned(model))
        {
            throw new ArgumentException("The model is not in the skinned format.", nameof(model));
        }

        int baseCount = model.BaseVertexCount;
        if (destination.Length < baseCount)
        {
            throw new ArgumentException(
                $"The model needs {baseCount} vertices but {destination.Length} were given.",
                nameof(destination));
        }

        int highest = HighestBone(model);
        if (highest >= pose.BoneCount)
        {
            throw new ArgumentException(
                $"The model reaches bone {highest} but the pose has {pose.BoneCount}.",
                nameof(pose));
        }

        ReadOnlySpan<MefVertex> source = model.Vertices;
        ReadOnlySpan<MefSkinInfluence> influences = model.SkinInfluences;
        ReadOnlySpan<Matrix3f> rotations = pose.Rotations;
        ReadOnlySpan<Vector3f> positions = pose.Positions;

        // 1. Base vertices. Each is placed by its one bone, then scaled by its own trailing float.
        for (int i = 0; i < baseCount; i++)
        {
            MefVertex vertex = source[i];
            MefSkinInfluence binding = influences[i];
            Matrix3f rotation = rotations[binding.BoneIndex];
            Vector3f translation = positions[binding.BoneIndex];

            Vector3f position = Rotate(rotation, vertex.Position);
            position = new Vector3f(
                (position.X + translation.X) * binding.Weight * scale,
                (position.Y + translation.Y) * binding.Weight * scale,
                (position.Z + translation.Z) * binding.Weight * scale);

            destination[i] = new Vertex3D(
                position,
                Rotate(rotation, vertex.Normal),
                vertex.DiffuseU,
                vertex.DiffuseV,
                0f,
                0f);
        }

        // 2. Extra influences. The same 40 bytes read differently: each names a base vertex it adds to,
        // its own bone, and a weight. No scale, and the normal is left as the base vertex wrote it.
        for (int i = baseCount; i < influences.Length; i++)
        {
            MefSkinInfluence extra = influences[i];
            if (extra.TargetVertex >= baseCount)
            {
                continue;
            }

            MefVertex vertex = source[i];
            Vector3f contribution = Rotate(rotations[extra.BoneIndex], vertex.Position);
            Vector3f translation = positions[extra.BoneIndex];

            Vertex3D target = destination[extra.TargetVertex];
            destination[extra.TargetVertex] = new Vertex3D(
                new Vector3f(
                    target.Position.X + (extra.Weight * (contribution.X + translation.X) * scale),
                    target.Position.Y + (extra.Weight * (contribution.Y + translation.Y) * scale),
                    target.Position.Z + (extra.Weight * (contribution.Z + translation.Z) * scale)),
                target.Normal,
                target.DiffuseU,
                target.DiffuseV,
                target.LightmapU,
                target.LightmapV);
        }

        return baseCount;
    }

    // The palette applies its 3x3 as M * v -- 0x49B700 dots the record's rows at +36 with the source
    // position, the same convention BonePose places bones with.
    private static Vector3f Rotate(Matrix3f m, Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));
}
