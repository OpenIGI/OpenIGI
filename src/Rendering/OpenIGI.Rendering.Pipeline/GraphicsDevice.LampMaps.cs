using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The nearest lamps' own shadow maps, for the lamp shadows marched without ray tracing.</summary>
/// <remarks>
/// <para>
/// The distance field a software lamp shadow marches is a quarter of a metre a voxel at its finest and is
/// built from each model's own field, a metre a voxel for a building. A canopy a few centimetres thick that
/// stands between a row of wall lamps and the wall beneath them is not in it at any resolution the march
/// can tell from the wall it grows out of, and the lamps lit that wall, the ledge and the ground below
/// through it -- where the traced shadow had them dark. The leaked light then bounced, and that bounce is
/// what snapped as the camera moved.
/// </para>
/// <para>
/// So the lamps nearest the camera get what a rasteriser has always given a light: the world drawn from it
/// into a depth map. Each map is a layer of one array, drawn through the cascades' own depth-only pass with
/// a perspective projection out of the fitting's glass. It holds thin slabs, cut-outs and bodies exactly;
/// lamps without one keep the field march.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    /// <summary>How many lamps can hold a map at once.</summary>
    public const int LampMapSlotCount = 240;

    /// <summary>Texels along each side of one face of a lamp's cube.</summary>
    /// <remarks>
    /// A quarter of the sun's: a lamp lights a room, not a valley, and every lamp takes six faces. At this
    /// size twenty lamps hold cubes in some thirty megabytes, where at 512 only eight did, and the ninth
    /// fell back to the distance field and its leaks.
    /// </remarks>
    private const int LampMapSize = 256;

    private IRhiTexture? _lampMaps;
    private IRhiTextureView? _lampMapArray;
    private IRhiTextureView[] _lampMapLayers = [];
    private IRhiSampler? _lampMapSampler;

    // Each slot's map as it was drawn: the fitting's absolute position, the light's rotation and its
    // projection. Absolute so the matrix the shading reads is rebuilt against THIS frame's camera; a map
    // drawn a frame earlier is still the same picture of a static world.
    private readonly Vector3d[] _lampMapPositions = new Vector3d[LampMapSlotCount];
    private readonly Matrix3f[] _lampMapRotations = new Matrix3f[LampMapSlotCount];
    private readonly Matrix4f[] _lampMapProjections = new Matrix4f[LampMapSlotCount];
    private readonly bool[] _lampMapDrawn = new bool[LampMapSlotCount];

    // Which slot each of this frame's lamps reads, in the order the lamps were handed over; -1 for none.
    private readonly int[] _lampMapSlotOf = new int[DeferredLocalLight.Maximum];

    /// <inheritdoc/>
    public bool WantsLampShadowMaps => WillMarchLamps();

    /// <inheritdoc/>
    public int LampShadowMapSlots => LampMapSlotCount;

    /// <inheritdoc/>
    public void SetLampShadowMapSlots(ReadOnlySpan<int> slotPerLight)
    {
        ThrowIfDisposed();

        Array.Fill(_lampMapSlotOf, -1);
        int count = Math.Min(slotPerLight.Length, _lampMapSlotOf.Length);
        for (int i = 0; i < count; i++)
        {
            // The slot, with the cube bit on the first of six for a lamp that faces nowhere.
            int entry = slotPerLight[i];
            int slot = entry & 0xFF;
            int span = (entry & LampMapCubeBit) != 0 ? 6 : 1;
            _lampMapSlotOf[i] = entry >= 0 && slot + span <= LampMapSlotCount ? entry : -1;
        }
    }

    /// <inheritdoc/>
    public bool BeginLampShadowMap(int slot, Vector3d position, Matrix3f rotation, in Matrix4f projection)
    {
        ThrowIfDisposed();

        if (slot is < 0 or >= LampMapSlotCount)
        {
            return false;
        }

        EnsureLampMaps();
        _shadowPipelines ??= new ShadowPipelineCache(_device, _materialLayout, ShadowFormat);

        ClosePass();
        CloseScenePass();
        CloseShadowPass();

        _lampMapPositions[slot] = position;
        _lampMapRotations[slot] = rotation;
        _lampMapProjections[slot] = projection;
        _lampMapDrawn[slot] = true;

        OpenShadowLayer(_lampMapLayers[slot], LampMapSize, $"lampmap{slot}", rotation, resume: false);
        return true;
    }

    /// <summary>Makes the map array, its layers and the comparison sampler it is read through.</summary>
    private void EnsureLampMaps()
    {
        if (_lampMaps is not null)
        {
            return;
        }

        _lampMaps = _device.CreateTexture(new RhiTextureDescriptor(
            LampMapSize,
            LampMapSize,
            ShadowFormat,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.Sampled,
            DepthOrArrayLayers: LampMapSlotCount,
            Label: "lamp-maps"));
        _lampMapArray = _lampMaps.CreateView(new RhiTextureViewDescriptor(
            ArrayLayerCount: LampMapSlotCount,
            Dimension: RhiTextureViewDimension.Texture2DArray,
            Label: "lamp-maps.array"));
        _lampMapLayers = new IRhiTextureView[LampMapSlotCount];
        for (int layer = 0; layer < LampMapSlotCount; layer++)
        {
            _lampMapLayers[layer] = _lampMaps.CreateView(new RhiTextureViewDescriptor(
                BaseArrayLayer: layer,
                ArrayLayerCount: 1,
                Label: $"lamp-maps.{layer}"));
        }

        _lampMapSampler = _device.CreateSampler(new RhiSamplerDescriptor(
            Compare: RhiCompare.LessEqual,
            Label: "lamp-maps.comparison"));
    }

    /// <summary>Writes the maps' matrices and the lamps' slots into the field block the march reads.</summary>
    /// <remarks>
    /// Camera-relative world to the map's clip space, rebuilt every frame from where the map was drawn: the
    /// fitting's position less this frame's camera, rotated into the light and projected. A slot never
    /// drawn is handed out as none, so a lamp assigned this frame marches the field until its map exists.
    /// </remarks>
    private void WriteLampMaps(float[] block)
    {
        for (int slot = 0; slot < LampMapSlotCount; slot++)
        {
            Matrix4f matrix = Matrix4f.Identity;
            if (_lampMapDrawn[slot])
            {
                Vector3d position = _lampMapPositions[slot];
                Vector3f lampFromCamera = new(
                    (float)(position.X - _cameraPosition.X),
                    (float)(position.Y - _cameraPosition.Y),
                    (float)(position.Z - _cameraPosition.Z));
                Matrix3f rotation = _lampMapRotations[slot];
                // Into the light's frame the way the cascades go there: a Matrix3f transforms by its transpose.
                Vector3f translation = -rotation.Transposed().Transform(lampFromCamera);
                matrix = _lampMapProjections[slot] * Matrix4f.CreateRigid(rotation, translation);
            }

            matrix.CopyColumnMajorTo(block.AsSpan(LampMapMatricesAt + (slot * 16), 16));

            // The projection's own depth terms, which the matrix above has mixed with the rotation.
            block[LampMapDepthAt + (slot * 2)] = _lampMapProjections[slot].M33;
            block[LampMapDepthAt + (slot * 2) + 1] = _lampMapProjections[slot].M34;
        }

        bool any = _lampMaps is not null;
        for (int i = 0; i < DeferredLocalLight.Maximum; i++)
        {
            int entry = _lampMapSlotOf[i];
            bool usable = any && entry >= 0 && _lampMapDrawn[entry & 0xFF] && i < _localLightCount;
            block[LampMapSlotsAt + i] = BitConverter.Int32BitsToSingle(usable ? entry : -1);
        }
    }

    /// <summary>The bit a lamp's slot carries when it is the first of a cube's six.</summary>
    private const int LampMapCubeBit = 0x100;

    /// <summary>Where the maps' matrices start in the field block.</summary>
    private const int LampMapMatricesAt = 24;

    /// <summary>Where the maps' projection depth terms start in the field block, two floats each.</summary>
    private const int LampMapDepthAt = LampMapMatricesAt + (LampMapSlotCount * 16);

    /// <summary>Where the lamps' slots start in the field block, one int each.</summary>
    private const int LampMapSlotsAt = LampMapDepthAt + (LampMapSlotCount * 2);

    private void DisposeLampMaps()
    {
        foreach (IDisposable? owned in (IDisposable?[])[.. _lampMapLayers, _lampMapArray, _lampMaps, _lampMapSampler])
        {
            owned?.Dispose();
        }

        _lampMapLayers = [];
        _lampMapArray = null;
        _lampMaps = null;
        _lampMapSampler = null;
        Array.Clear(_lampMapDrawn);
    }
}
