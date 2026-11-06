using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// Which of the frame's lamps can reach each cell of the view: tiles of the screen by slices of depth, for the
/// shading and the lamp trace to walk instead of every lamp.
/// </summary>
/// <remarks>
/// <para>
/// A level's lamps run to the hundreds and the frame hands up to <see cref="DeferredLocalLight.Maximum"/> of them
/// over, nearest first; the shading tested every one at every pixel, and the lamp trace every one at every pixel
/// it traces, though a pixel is reached by a handful. Listed per cell, a pixel walks that handful. The lists are in
/// the frame's lamp order and leave out only lamps whose reach does not meet the cell, which add nothing anywhere
/// in it, so both readers add the same terms in the same order and the picture is the one walking every lamp made.
/// </para>
/// <para>
/// Built on the device each frame from the lamp block the shading reads, against the shading's own unprojection:
/// see Lamp/Grid.slang for the bounds, and Common/LampGrid.slang for the layout and the lookup. The buffer carries
/// its shape in a header, so a reader handed <see cref="Empty"/> -- a frame that built none -- walks every lamp.
/// </para>
/// </remarks>
public sealed class LampGrid : IDisposable
{
    /// <summary>A tile's edge in pixels; Common/LampGrid.slang's kLampGridTile.</summary>
    public const int Tile = 32;

    /// <summary>How many slices of depth; Common/LampGrid.slang's kLampGridSlices.</summary>
    public const int Slices = 32;

    /// <summary>The words one cell takes: its count and as many lamps as it lists.</summary>
    public const int CellWords = 32;

    /// <summary>The header's words, before the first cell.</summary>
    public const int HeaderWords = 8;

    /// <summary>The nearest depth the slices are spaced from, in metres; nearer still is the first slice.</summary>
    public const float NearestSlice = 0.25f;

    /// <summary>The floats in the build's parameter block: shape, unprojection, depth mapping, then the lamps.</summary>
    public const int ParameterFloats = 12 + (DeferredLocalLight.Maximum * 4);

    private readonly IRhiDevice _device;
    private readonly RetiredResources _retired;
    private readonly ComputePass _build;
    private readonly ComputeParameters _parameters;
    private IRhiBuffer? _buffer;
    private int _tilesX;
    private int _tilesY;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="LampGrid"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="framesInFlight">How many frames may be recorded before one completes.</param>
    /// <param name="retired">Where a buffer a resize replaces waits until the frames reading it finish.</param>
    public LampGrid(IRhiDevice device, int framesInFlight, RetiredResources retired)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(retired);
        _device = device;
        _retired = retired;
        _build = new ComputePass(device, ShaderModuleId.LampGrid, "lamp.grid");
        _parameters = new ComputeParameters(device, ParameterFloats * sizeof(float), framesInFlight, "lamp.grid");

        // A grid of no tiles: what a reader is handed on a frame that built none, and walks every lamp from.
        Empty = device.CreateBuffer(new RhiBufferDescriptor(
            HeaderWords * sizeof(uint), RhiBufferUsage.Storage | RhiBufferUsage.CopyDestination, "lamp.grid.empty"));
        device.WriteBuffer(Empty, 0, new byte[HeaderWords * sizeof(uint)]);
    }

    /// <summary>Gets the grid this frame built, or <see cref="Empty"/> before one has been.</summary>
    public IRhiBuffer Buffer => _buffer ?? Empty;

    /// <summary>Gets a grid of no tiles, which every reader takes as "walk every lamp".</summary>
    public IRhiBuffer Empty { get; }

    /// <summary>Records the grid's build for this frame.</summary>
    /// <param name="pass">The compute pass to record into.</param>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">The frame's height in pixels.</param>
    /// <param name="unprojection">How a depth becomes a view position, exactly as the shading has it.</param>
    /// <param name="lamps">The lamp block the shading reads (<see cref="DeferredLampBlock"/>).</param>
    /// <param name="frameInFlight">Which parameter buffer this frame writes.</param>
    public void Record(
        IRhiComputePassEncoder pass,
        int width,
        int height,
        DepthUnprojection unprojection,
        ReadOnlySpan<float> lamps,
        int frameInFlight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pass);

        int tilesX = Math.Max(1, (width + Tile - 1) / Tile);
        int tilesY = Math.Max(1, (height + Tile - 1) / Tile);
        EnsureBuffer(tilesX, tilesY);

        int count = Math.Clamp((int)(lamps[0] + 0.5f), 0, DeferredLocalLight.Maximum);
        float far = MathF.Max(unprojection.Far, NearestSlice * 2f);

        Span<float> block = stackalloc float[ParameterFloats];
        block.Clear();
        block[0] = tilesX;
        block[1] = tilesY;
        block[2] = Slices;
        block[3] = count;
        block[4] = unprojection.XScale;
        block[5] = unprojection.YScale;
        block[6] = unprojection.Near;
        block[7] = unprojection.Far;
        block[8] = NearestSlice;

        // Slices per doubling of distance, spreading the slices from the nearest depth out to the far plane.
        block[9] = Slices / MathF.Log2(far / NearestSlice);

        // Tiles per whole screen, as a fraction of a tile where the last one runs past the edge.
        block[10] = width / (float)Tile;
        block[11] = height / (float)Tile;
        lamps.Slice(DeferredLampBlock.PositionOffset, count * 4).CopyTo(block[12..]);

        _build.Bind("p", _parameters.Write<float>(frameInFlight, block));
        _build.Bind("uLampGrid", _buffer!);
        _build.Dispatch(pass, tilesX * tilesY * Slices);
    }

    private void EnsureBuffer(int tilesX, int tilesY)
    {
        if (_buffer is not null && tilesX == _tilesX && tilesY == _tilesY)
        {
            return;
        }

        if (_buffer is not null)
        {
            _retired.Retire(_buffer);
        }

        ulong words = (ulong)HeaderWords + ((ulong)tilesX * (ulong)tilesY * Slices * CellWords);
        _buffer = _device.CreateBuffer(new RhiBufferDescriptor(words * sizeof(uint), RhiBufferUsage.Storage, "lamp.grid"));
        _tilesX = tilesX;
        _tilesY = tilesY;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _build.Dispose();
        _parameters.Dispose();
        _buffer?.Dispose();
        Empty.Dispose();
    }
}
