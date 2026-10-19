using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>A boned model's vertices as the sequel's renderer rebuilds them on load, and their posing on a skeleton.</summary>
/// <remarks>
/// <para>
/// The file keeps I.G.I. 1's layout, a base vertex per point on its one bone and then extra weighted influences
/// that name a base vertex, but the sequel does not skin it that way. On load
/// (<c>Direct3DRender_CreateBoneRenderMesh</c>, <c>igi2ed 0x56A400</c>) every base vertex is moved from its bone's
/// frame into the model's rest frame by its bone's bind position, and gets up to four bone slots: its own bone with its
/// <c>+32</c> float turned into a weight byte, then one slot for each extra influence that names it. A fifth influence
/// takes the slot of the lightest one when it is no lighter, and is dropped otherwise. An extra influence's own
/// position is never read.
/// </para>
/// <para>
/// The bind positions are the hierarchy's translations summed down from the root
/// (<c>Direct3DRender_SetBoneTranslations</c>, <c>0x56A750</c>): the rest pose has no rotation.
/// </para>
/// <para>
/// Posing is <c>Direct3DRender_ProcessVerticesBone</c> (<c>0x568BE0</c>) as the nearest detail draws it: each slot
/// places the vertex by its bone, from the vertex less that bone's bind position, and the results are summed by
/// weight. Every slot but the last weighs its byte over 255; the last takes what is left of one. The normal is turned
/// by the first slot's bone alone. The first-person view always draws at that detail
/// (<c>Direct3DRender_DrawBoneMesh</c>, <c>0x56FC30</c>, forces it on layer zero). Morph targets, which only a face
/// sets weights for, are not applied.
/// </para>
/// </remarks>
public sealed class Igi2BoneSkin
{
    /// <summary>The most bones a vertex is placed by.</summary>
    public const int MaximumInfluences = 4;

    /// <summary>One over 255 as the renderer stores it, <c>0x3B808081</c>.</summary>
    private static readonly float InverseByte = BitConverter.Int32BitsToSingle(0x3B808081);

    private readonly Vector3f[] _bind;
    private readonly Vector3f[] _positions;
    private readonly MefVertex[] _vertices;
    private readonly uint[] _bones;
    private readonly uint[] _weights;
    private readonly byte[] _lastSlot;

    private Igi2BoneSkin(Vector3f[] bind, Vector3f[] positions, MefVertex[] vertices, uint[] bones, uint[] weights, byte[] lastSlot)
    {
        _bind = bind;
        _positions = positions;
        _vertices = vertices;
        _bones = bones;
        _weights = weights;
        _lastSlot = lastSlot;
    }

    /// <summary>Gets the number of vertices posed, the model's base vertices.</summary>
    public int VertexCount => _positions.Length;

    /// <summary>Gets the number of bones the bind pose covers.</summary>
    public int BoneCount => _bind.Length;

    /// <summary>Gets each bone's bind position, in model units.</summary>
    public ReadOnlySpan<Vector3f> BindPositions => _bind;

    /// <summary>Gets each vertex's position in the model's rest frame, in model units.</summary>
    public ReadOnlySpan<Vector3f> RestPositions => _positions;

    /// <summary>Builds a model's skin as the renderer does on load.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The skin, or <see langword="null"/> when the model is not boned or a vertex names a bone its hierarchy lacks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static Igi2BoneSkin? Create(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Format != MefMeshFormat.Skinned || model.BoneHierarchy is not { } hierarchy || model.BaseVertexCount <= 0)
        {
            return null;
        }

        Vector3f[] bind = BindPositionsOf(hierarchy);
        int count = model.BaseVertexCount;
        ReadOnlySpan<MefVertex> source = model.Vertices;
        ReadOnlySpan<MefSkinInfluence> influences = model.SkinInfluences;
        Vector3f[] positions = new Vector3f[count];
        MefVertex[] vertices = new MefVertex[count];
        uint[] bones = new uint[count];
        uint[] weights = new uint[count];
        byte[] lastSlot = new byte[count];

        for (int i = 0; i < count; i++)
        {
            MefSkinInfluence own = influences[i];
            int bone = (byte)own.BoneIndex;
            if (bone >= bind.Length)
            {
                return null;
            }

            Vector3f p = source[i].Position;
            Vector3f at = bind[bone];
            positions[i] = new Vector3f(((Fpu)at.X + p.X).F, ((Fpu)at.Y + p.Y).F, ((Fpu)at.Z + p.Z).F);
            vertices[i] = source[i];
            bones[i] = (uint)bone;
            weights[i] = WeightByte(own.Weight);
        }

        for (int i = count; i < influences.Length; i++)
        {
            MefSkinInfluence extra = influences[i];
            int target = extra.TargetVertex;
            if (target >= count)
            {
                continue;
            }

            if ((byte)extra.BoneIndex >= bind.Length)
            {
                return null;
            }

            long weight = Fpu.ToInt32((Fpu)extra.Weight * 255.0);
            int slot;
            if (lastSlot[target] == MaximumInfluences - 1)
            {
                // Full: the lightest slot, the first of equals, gives way unless it outweighs the newcomer.
                uint lightest = 255;
                slot = 0;
                for (int s = 0; s < MaximumInfluences; s++)
                {
                    uint held = (weights[target] >> (8 * s)) & 0xFF;
                    if (held < lightest)
                    {
                        lightest = held;
                        slot = s;
                    }
                }

                if (lightest > (uint)weight)
                {
                    continue;
                }

                uint keep = ~(0xFFu << (8 * slot));
                bones[target] &= keep;
                weights[target] &= keep;
            }
            else
            {
                slot = lastSlot[target] + 1;
                lastSlot[target] = (byte)slot;
            }

            bones[target] |= (uint)extra.BoneIndex << (8 * slot);
            weights[target] |= (uint)(byte)weight << (8 * slot);
        }

        return new Igi2BoneSkin(bind, positions, vertices, bones, weights, lastSlot);
    }

    /// <summary>
    /// Each bone's bind position: the hierarchy's translations summed from the root down,
    /// <c>Direct3DRender_SetBoneTranslations</c> (<c>igi2ed 0x56A750</c>).
    /// </summary>
    /// <param name="hierarchy">The model's bone hierarchy.</param>
    /// <returns>One position per bone, in model units; the root's is its own stored translation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hierarchy"/> is <see langword="null"/>.</exception>
    public static Vector3f[] BindPositionsOf(Igi2BoneHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        IReadOnlyList<Vector3f> translations = hierarchy.Translations;
        IReadOnlyList<int> parents = hierarchy.Parents;
        Vector3f[] bind = new Vector3f[hierarchy.BoneCount];
        for (int bone = 0; bone < bind.Length; bone++)
        {
            Vector3f t = translations[bone];
            int parent = parents[bone];
            if (parent < 0)
            {
                bind[bone] = t;
                continue;
            }

            Vector3f above = bind[parent];
            bind[bone] = new Vector3f(((Fpu)t.X + above.X).F, ((Fpu)t.Y + above.Y).F, ((Fpu)t.Z + above.Z).F);
        }

        return bind;
    }

    /// <summary>Gets the bones and weights one vertex is placed by.</summary>
    /// <param name="vertex">The vertex.</param>
    /// <param name="bones">Receives each slot's bone.</param>
    /// <param name="weights">Receives each slot's weight byte; the last slot's is not used.</param>
    /// <returns>The number of slots used, one to four.</returns>
    public int Influences(int vertex, Span<int> bones, Span<byte> weights)
    {
        int used = _lastSlot[vertex] + 1;
        for (int s = 0; s < used; s++)
        {
            bones[s] = (int)((_bones[vertex] >> (8 * s)) & 0xFF);
            weights[s] = (byte)(_weights[vertex] >> (8 * s));
        }

        return used;
    }

    /// <summary>Poses the vertices on a skeleton, <c>Direct3DRender_ProcessVerticesBone</c> (<c>igi2ed 0x568BE0</c>).</summary>
    /// <param name="pose">The posed skeleton, in the model's own frame.</param>
    /// <param name="scale">A factor applied to every position afterwards, for converting units.</param>
    /// <param name="destination">Receives the posed vertices; must hold at least <see cref="VertexCount"/>.</param>
    /// <returns>The number of vertices written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pose"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The pose has fewer bones than the bind pose, or the destination is too small.</exception>
    public int Skin(BonePose pose, float scale, Span<Vertex3D> destination)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (pose.BoneCount < _bind.Length)
        {
            throw new ArgumentException($"The skin covers {_bind.Length} bones but the pose has {pose.BoneCount}.", nameof(pose));
        }

        int count = _positions.Length;
        if (destination.Length < count)
        {
            throw new ArgumentException($"The skin needs {count} vertices but {destination.Length} were given.", nameof(destination));
        }

        ReadOnlySpan<Matrix3f> rotations = pose.Rotations;
        ReadOnlySpan<Vector3f> translations = pose.Positions;
        for (int i = 0; i < count; i++)
        {
            Vector3f p = _positions[i];
            int last = _lastSlot[i];
            Fpu x = 0f, y = 0f, z = 0f, total = 0f;
            for (int s = 0; s <= last; s++)
            {
                int bone = (int)((_bones[i] >> (8 * s)) & 0xFF);
                Fpu weight = s == last ? 1f - total : (Fpu)(float)((_weights[i] >> (8 * s)) & 0xFF) * InverseByte;

                Vector3f at = _bind[bone];
                Fpu dx = (Fpu)p.X - at.X, dy = (Fpu)p.Y - at.Y, dz = (Fpu)p.Z - at.Z;
                Matrix3f m = rotations[bone];
                Vector3f t = translations[bone];
                float rx = (((dz * m.M13) + (dy * m.M12)) + (dx * m.M11)).F;
                float ry = (((dz * m.M23) + (dy * m.M22)) + (dx * m.M21)).F;
                float rz = (((dz * m.M33) + (dy * m.M32)) + (dx * m.M31)).F;
                x += ((Fpu)rx + t.X) * weight;
                y += ((Fpu)ry + t.Y) * weight;
                z += ((Fpu)rz + t.Z) * weight;
                total += weight;
            }

            MefVertex vertex = _vertices[i];
            Matrix3f first = rotations[(int)(_bones[i] & 0xFF)];
            Vector3f n = vertex.Normal;
            float nx = ((((Fpu)first.M13 * n.Z) + ((Fpu)first.M11 * n.X)) + ((Fpu)first.M12 * n.Y)).F;
            float ny = ((((Fpu)first.M23 * n.Z) + ((Fpu)first.M21 * n.X)) + ((Fpu)first.M22 * n.Y)).F;
            float nz = ((((Fpu)first.M33 * n.Z) + ((Fpu)first.M31 * n.X)) + ((Fpu)first.M32 * n.Y)).F;

            destination[i] = new Vertex3D(
                new Vector3f(x.F * scale, y.F * scale, z.F * scale),
                new Vector3f(nx, ny, nz),
                vertex.DiffuseU,
                vertex.DiffuseV,
                0f,
                0f);
        }

        return count;
    }

    /// <summary>A weight as the byte the renderer keeps, <c>(u8)(i64)(w × 255.0)</c>: truncated, then its low byte.</summary>
    private static uint WeightByte(float weight) => (byte)Fpu.ToInt32((Fpu)weight * 255.0);
}
