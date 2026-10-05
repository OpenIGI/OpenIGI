using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Coarse shadow maps for every lamp in the level, for the surface cache's relight.</summary>
/// <remarks>
/// <para>
/// Without ray tracing, the relight asked the distance field whether a lamp reached each patch of the cache.
/// Near the camera the field is a quarter of a metre a voxel; sixty metres out it is one or two, and the wall
/// a row of lamps is fixed to the far side of is not in it. Those lamps lit the near side of their own wall
/// in the cache, and the screen gather bounced it onto the wall the player was looking at, in a bright patch
/// the traced frame had dark. The lamp shadow maps answer this exactly, but only for the few lamps nearest
/// the camera.
/// </para>
/// <para>
/// So every lamp also gets six small faces here, drawn once after a level loads and kept: the relight's
/// question is whether a lamp's light reaches a patch of wall a hand across, which a map of sixty-four texels
/// a side answers, and a static world draws the same picture every time.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>How many coarse maps there are: six faces each for a hundred and seventy lamps.</summary>
    public const int LampBounceMapSlotCount = IGraphicsDevice.LampBounceMapCapacity;

    /// <summary>Texels along each side of one coarse face.</summary>
    private const int LampBounceMapSize = 64;

    private IRhiTexture? _lampBounceMaps;
    private IRhiTextureView? _lampBounceMapArray;
    private IRhiTextureView[] _lampBounceMapLayers = [];
    private TextureHandle _lampBounceMapHandle;
    private int _lampBounceMapVersion;

    private readonly Vector3d[] _lampBouncePositions = new Vector3d[LampBounceMapSlotCount];
    private readonly Matrix3f[] _lampBounceRotations = new Matrix3f[LampBounceMapSlotCount];
    private readonly Matrix4f[] _lampBounceProjections = new Matrix4f[LampBounceMapSlotCount];
    private readonly bool[] _lampBounceDrawn = new bool[LampBounceMapSlotCount];

    /// <inheritdoc/>
    public int LampBounceMapSlots => WillMarchLamps() ? LampBounceLayers : 0;

    /// <summary>How many coarse maps this adapter's arrays hold: whole lamps, up to the bank's size.</summary>
    /// <remarks>WebGPU guarantees no more than 256 layers, which is forty-two lamps rather than 170.</remarks>
    private int LampBounceLayers =>
        Math.Min(LampBounceMapSlotCount, _device.Limits.MaxTextureArrayLayers / 6 * 6);

    /// <inheritdoc/>
    /// <remarks>Made on first use, so a device that never marches its lamps never holds the array.</remarks>
    public TextureHandle LampBounceMapArray => Alias(ref _lampBounceMapHandle, () =>
    {
        EnsureLampBounceMaps();
        return _lampBounceMapArray;
    });

    /// <inheritdoc/>
    public int LampBounceMapVersion => _lampBounceMapVersion;

    // How many more frames the lamp shadows' history is held short, after a lamp's output changed.
    private int _lampLevelChangeFrames;

    /// <inheritdoc/>
    public void NoteLampLevelsChanged() => _lampLevelChangeFrames = LampLevelChangeHoldFrames;

    /// <summary>How long the lamp shadows' history stays short after a lamp switches, in frames.</summary>
    private const int LampLevelChangeHoldFrames = 6;

    /// <inheritdoc/>
    public bool BeginLampBounceMap(int slot, Vector3d position, Matrix3f rotation, in Matrix4f projection)
    {
        ThrowIfDisposed();

        if (slot < 0 || slot >= LampBounceLayers)
        {
            return false;
        }

        EnsureLampBounceMaps();
        _shadowPipelines ??= new ShadowPipelineCache(_device, _materialLayout, ShadowFormat);

        ClosePass();
        CloseScenePass();
        CloseShadowPass();

        _lampBouncePositions[slot] = position;
        _lampBounceRotations[slot] = rotation;
        _lampBounceProjections[slot] = projection;
        _lampBounceDrawn[slot] = true;
        _lampBounceMapVersion++;

        OpenShadowLayer(_lampBounceMapLayers[slot], LampBounceMapSize, $"lampbounce{slot}", rotation, resume: false);
        return true;
    }

    // Which coarse cube each of the frame's lamps has: its first slot, or -1.
    private readonly int[] _lampCoarseOf = CreateNone(DeferredLocalLight.Maximum);

    private readonly float[] _lampCoarseBlock = new float[TracedLampShadow.CoarseFloats];

    /// <summary>Writes this frame's coarse-map block and hands it back.</summary>
    private float[] WriteLampCoarseBlock()
    {
        WriteLampCoarseMaps(_lampCoarseBlock);
        return _lampCoarseBlock;
    }

    private static int[] CreateNone(int count)
    {
        int[] none = new int[count];
        Array.Fill(none, -1);
        return none;
    }

    /// <inheritdoc/>
    public void SetLampCoarseMapSlots(ReadOnlySpan<int> firstSlotPerLight)
    {
        ThrowIfDisposed();
        Array.Fill(_lampCoarseOf, -1);
        int count = Math.Min(firstSlotPerLight.Length, _lampCoarseOf.Length);
        for (int i = 0; i < count; i++)
        {
            int first = firstSlotPerLight[i];
            _lampCoarseOf[i] = first >= 0 && first + 6 <= LampBounceLayers ? first : -1;
        }
    }

    /// <summary>
    /// The coarse cubes of this frame's lamps, six records a lamp in light order, camera-relative, for the
    /// lamp shadow march to read where a lamp has no fine map.
    /// </summary>
    /// <param name="destination"><see cref="TracedLampShadow.CoarseFloats"/> floats.</param>
    /// <remarks>
    /// Each record is the face's world-to-clip matrix's first, second and fourth rows, then its projection's
    /// two depth terms, one when the face is drawn, and the layer of the coarse map array it lives in.
    /// </remarks>
    private void WriteLampCoarseMaps(Span<float> destination)
    {
        destination.Clear();
        if (_lampBounceMaps is null)
        {
            return;
        }

        const int floats = 16;
        Vector3d camera = new(_cameraPosition.X, _cameraPosition.Y, _cameraPosition.Z);
        int lights = Math.Min(_localLightCount, DeferredLocalLight.Maximum);
        for (int i = 0; i < lights; i++)
        {
            int first = _lampCoarseOf[i];
            if (first < 0)
            {
                continue;
            }

            for (int face = 0; face < 6; face++)
            {
                int slot = first + face;
                if (!_lampBounceDrawn[slot])
                {
                    continue;
                }

                Span<float> record = destination.Slice(((i * 6) + face) * floats, floats);
                Matrix4f m = LampMapMatrix(
                    _lampBouncePositions[slot], _lampBounceRotations[slot], _lampBounceProjections[slot], camera);
                ReadOnlySpan<float> rows =
                [
                    m.M11, m.M12, m.M13, m.M14,
                    m.M21, m.M22, m.M23, m.M24,
                    m.M41, m.M42, m.M43, m.M44,
                ];
                rows.CopyTo(record);
                record[12] = _lampBounceProjections[slot].M33;
                record[13] = _lampBounceProjections[slot].M34;
                record[14] = 1f;
                record[15] = slot;
            }
        }
    }

    /// <inheritdoc/>
    public void WriteLampBounceMaps(Vector3d origin, Span<float> destination)
    {
        ThrowIfDisposed();
        destination.Clear();

        const int floats = IGraphicsDevice.LampMapRecordFloats;
        int count = Math.Min(LampBounceLayers, destination.Length / floats);
        for (int slot = 0; slot < count; slot++)
        {
            if (!_lampBounceDrawn[slot])
            {
                continue;
            }

            Span<float> record = destination.Slice(slot * floats, floats);
            Matrix4f m = LampMapMatrix(
                _lampBouncePositions[slot], _lampBounceRotations[slot], _lampBounceProjections[slot], origin);
            ReadOnlySpan<float> rows =
            [
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44,
            ];
            rows.CopyTo(record);
            record[16] = _lampBounceProjections[slot].M33;
            record[17] = _lampBounceProjections[slot].M34;
            record[18] = 1f;
        }
    }

    /// <summary>Makes the coarse map array and its layers.</summary>
    private void EnsureLampBounceMaps()
    {
        if (_lampBounceMaps is not null)
        {
            return;
        }

        _lampBounceMaps = _device.CreateTexture(new RhiTextureDescriptor(
            LampBounceMapSize,
            LampBounceMapSize,
            ShadowFormat,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
            DepthOrArrayLayers: LampBounceLayers,
            Label: "lamp-bounce-maps"));
        _lampBounceMapArray = _lampBounceMaps.CreateView(new RhiTextureViewDescriptor(
            ArrayLayerCount: LampBounceLayers,
            Dimension: RhiTextureViewDimension.Texture2DArray,
            Label: "lamp-bounce-maps.array"));
        _lampBounceMapLayers = new IRhiTextureView[LampBounceLayers];
        for (int layer = 0; layer < _lampBounceMapLayers.Length; layer++)
        {
            _lampBounceMapLayers[layer] = _lampBounceMaps.CreateView(new RhiTextureViewDescriptor(
                BaseArrayLayer: layer,
                ArrayLayerCount: 1,
                Label: $"lamp-bounce-maps.{layer}"));
        }
    }

    private void DisposeLampBounceMaps()
    {
        foreach (IDisposable? owned in (IDisposable?[])[.. _lampBounceMapLayers, _lampBounceMapArray, _lampBounceMaps])
        {
            owned?.Dispose();
        }

        _lampBounceMapLayers = [];
        _lampBounceMapArray = null;
        _lampBounceMaps = null;
        Array.Clear(_lampBounceDrawn);
        _lampBounceMapVersion++;
    }
}
