using System.Runtime.InteropServices;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Presentation.Igi2.World;

/// <summary>
/// A copy of the tessellated geometry one tile's ground chunks were last streamed from, kept to tell whether a frame's
/// tessellation would stream the same chunks again.
/// </summary>
/// <remarks>
/// <para>
/// A chunk's vertices are a function of the mesh's positions, normals and ground triangles, and of each pass's material,
/// scales, mapping, triangles and weights — nothing else of the frame reaches them, the camera entering only through the
/// model-view matrix the draw is given. So a frame whose tessellation matches this copy byte for byte can draw the chunks
/// already uploaded and skip building and uploading them again.
/// </para>
/// <para>
/// The match is on the geometry, not on the view. The quadtree's split decisions and morphs are quantised, so a camera that
/// shifts by a hair — interpolated between ticks, or settling — still tessellates to the same ground most frames, where a
/// test on the eye alone would rebuild every one of them.
/// </para>
/// </remarks>
internal sealed class Igi2TerrainStreamedGeometry
{
    private readonly List<PassCopy> _passes = [];
    private Copy _positions;
    private Copy _normals;
    private Copy _ground;
    private bool _known;

    /// <summary>Forgets the copy, so the next frame streams whatever it tessellates.</summary>
    public void Forget() => _known = false;

    /// <summary>Whether the mesh is the geometry this copy was taken from, byte for byte.</summary>
    /// <param name="mesh">The frame's tessellation of the tile.</param>
    /// <returns>True when streaming the mesh would upload the same chunks again.</returns>
    public bool Matches(Igi2TerrainMesh mesh)
    {
        if (!_known
            || mesh.Passes.Count != _passes.Count
            || !_positions.Equals(MemoryMarshal.AsBytes(mesh.PositionSpan))
            || !_normals.Equals(MemoryMarshal.AsBytes(mesh.NormalSpan))
            || !_ground.Equals(MemoryMarshal.AsBytes(mesh.GroundIndices.Span)))
        {
            return false;
        }

        for (int i = 0; i < _passes.Count; i++)
        {
            if (!_passes[i].Matches(mesh.Passes[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Takes a copy of the geometry the tile's chunks were just streamed from.</summary>
    /// <param name="mesh">The tessellation that was streamed.</param>
    public void Remember(Igi2TerrainMesh mesh)
    {
        _positions.Set(MemoryMarshal.AsBytes(mesh.PositionSpan));
        _normals.Set(MemoryMarshal.AsBytes(mesh.NormalSpan));
        _ground.Set(MemoryMarshal.AsBytes(mesh.GroundIndices.Span));

        int count = mesh.Passes.Count;
        while (_passes.Count < count)
        {
            _passes.Add(new PassCopy());
        }

        _passes.RemoveRange(count, _passes.Count - count);
        for (int i = 0; i < count; i++)
        {
            _passes[i].Set(mesh.Passes[i]);
        }

        _known = true;
    }

    /// <summary>A byte copy that keeps its buffer between frames.</summary>
    private struct Copy
    {
        private byte[]? _bytes;
        private int _length;

        public readonly bool Equals(ReadOnlySpan<byte> bytes) =>
            bytes.Length == _length && bytes.SequenceEqual(_bytes.AsSpan(0, _length));

        public void Set(ReadOnlySpan<byte> bytes)
        {
            if (_bytes is null || _bytes.Length < bytes.Length)
            {
                _bytes = new byte[Math.Max(bytes.Length, (_bytes?.Length ?? 0) * 2)];
            }

            bytes.CopyTo(_bytes);
            _length = bytes.Length;
        }
    }

    private sealed class PassCopy
    {
        private Copy _indices;
        private Copy _weights;
        private int _materialId;
        private float _textureScale;
        private float _detailTextureScale;
        private int _baseTriangleCount;
        private int _mappingStyle;

        public bool Matches(in Igi2TerrainPass pass) =>
            pass.MaterialId == _materialId
            && pass.BaseTriangleCount == _baseTriangleCount
            && pass.MappingStyle == _mappingStyle
            && BitConverter.SingleToInt32Bits(pass.TextureScale) == BitConverter.SingleToInt32Bits(_textureScale)
            && BitConverter.SingleToInt32Bits(pass.DetailTextureScale) == BitConverter.SingleToInt32Bits(_detailTextureScale)
            && _indices.Equals(MemoryMarshal.AsBytes(pass.Indices.Span))
            && _weights.Equals(MemoryMarshal.AsBytes(pass.Weights.Span));

        public void Set(in Igi2TerrainPass pass)
        {
            _materialId = pass.MaterialId;
            _textureScale = pass.TextureScale;
            _detailTextureScale = pass.DetailTextureScale;
            _baseTriangleCount = pass.BaseTriangleCount;
            _mappingStyle = pass.MappingStyle;
            _indices.Set(MemoryMarshal.AsBytes(pass.Indices.Span));
            _weights.Set(MemoryMarshal.AsBytes(pass.Weights.Span));
        }
    }
}
