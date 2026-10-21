using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Shaders;

namespace OpenIGI.Presentation.Lighting;

/// <summary>
/// The level's local lights as the card relight reads them: their packed buffer, their switch levels,
/// and the lamps' coarse maps with the projections the relight reads them through.
/// </summary>
internal sealed class SurfaceCacheLocalLights
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>How many floats one local light occupies. Two <c>vec4</c>s.</summary>
    private const int FloatsPerLight = 12;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly Vector3d _origin;
    private StorageBufferHandle _lights;
    private int _lightCount;

    // The lamps' coarse maps, where the device draws them: every map's projection from this pass's origin,
    // and the device's count of map changes they were written at. See SetLampMapEntries.
    private StorageBufferHandle _lampMapRecords;
    private float[]? _lampMapFloats;
    private int _lampMapVersion = -1;

    // The fixtures as they were handed over, and the buffer contents built from them, kept so that a lamp
    // being switched can rewrite one float per light rather than rebuild the whole buffer.
    private IReadOnlyList<LightFixture>? _fixtures;
    private float[]? _packed;

    /// <summary>Initializes the lights over the device and the pass's origin.</summary>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="logger">Where to report what was uploaded.</param>
    /// <param name="origin">The world point the lights are rebased onto.</param>
    internal SurfaceCacheLocalLights(IGraphicsDevice device, IOpenIgiLogger logger, Vector3d origin)
    {
        _device = device;
        _logger = logger;
        _origin = origin;
    }

    /// <summary>Gets the packed light buffer the relight binds.</summary>
    internal StorageBufferHandle Buffer => _lights;

    /// <summary>Gets how many lights the buffer holds.</summary>
    internal int Count => _lightCount;

    /// <summary>Packs and uploads the fixtures, rebased onto the pass's origin.</summary>
    /// <param name="fixtures">The proposed fixtures, in world metres.</param>
    internal void SetLocalLights(IReadOnlyList<LightFixture> fixtures)
    {
        _device.DestroyStorageBuffer(_lights);
        _lights = default;
        _lightCount = fixtures.Count;
        _fixtures = fixtures;

        // At least one light's worth even when there are none: a storage buffer of no bytes is not a
        // thing to bind, and the shader reads the count rather than the buffer's size.
        float[] packed = new float[Math.Max(fixtures.Count, 1) * FloatsPerLight];

        for (int i = 0; i < fixtures.Count; i++)
        {
            LightFixture fixture = fixtures[i];
            int at = i * FloatsPerLight;

            packed[at] = (float)(fixture.Position.X - _origin.X);
            packed[at + 1] = (float)(fixture.Position.Y - _origin.Y);
            packed[at + 2] = (float)(fixture.Position.Z - _origin.Z);
            packed[at + 3] = fixture.Radius;

            packed[at + 4] = fixture.Colour.X;
            packed[at + 5] = fixture.Colour.Y;
            packed[at + 6] = fixture.Colour.Z;
            packed[at + 7] = MathF.Min(fixture.Intensity, SurfaceCacheLighting.MaximumLampIntensity);

            // Zero unless the fixture's own geometry agreed on a facing, which the shader reads as a
            // source that shines every way rather than as one aimed at a guess.
            if (fixture.IsDirectional)
            {
                packed[at + 8] = fixture.Direction.X;
                packed[at + 9] = fixture.Direction.Y;
                packed[at + 10] = fixture.Direction.Z;
            }

            // No shadow map until SetLampMapEntries says otherwise.
            packed[at + 11] = BitConverter.Int32BitsToSingle(-1);
        }

        _packed = packed;
        _lights = _device.CreateStorageBuffer(packed.Length * sizeof(float), "cards.lights");
        _device.UpdateStorageBuffer<float>(_lights, packed);

        _logger.Information(LogCategory, $"Card lighting: {fixtures.Count} local light(s).");
    }

    /// <summary>Records which lamps hold coarse maps, re-sending only what changed.</summary>
    /// <param name="entryPerFixture">One entry per fixture, or -1 for none.</param>
    internal void SetLampMapEntries(ReadOnlySpan<int> entryPerFixture)
    {
        if (_packed is not { } packed || !_lights.IsValid)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < _lightCount; i++)
        {
            int entry = i < entryPerFixture.Length ? entryPerFixture[i] : -1;
            float bits = BitConverter.Int32BitsToSingle(entry);
            int at = (i * FloatsPerLight) + 11;
            if (BitConverter.SingleToInt32Bits(packed[at]) != entry)
            {
                packed[at] = bits;
                changed = true;
            }
        }

        if (changed)
        {
            _device.UpdateStorageBuffer<float>(_lights, packed);
        }
    }

    /// <summary>Rewrites every light's strength from a switchboard.</summary>
    /// <param name="board">The level's lamps, indexed as the fixtures were.</param>
    internal void SetLocalLightLevels(LampSwitchboard board)
    {
        if (_packed is not { Length: > 0 } packed || _fixtures is not { } fixtures || !_lights.IsValid)
        {
            return;
        }

        for (int i = 0; i < fixtures.Count && i < board.Count; i++)
        {
            packed[(i * FloatsPerLight) + 7] = MathF.Min(fixtures[i].Intensity, SurfaceCacheLighting.MaximumLampIntensity) * board.LevelOf(i);
        }

        _device.UpdateStorageBuffer<float>(_lights, packed);
    }

    /// <summary>Binds the lamps' coarse maps and their projections, refreshed when the maps have changed.</summary>
    /// <remarks>
    /// Every dispatch of the program binds them, whatever its mode: the modes share one set of bindings and
    /// an unwritten one is a fault. Where the device draws no maps, the buffer holds no drawn map and the
    /// texture slot is left to the device's stand-in.
    /// </remarks>
    internal void BindLampMaps()
    {
        int floats = IGraphicsDevice.LampBounceMapCapacity * IGraphicsDevice.LampMapRecordFloats;
        if (!_lampMapRecords.IsValid)
        {
            _lampMapFloats = new float[floats];
            _lampMapRecords = _device.CreateStorageBuffer(floats * sizeof(float), "cards.lampmaps");
            _device.UpdateStorageBuffer<float>(_lampMapRecords, _lampMapFloats);
            _lampMapVersion = -1;
        }

        bool maps = _device.LampBounceMapSlots > 0;
        int version = maps ? _device.LampBounceMapVersion : -2;
        if (version != _lampMapVersion && _lampMapFloats is { } records)
        {
            if (maps)
            {
                _device.WriteLampBounceMaps(_origin, records);
            }
            else
            {
                Array.Clear(records);
            }

            _device.UpdateStorageBuffer<float>(_lampMapRecords, records);
            _lampMapVersion = version;
        }

        _device.BindStorageBuffer(7, _lampMapRecords);
        _device.SetTexture(8, maps ? _device.LampBounceMapArray : TextureHandle.None);
    }

    /// <summary>Destroys the light buffer and the lamp-map records.</summary>
    internal void Destroy()
    {
        _device.DestroyStorageBuffer(_lights);
        _device.DestroyStorageBuffer(_lampMapRecords);
    }
}
