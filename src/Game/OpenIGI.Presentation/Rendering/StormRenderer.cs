using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Simulates and draws the remastered storm: rain, the splashes it raises, and its far sheets.</summary>
/// <remarks>
/// <para>
/// The remastered counterpart to <see cref="RainRenderer"/>, which stays exactly as it is: that one is
/// the original's rain, an analytic band of thin vertical streaks, and it is what the vanilla preset
/// draws. This is a real particle system — drops are spawned, blown, aged and recycled, and each one
/// knows where it is and how fast it is going.
/// </para>
/// <para>
/// <b>Streaks alone look like the original, which is the whole point of the rest.</b> A screen of thin
/// grey lines is what the vanilla band already was; what reads as weather is everything around it — the
/// ground answering with splashes, sheets of drizzle filling the distance past where individual drops
/// stop resolving, and fat out-of-focus drops close to the lens that no amount of extra streaks
/// substitutes for. All three are here, drawn from one vertex buffer as three contiguous ranges so the
/// whole storm costs three draws.
/// </para>
/// <para>
/// <b>The drops are images, not arithmetic.</b> The streaks were procedural — a power curve across
/// the quad, tapered at the ends — and a drop of falling water does not look like that. It is a
/// wobbling, uneven line with beads of light strung along it where the surface catches. The sheet is
/// two stacked strips of ten streaks each, the same ten sharp and defocused, and a drop keeps one
/// index for its whole life and picks a strip by how far away it is. The shape of a streak inside its
/// cell — its width, how much of the cell it fills lengthways, how much fatter the defocused one is —
/// is named as a constant below.
/// </para>
/// <para>
/// <b>Simulated on the CPU, deliberately.</b> A storm of this shape is a couple of thousand live drops —
/// spawn rate times lifetime — and at that count the simulation is microseconds. The frame this
/// renderer lives in is GPU-bound, so the arithmetic is free where it is and would not be on the other
/// side; what actually costs is the translucent overdraw, which is identical whoever moves the drops.
/// </para>
/// <para>
/// <b>Where the ground is.</b> Splashes need to know what a drop lands on, and this port has no cheap
/// world height query — the ground query descends the octree per point, and a cast per drop is out of
/// the question at these rates. <see cref="GroundHeightMetres"/> is therefore a
/// single plane, set each tick from the player's own feet. Within the dozen metres a splash is visible
/// from, the ground under the player is what the rain is landing on; the approximation shows only on a
/// steep slope or a rooftop below, and it costs nothing.
/// </para>
/// <para>
/// <b>Not yet done here, and honest about it.</b> Fading a drop out as it approaches a wall
/// (<see cref="PrecipitationSettings.GeometryFadeMetres"/>) needs the fragment shader to read scene
/// depth, which no draw from this side can do; it is a backend change and is not attempted rather than
/// faked. The same want blocks the drop's two brightest terms: ideally a drop would add a blurred,
/// desaturated tap of the frame behind it — that is what backlighting and the ambient term stand for —
/// and this pass cannot read the frame it is drawing into, so the sun's own colour and the level's
/// ambient stand in for it. Wet surfaces and puddles are G-buffer work and live outside this file
/// entirely.
/// </para>
/// </remarks>
public sealed class StormRenderer : IDisposable
{
    private const string LogCategory = "Storm";

    /// <summary>The most drops ever alive, whatever the settings ask for.</summary>
    /// <remarks>
    /// This and the two below are one budget, not three: the whole storm is drawn from a single index
    /// buffer of <see langword="ushort"/>, so their sum may never exceed 16,384 quads — one more and
    /// the fourth vertex of the last quad is index 65,536, which wraps to zero and folds the far end of
    /// the storm into a spray of triangles through the origin. There is deliberately a little room left
    /// under the ceiling.
    /// </remarks>
    public const int MaximumDrops = 12000;

    /// <summary>The most splashes ever alive at once.</summary>
    public const int MaximumSplashes = 4000;

    /// <summary>The most far sheets ever alive at once.</summary>
    public const int MaximumCurtains = 64;

    /// <summary>How many streaks the drop sheet holds side by side.</summary>
    /// <remarks>
    /// Ten, in both halves of the sheet, and the count is load-bearing rather than decorative: a drop
    /// picks one of the ten and keeps it for its whole life, and the same index addresses the same
    /// streak sharp or defocused.
    /// </remarks>
    public const int SheetCells = 10;

    /// <summary>How many frames one splash animation runs for.</summary>
    /// <remarks>
    /// Eight, laid out FOUR ACROSS BY TWO DOWN in square cells. Reading the sheet as eight across by four
    /// down slices every frame into quarters and draws a corner of one splash where a whole one belongs,
    /// which shows as blocky white fragments on the ground.
    /// </remarks>
    public const int SplashFrames = 8;

    /// <summary>How many cells across the splash sheet is.</summary>
    public const int SplashColumns = 4;

    /// <summary>How many strips one splash animation occupies: eight frames, four across.</summary>
    public const int SplashFrameRows = SplashFrames / SplashColumns;

    /// <summary>How many splash animations the source image holds.</summary>
    /// <remarks>
    /// Three, packed ONE PER COLOUR CHANNEL of the same image rather than side by side, and unpacked into
    /// strips of their own when the sheet is composed. Reading only the blue channel, as the mask loader
    /// does for every other sheet, would throw two of the three away.
    /// </remarks>
    public const int SplashVariants = 3;

    /// <summary>How many strips the composed splash sheet holds: two per animation, then the blob.</summary>
    /// <remarks>
    /// The small splashes' round blob needs no animation, so it rides along as one extra strip. The drop
    /// sheet composes its two strips together for the same reason: so the whole storm draws from a single
    /// texture.
    /// </remarks>
    public const int SplashRows = (SplashVariants * SplashFrameRows) + 1;

    /// <summary>The strip the flat blob the small splashes are drawn from lives in.</summary>
    public const int SplashBlobRow = SplashRows - 1;

    /// <summary>Where the small splashes' round blob lives, relative to the executable.</summary>
    public const string DropletSheetAsset = "rain_droplet.png";

    /// <summary>Where the sharp streaks live, relative to the executable.</summary>
    public const string FocusedSheetAsset = "rain_streaks.png";

    /// <summary>Where the defocused streaks live, relative to the executable.</summary>
    public const string DefocusedSheetAsset = "rain_streaks_defocused.png";

    /// <summary>Where the splash animations live, relative to the executable.</summary>
    public const string SplashSheetAsset = "rain_splashes.png";

    /// <summary>Where the far sheets' cloud lives, relative to the executable.</summary>
    public const string CurtainSheetAsset = "rain_curtain.png";

    // Fully emissive, no occlusion, no motion history: the forward shader treats the quads as their own
    // light rather than as surfaces to be lit, and the storm is regenerated in world space every frame
    // so a previous-frame position would be meaningless.
    private static readonly SurfaceMaterial DropMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    // The falling drops themselves: clear water, drawn by a pipeline that refracts the scene behind each
    // streak, so the vertex colour carries only the light a drop throws of its own -- a lamp or the sun
    // behind it -- and never the drop's body, which is the world seen through it.
    private static readonly SurfaceMaterial RainMaterial = DropMaterial with { Rain = true };

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly PrecipitationSimulation _simulation = new();
    private readonly PrecipitationGeometry _geometry;
    private bool _reportedSpriteFailure;

    private Vertex3D[] _vertices = [];
    private ushort[] _indices = [];

    private int _lastQuads;

    private MeshHandle _mesh = MeshHandle.None;
    private TextureHandle _dropSprite = TextureHandle.None;

    // Where the sharp half of the drop sheet ends and the defocused half begins. Read from the two
    // images rather than assumed to be a half, so a re-export at a different height still lands.
    private float _sheetSplitV = 0.5f;

    private TextureHandle _splashSprite = TextureHandle.None;
    private TextureHandle _curtainSprite = TextureHandle.None;
    private bool _disposed;

    private PrecipitationSettings _settings = PrecipitationSettings.Default;
    private bool _indoors;

    /// <summary>Initializes a new instance of the <see cref="StormRenderer"/> class.</summary>
    /// <param name="device">The graphics device to draw with.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public StormRenderer(IGraphicsDevice device, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _geometry = new PrecipitationGeometry(_simulation);
        _logger = logger ?? NullLogger.Instance;
        Resize(_settings);
    }

    /// <summary>Gets or sets whether the storm is drawn at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the height rain lands at, in world metres.</summary>
    /// <remarks>See the type's remarks: one plane, set from the player's feet, deliberately.</remarks>
    public double GroundHeightMetres { get; set; } = double.NegativeInfinity;

    /// <summary>Gets or sets the world trace the rain finds its landing surfaces with.</summary>
    /// <remarks>
    /// Optional. Without one the storm falls back on <see cref="GroundHeightMetres"/> — one flat plane —
    /// which is what it had, and which rains through every roof and step in the level. See
    /// <see cref="SurfaceHeights"/> for what it is used for and why it is not one trace per drop.
    /// </remarks>
    public WorldRayCast? Ground { get; set; }

    /// <summary>Gets or sets how many world units make a metre, for <see cref="Ground"/>.</summary>
    public double UnitsPerMetre { get; set; } = 4096.0;

    /// <summary>Gets or sets whether the eye is under cover.</summary>
    /// <remarks>
    /// <b>It no longer switches the storm off, and must not.</b> Standing in a building suppressed the
    /// whole system — so the moment the player stepped inside, the rain outside the window stopped too,
    /// and the world beyond the doorway went dry while it was visibly still raining a second earlier.
    /// What has to be culled is a drop that is <em>itself</em> under a roof, which is a per-drop question
    /// and is answered per drop by the shelter test. This is kept because it says something true that a
    /// caller may want; nothing here reads it.
    /// </remarks>
    public bool Indoors
    {
        get => _indoors;
        set => _indoors = value;
    }

    /// <summary>Gets the number of drops currently alive.</summary>
    public int LiveDrops => _simulation.LiveDrops;

    /// <summary>Gets the number of splashes currently alive.</summary>
    public int LiveSplashes => _simulation.LiveSplashes;

    /// <summary>Gets the storm's tuning.</summary>
    public PrecipitationSettings Settings => _settings;

    /// <summary>Gets whether anything would be drawn this frame.</summary>
    public bool IsActive => Enabled && _settings.Intensity > 0f;

    /// <summary>Replaces the storm's tuning, resizing the buffers when the drop budget changes.</summary>
    /// <param name="settings">The new tuning.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    public void Configure(PrecipitationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        Resize(settings);
    }

    /// <summary>Supplies the roofs a drop is considered sheltered underneath.</summary>
    /// <param name="shelter">The level's top-down map of what the solid world stands over.</param>
    public void SetShelter(ShelterHeightField shelter) => _simulation.SetShelter(shelter);

    /// <summary>Advances the storm by one simulation tick.</summary>
    /// <param name="eyeMetres">Where the eye is now, in world metres; the spawn column follows it.</param>
    public void Tick(Vector3d eyeMetres)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsActive || _simulation.DropCapacity == 0)
        {
            _simulation.Halt();
            return;
        }

        _simulation.Tick(eyeMetres, _settings, GroundHeightMetres, Ground, UnitsPerMetre);
    }

    /// <summary>Draws the storm.</summary>
    /// <param name="camera">The eye to draw from; the storm is built in its local frame.</param>
    /// <param name="projection">The view projection.</param>
    /// <param name="interpolation">How far between the last two ticks the frame sits, from 0 to 1.</param>
    /// <param name="sunDirection">
    /// The direction <em>towards</em> the sun, which is the convention every other consumer of the
    /// level's <c>Dirlight</c> uses. It was documented here as the direction the light travels, and the
    /// backlighting was computed against that reading — so the drops brightened when the sun was behind
    /// the <em>camera</em>, which is the one arrangement in which real rain does not light up.
    /// </param>
    /// <param name="sunColour">The sun's colour, standing in for the frame behind a backlit drop.</param>
    /// <param name="ambientColour">The sky's light, standing in for the frame behind any drop.</param>
    /// <param name="seconds">Elapsed seconds, for the gust field and the glimmer.</param>
    /// <param name="lamps">The lamps lighting the frame, which light the drops falling through them.</param>
    /// <param name="veil">
    /// The least light a drop is drawn with, in the scene's units: see <see cref="PrecipitationGeometry.BuildDrops"/>. Nought
    /// for none.
    /// </param>
    public void Draw(
        in Camera3D camera,
        in Matrix4f projection,
        float interpolation,
        in Vector3f sunDirection,
        in Vector3f sunColour,
        in Vector3f ambientColour,
        float seconds,
        ReadOnlySpan<StormLamp> lamps = default,
        float veil = 0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsActive)
        {
            _lastQuads = 0;
            return;
        }

        Vector3d eye = camera.Position;

        TextureHandle curtainSprite;
        TextureHandle splashSprite;
        TextureHandle dropSprite;

        try
        {
            // Resolved before anything is built rather than on the way into the submit, because the drop
            // sheet is what reports where its defocused strip begins and the drops are addressed against
            // that. Fetched last, the first frame of a storm would address a sheet it had not read.
            curtainSprite = CurtainSprite();
            splashSprite = SplashSprite();
            dropSprite = DropSprite();
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // A storm that cannot get its sprites is simply not drawn; it is presentation only. But it
            // is SAID, once, because this is not a small failure: the three sheets are resolved together
            // and the rain, the splashes and the curtains all disappear together with whichever one
            // threw. Swallowing it silently cost a round trip of "the rain is gone" with nothing in the
            // log to say why.
            if (!_reportedSpriteFailure)
            {
                _reportedSpriteFailure = true;
                _logger.Warning(
                    LogCategory,
                    FormattableString.Invariant($"The storm's sprite sheets would not load ({ex.Message}); nothing is drawn."));
            }

            _lastQuads = 0;
            return;
        }

        PrecipitationSettings s = _settings;
        int curtainQuads = _geometry.BuildCurtains(_vertices, s, eye, 0);
        int splashQuads = _geometry.BuildSplashes(_vertices, s, eye, curtainQuads, ambientColour);
        int dropQuads = _geometry.BuildDrops(
            _vertices,
            s,
            _sheetSplitV,
            eye,
            curtainQuads + splashQuads,
            interpolation,
            sunDirection,
            sunColour,
            ambientColour,
            seconds,
            lamps,
            veil);
        int quads = curtainQuads + splashQuads + dropQuads;
        if (quads == 0)
        {
            _lastQuads = 0;
            return;
        }

        if (_lastQuads > quads)
        {
            Array.Clear(_vertices, quads * 4, (_lastQuads - quads) * 4);
        }

        _lastQuads = quads;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(DropMaterial);

        Matrix4f modelView = camera.CreateModelView(camera.Position);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, _indices);
            }

            _device.UpdateAnimatedMesh(_mesh, _vertices);

            // Regenerated in world space every frame; a prior skinned or particle draw must not
            // interpolate this stream toward an unrelated previous pose or the streaks shear.
            _device.ClearPoseInterpolation();

            // Far to near: the sheets sit behind everything, then the ground, then the drops.
            Submit(0, curtainQuads, modelView, curtainSprite);
            Submit(curtainQuads, splashQuads, modelView, splashSprite);
            _device.SetMaterialOverride(RainMaterial);
            Submit(curtainQuads + splashQuads, dropQuads, modelView, dropSprite);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // A frame that cannot upload is simply not drawn; the storm is presentation only.
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }
    }

    /// <summary>Removes every drop, splash and sheet, as changing level does.</summary>
    public void Clear()
    {
        _simulation.Clear();
        _lastQuads = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }

        foreach (TextureHandle texture in (ReadOnlySpan<TextureHandle>)[_dropSprite, _splashSprite, _curtainSprite])
        {
            if (texture.IsValid)
            {
                _device.DestroyTexture(texture);
            }
        }

        _dropSprite = _splashSprite = _curtainSprite = TextureHandle.None;
    }

    /// <summary>Draws one contiguous run of quads.</summary>
    /// <remarks>
    /// <b>Additive, not alpha blended.</b> Water does not take light out of what is behind it; it puts a
    /// little back, which is why rain reads against a dark wall as well as against a bright sky. Alpha
    /// blending with an ambient term that adds the frame behind the drop back on top very nearly cancels
    /// the attenuation the blend applied, and what is left is the lit drop, added. Alpha blended alone,
    /// every drop darkened whatever it crossed, and the storm sat over the scene like a sheet of
    /// scratched perspex.
    /// </remarks>
    private void Submit(int firstQuad, int quads, in Matrix4f modelView, TextureHandle texture)
    {
        if (quads <= 0)
        {
            return;
        }

        _device.DrawMesh(
            _mesh,
            firstQuad * 6,
            quads * 6,
            modelView,
            texture,
            TextureHandle.None,
            BlendMode.Additive);
    }

    private void Resize(PrecipitationSettings settings)
    {
        // A little headroom over the steady state: spawning is credit-based, so a frame after an
        // intensity change can legitimately run slightly ahead of the average.
        int wanted = Math.Clamp((int)(settings.SteadyStateDrops() * 1.15f) + 32, 0, MaximumDrops);
        if (wanted == _simulation.DropCapacity && _vertices.Length > 0)
        {
            return;
        }

        _simulation.Resize(wanted);

        int quads = _simulation.DropCapacity + MaximumSplashes + MaximumCurtains;
        _vertices = new Vertex3D[quads * 4];
        _indices = QuadIndices.Build(quads);
        _lastQuads = 0;

        // The mesh is sized by its vertex array, so a resize has to retire the old one.
        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }

        _logger.Information(
            LogCategory,
            FormattableString.Invariant(
                $"Storm sized for {_simulation.DropCapacity} drop(s), {MaximumSplashes} splash(es), {MaximumCurtains} sheet(s)."));
    }

    // The drop sheet: two strips of ten streaks stacked into one image, sharp on top and
    // defocused below, so the whole storm still draws from a single texture and a drop chooses which
    // strip it belongs to with nothing more than a v offset. ALPHA carries the shape and the colour
    // stays white, so the vertex tint decides the hue and the quad never stamps a rectangle into the
    // scene's alpha channel.
    private TextureHandle DropSprite()
    {
        if (_dropSprite.IsValid)
        {
            return _dropSprite;
        }

        (Bgra32[] pixels, int width, int height, float splitV) = PrecipitationSpriteSheets.BuildDropSheet(_logger);
        _sheetSplitV = splitV;

        _dropSprite = PrecipitationSpriteSheets.UploadSheet(_device, pixels, width, height, width / SheetCells);
        return _dropSprite;
    }

    // The splash sheet, composed rather than loaded whole.
    //
    // The source holds eight splash frames, four across and two down, with THREE different animations
    // packed one per colour channel. Nothing here can pick a channel per quad -- a vertex carries a UV
    // and no more -- so the three are unpacked into three pairs of strips and the variant chooses which
    // pair.
    //
    // The seventh strip is the small splashes' blob, loaded from its own image. It is folded in here
    // for the same reason the drop sheet folds its two strips together: one texture for the whole storm.
    private TextureHandle SplashSprite()
    {
        if (_splashSprite.IsValid)
        {
            return _splashSprite;
        }

        (Bgra32[] pixels, int width, int height) = PrecipitationSpriteSheets.BuildSplashSheet(_logger);
        _splashSprite = PrecipitationSpriteSheets.UploadSheet(_device, pixels, width, height, width / 4);
        return _splashSprite;
    }

    // The far sheets' cloud. Its mask has to reach one somewhere: a mask that never does is a sheet that
    // cannot reach the opacity it is asked for however far its alpha is turned up -- which reads as the
    // drizzle being tuned too faint.
    private TextureHandle CurtainSprite()
    {
        if (_curtainSprite.IsValid)
        {
            return _curtainSprite;
        }

        (Bgra32[] pixels, int width, int height) = PrecipitationSpriteSheets.BuildCurtainSheet(_logger);

        _curtainSprite = PrecipitationSpriteSheets.UploadSheet(_device, pixels, width, height, width);
        return _curtainSprite;
    }
}
