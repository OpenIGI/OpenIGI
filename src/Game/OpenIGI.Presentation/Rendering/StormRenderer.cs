using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A lamp the storm's drops are lit by, as the deferred lighting has it this frame.</summary>
/// <param name="Position">Where it is, in metres from the eye, in world axes.</param>
/// <param name="Reach">How far its light reaches, in metres; nothing past it.</param>
/// <param name="Radiance">Its colour times its intensity, before distance.</param>
/// <param name="Facing">The way a directional fitting points, or nought for one that shines all round.</param>
public readonly record struct StormLamp(Vector3f Position, float Reach, Vector3f Radiance, Vector3f Facing);

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

    private const float TicksPerSecond = 30f;

    // The fill the generated sheet is built to: a sharp streak covers about thirty-nine hundredths of its
    // cell's width and nine tenths of its height, so a quad sized straight from DropWidthMetres would draw
    // a streak two and a half times thinner than the tuning asks for. Dividing the quad by the fill is
    // what keeps DropWidthMetres meaning the width of the streak.
    private const float FocusedStreakFill = 0.39f;
    private const float StreakLengthFill = 0.9f;

    // The defocused half is drawn wider inside the same cell, which is the whole of the defocus: same quad,
    // same drop, a streak that comes out about two and a half times fatter because the sheet says so.
    // Nothing here widens the geometry to fake it.

    // The smallest drop, as a share of the largest. Sizes are drawn evenly between this and one, so most of
    // the column is fine drizzle-sized drops with the odd heavy one among them, and the size range is a
    // little over three to one.
    private const float SmallestDropScale = 0.32f;

    // How far one drop's pull may differ from the settings' gravity, either way. Narrow, because a drop is
    // born at its terminal speed and the difference barely shows; it is there so that drops which do slow
    // or speed up (through the vertical drift) do not all recover at the same rate.
    private const float GravitySpread = 0.08f;

    // The shortest life a drop may be given, as a share of LifetimeSeconds. Even the shortest must outlast
    // the slowest drop's fall from the top of the spawn slab to the ground, or drops would wink out mid-air.
    private const float ShortestLifeShare = 0.45f;

    // The crown flipbook. A crown's life is drawn between this share of SplashLifetimeSeconds and all of
    // it, which plays the eight frames at roughly fifteen to twenty-five a second; its size between this
    // share of SplashRadiusMetres and all of it.
    private const float ShortestCrownLifeShare = 0.55f;

    /// <summary>The smallest crown, as a share of <see cref="PrecipitationSettings.SplashRadiusMetres"/>.</summary>
    internal const float SmallestCrownShare = 0.66f;

    // Which of the three crown animations a splash plays, as cumulative shares: one is favoured and the
    // other two split the rest unevenly, because three animations in equal turns read as a pattern.
    private const float FirstVariantShare = 0.5f;
    private const float SecondVariantShare = 0.8f;

    // The small droplets thrown out of an impact: launched upwards and sideways, pulled back by a weakened
    // gravity under a strong drag, so they rise a few centimetres, drift a few more and fall back.
    private const float DropletUpSpeed = 0.55f;          // metres per second, at the slowest
    private const float DropletUpSpread = 0.3f;          // the fastest is this much quicker again
    private const float DropletSideSpeed = 0.35f;        // metres per second, at the most
    private const float DropletGravity = 4.5f;           // metres per second squared
    private const float DropletDrag = 1.0f;              // per second
    private const float DropletShortestLife = 0.05f;     // seconds
    private const float DropletLongestLife = 0.4f;       // seconds
    private const float DropletSizeShare = 0.15f;        // of SplashRadiusMetres, before the spread below
    private const float DropletSizeLow = 0.55f;
    private const float DropletSizeHigh = 1.5f;

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

    // How much of the ambient light a drop still adds over what it refracts: the faint sheen of the sky on
    // its surface. The rest of what made every drop a white scratch is now the scene behind it.
    private const float AmbientGlint = 0.12f;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly Random _random = new(0x5A1FA11);
    private bool _reportedSpriteFailure;

    private Drop[] _drops = [];
    private readonly Splash[] _splashes = new Splash[MaximumSplashes];
    private readonly SurfaceHeights _surface = new();
    private readonly Curtain[] _curtains = new Curtain[MaximumCurtains];
    private Vertex3D[] _vertices = [];
    private ushort[] _indices = [];

    private int _dropCapacity;
    private int _liveDrops;
    private int _liveSplashes;
    private int _liveCurtains;
    private int _lastQuads;
    private float _spawnCredit;
    private float _splashCredit;

    // Where the wind is now and where it is heading. State rather than settings, because the settings
    // give the range it wanders over and this is the wander.
    private float _windSpeed = float.NaN;
    private float _windDegrees;
    private float _windToSpeed;
    private float _windToDegrees;
    private float _windHold;

    private MeshHandle _mesh = MeshHandle.None;
    private TextureHandle _dropSprite = TextureHandle.None;

    // Where the sharp half of the drop sheet ends and the defocused half begins. Read from the two
    // images rather than assumed to be a half, so a re-export at a different height still lands.
    private float _sheetSplitV = 0.5f;

    private TextureHandle _splashSprite = TextureHandle.None;
    private TextureHandle _curtainSprite = TextureHandle.None;
    private bool _disposed;

    private PrecipitationSettings _settings = PrecipitationSettings.Default;

    // Where the level's roofs are, as a top-down raster. IsSheltered is asked once per live drop per
    // FRAME as well as once per splash per tick, and this answers each in a single array read at a
    // constant cost -- where the building-shell list it replaced needed a narrowed square, a bucketed
    // grid inside that square and a run of box tests inside the bucket, all of it to decide something
    // one texel already knows.
    private ShelterHeightField _shelter = ShelterHeightField.Empty;
    private bool _indoors;

    /// <summary>Initializes a new instance of the <see cref="StormRenderer"/> class.</summary>
    /// <param name="device">The graphics device to draw with.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public StormRenderer(IGraphicsDevice device, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
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
    public int LiveDrops => _liveDrops;

    /// <summary>Gets the number of splashes currently alive.</summary>
    public int LiveSplashes => _liveSplashes;

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
    public void SetShelter(ShelterHeightField shelter) => _shelter = shelter ?? ShelterHeightField.Empty;

    /// <summary>Advances the storm by one simulation tick.</summary>
    /// <param name="eyeMetres">Where the eye is now, in world metres; the spawn column follows it.</param>
    public void Tick(Vector3d eyeMetres)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsActive || _dropCapacity == 0)
        {
            _liveDrops = 0;
            _liveSplashes = 0;
            _liveCurtains = 0;
            _spawnCredit = 0f;
            _splashCredit = 0f;
            return;
        }

        const float Step = 1f / TicksPerSecond;
        PrecipitationSettings s = _settings;
        (float windX, float windY, float windZ) = StepWind(Step, s);
        float fall = MathF.Max(0f, s.FallSpeedMetresPerSecond * s.FallSpeedScale);

        AgeSplashes(Step);
        StepCurtains(Step, eyeMetres, s, windX, windY);

        double ground = GroundHeightMetres;
        if (Ground is { } trace)
        {
            _surface.Tick(eyeMetres, trace, UnitsPerMetre, ground);
        }

        bool landing = double.IsFinite(ground) || Ground is not null;
        SpawnSplashes(Step, eyeMetres, s, ground);

        int survivors = 0;
        for (int i = 0; i < _liveDrops; i++)
        {
            ref Drop drop = ref _drops[i];
            drop.Age += Step;
            if (drop.Age >= drop.Life)
            {
                continue;
            }

            // The sideways velocity is the one the drop was born with and is simply carried through;
            // only the fall is integrated, and the speed limit is applied to the fall ALONE.
            //
            // Clamping the whole vector was a bug with a very particular look: a drop was handed the wind
            // once, then gravity built the vertical until the total hit the limit, and from there every
            // further tick scaled the WHOLE velocity down to fit -- quietly eating the sideways part. The
            // rain set off leaning and straightened up in mid-air, and the harder the gust the more
            // completely it collapsed. Nothing here touches the lateral.
            (float driftX, float driftY) = DriftAt(drop.WindX, drop.WindY, drop.DriftX, drop.DriftY, drop.Age);

            // The air pushes up and down as well as along, and it accumulates with age the same way, so
            // the speed a drop settles at drifts from the one its size gave it.
            float terminal = MathF.Max(0.5f, drop.Terminal + (drop.DriftZ * drop.Age));
            drop.Velocity = new Vector3f(
                driftX,
                driftY,
                MathF.Max(-terminal, drop.Velocity.Z - (drop.Gravity * Step)));

            drop.Previous = drop.Position;
            drop.Position = new Vector3d(
                drop.Position.X + (drop.Velocity.X * Step),
                drop.Position.Y + (drop.Velocity.Y * Step),
                drop.Position.Z + (drop.Velocity.Z * Step));

            // Landing is a crossing rather than a proximity test: a fast drop covers thirty centimetres
            // in a tick, so anything that asks "is it near the ground" either misses most of them or
            // fires several ticks running for the same drop.
            // Retired where it meets the world, tested against the sampled height grid. It no longer raises
            // anything -- see SpawnSplashes -- but a drop that falls through a roof goes on being drawn
            // inside the building under it.
            if (landing)
            {
                double surface = SurfaceUnder(drop.Position.X, drop.Position.Y, ground);
                if (double.IsFinite(surface) && drop.Previous.Z > surface && drop.Position.Z <= surface)
                {
                    continue;
                }
            }

            if (survivors != i)
            {
                _drops[survivors] = drop;
            }

            survivors++;
        }

        _liveDrops = survivors;

        // Carry the fraction over rather than rounding it away: at low intensities the whole rate is
        // under one drop a tick, and rounding would spawn nothing at all.
        _spawnCredit += MathF.Max(0f, s.DropsPerSecond * Math.Clamp(s.Intensity, 0f, 1f)) * Step;
        int wanted = (int)_spawnCredit;
        _spawnCredit -= wanted;

        for (int n = 0; n < wanted && _liveDrops < _dropCapacity; n++)
        {
            Spawn(ref _drops[_liveDrops], eyeMetres, s, windX, windY, windZ, fall);
            _liveDrops++;
        }
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
    /// The least light a drop is drawn with, in the scene's units: see <see cref="BuildDrops"/>. Nought
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

        int curtainQuads = BuildCurtains(eye, 0);
        int splashQuads = BuildSplashes(eye, curtainQuads, ambientColour);
        int dropQuads = BuildDrops(
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
        _liveDrops = 0;
        _liveSplashes = 0;
        _liveCurtains = 0;
        _spawnCredit = 0f;
        _splashCredit = 0f;
        _lastQuads = 0;
        _windSpeed = float.NaN;
        _surface.Clear();
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

    /// <summary>Wanders the wind towards a new speed and heading, and returns where it is now.</summary>
    /// <remarks>
    /// <para>
    /// A target inside the settings' range, eased towards, and replaced before it is reached. What that
    /// gives is rain whose lean is never quite the same twice and never changes abruptly either — the
    /// two things a single fixed wind speed cannot do. A step change would be worse than no wind at all:
    /// the whole column shears in one frame, which reads as the camera being knocked rather than as air.
    /// </para>
    /// <para>
    /// The easing is per second and independent of the target, so an unusually large change takes
    /// proportionally longer rather than snapping — and the hold before the next target is itself
    /// randomised, so the wind has no period for the eye to lock onto.
    /// </para>
    /// </remarks>
    private (float X, float Y, float Z) StepWind(float step, PrecipitationSettings s)
    {
        float mean = s.WindMetresPerSecond;
        float gust = MathF.Max(0f, s.WindGustMetresPerSecond);
        float swing = MathF.Max(0f, s.WindSwingDegrees);

        // Started at the mean rather than at zero, so the first seconds of a storm are not a dead calm
        // that then picks up for no reason anyone can see.
        if (!float.IsFinite(_windSpeed))
        {
            _windSpeed = mean;
            _windDegrees = s.WindDirectionDegrees;
            _windHold = 0f;
        }

        _windHold -= step;
        if (_windHold <= 0f)
        {
            // Two uniforms summed, which is a triangular distribution: signed about the mean, and far
            // more likely to land near it than at either extreme. A flat range between a floor and a
            // ceiling never crosses back through vertical and spends as long at its limits as anywhere
            // else, which is what made the lean look bolted on.
            float signed = ((float)_random.NextDouble() + (float)_random.NextDouble()) - 1f;
            _windToSpeed = mean + (signed * gust);
            _windToDegrees = s.WindDirectionDegrees + (((float)_random.NextDouble() * 2f) - 1f) * swing;
            _windHold = MathF.Max(0.5f, s.WindVariationSeconds) * (0.6f + ((float)_random.NextDouble() * 0.8f));
        }

        float ease = Math.Clamp(step / MathF.Max(0.5f, s.WindVariationSeconds), 0f, 1f);
        _windSpeed += (_windToSpeed - _windSpeed) * ease;
        _windDegrees += (_windToDegrees - _windDegrees) * ease;

        return s.WindVelocity(_windSpeed, _windDegrees);
    }

    /// <summary>A cheap deterministic hash of a lattice point, which <see cref="SmoothNoise"/> is built from.</summary>
    /// <remarks>
    /// <para>
    /// Internal so the gust shape can be asserted on directly: a field that is accidentally constant
    /// makes the storm look even rather than broken, which is not a difference anyone spots by eye.
    /// </para>
    /// <para>
    /// <b>Integers, not a sine.</b> This was the usual <c>frac(sin(dot(p, k)) * 43758)</c>, which costs a
    /// transcendental. Twelve thousand drops each taking two smoothed samples is a hundred and ninety
    /// thousand sines a frame, which is milliseconds of CPU spent on a field nobody can point at — and
    /// the sine's own periodicity puts visible structure in it at large coordinates, where the world
    /// actually is. A bit mix is a handful of integer operations and better distributed.
    /// </para>
    /// </remarks>
    internal static float Noise(float x, float y, float z)
    {
        unchecked
        {
            uint hash = (uint)((int)MathF.Floor(x) * 73856093)
                ^ (uint)((int)MathF.Floor(y) * 19349663)
                ^ (uint)((int)MathF.Floor(z) * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5BD1E995u;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFFu) * (1f / 0xFFFFFFu);
        }
    }

    /// <summary>A smooth value noise in [0, 1]: the hash on a unit lattice, interpolated between.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all.</b> The gust field was the raw hash, sampled at the drop's own
    /// position. A hash has no correlation length: two drops a hand's breadth apart get unrelated
    /// values, so what was meant to arrive as sheets of rain came out as per-drop random opacity —
    /// invisible as sheets, and visible as the storm having a slightly dirty, speckled quality. A gust
    /// field has to be smooth: neighbouring drops must agree, or there is no sheet.
    /// </para>
    /// <para>
    /// The same is true of the glimmer for a different reason. Sampled from a hash on a coordinate that
    /// moves with time, a drop's flash is a fresh random number every frame — a strobe rather than a
    /// glint. Interpolated, it ramps up and back down over the several frames it takes the coordinate
    /// to cross a lattice cell, which is what a drop catching the light actually does.
    /// </para>
    /// </remarks>
    internal static float SmoothNoise(float x, float y, float z)
    {
        float fx = MathF.Floor(x), fy = MathF.Floor(y), fz = MathF.Floor(z);
        float tx = Fade(x - fx), ty = Fade(y - fy), tz = Fade(z - fz);

        float c000 = Noise(fx, fy, fz);
        float c100 = Noise(fx + 1f, fy, fz);
        float c010 = Noise(fx, fy + 1f, fz);
        float c110 = Noise(fx + 1f, fy + 1f, fz);
        float c001 = Noise(fx, fy, fz + 1f);
        float c101 = Noise(fx + 1f, fy, fz + 1f);
        float c011 = Noise(fx, fy + 1f, fz + 1f);
        float c111 = Noise(fx + 1f, fy + 1f, fz + 1f);

        float x00 = c000 + ((c100 - c000) * tx);
        float x10 = c010 + ((c110 - c010) * tx);
        float x01 = c001 + ((c101 - c001) * tx);
        float x11 = c011 + ((c111 - c011) * tx);
        float y0 = x00 + ((x10 - x00) * ty);
        float y1 = x01 + ((x11 - x01) * ty);
        return y0 + ((y1 - y0) * tz);

        // Smoothstep on the cell fraction. Interpolating linearly leaves a crease at every lattice
        // plane, and a crease in a gust field is a straight edge crossing the storm.
        static float Fade(float t) => t * t * (3f - (2f * t));
    }

    /// <summary>The light a particle is seen by: the frame's sky fill, desaturated and scaled.</summary>
    /// <remarks>
    /// <para>
    /// One frame's worth, so it is computed once and handed to every particle rather than recomputed per
    /// quad. What it stands in for, and why it is not the frame itself, is in the type's own remarks.
    /// </para>
    /// <para>
    /// <b>It multiplies the particle's colour; it is not added to it.</b> Adding a desaturated tap of the
    /// light behind a drop to its emissive while also lighting its base colour comes out, over alpha
    /// blending, very close to simply adding the lit colour, because the ambient term all but cancels the
    /// attenuation the blend applies. So the drops are drawn additively and carry the lit colour, which is
    /// one multiply instead of two terms and a blend, and lands in the same place.
    /// </para>
    /// </remarks>
    // The average of max(n.l, 0) over a sphere: how much of a light a small round body facing every way
    // takes, against a surface turned square to it.
    private const float SphereAverageCosine = 0.25f;

    /// <summary>The fill a drop is lit by, raised to the veil where the scene gives it less.</summary>
    /// <remarks>Raised in brightness and not in hue, so a blue night leaves blue streaks.</remarks>
    private static Vector3f Veiled(in Vector3f fill, float veil)
    {
        float luma = (fill.X * 0.2126f) + (fill.Y * 0.7152f) + (fill.Z * 0.0722f);

        if (!(veil > luma))
        {
            return fill;
        }

        return luma > 1e-6f ? fill * (veil / luma) : new Vector3f(veil, veil, veil);
    }

    private static (float R, float G, float B) Ambient(PrecipitationSettings s, in Vector3f ambientColour)
    {
        float luma =
            (ambientColour.X * 0.212639f) + (ambientColour.Y * 0.715169f) + (ambientColour.Z * 0.072192f);
        float greying = Math.Clamp(1f - s.AmbientSaturation, 0f, 1f);
        float scale = MathF.Max(0f, s.AmbientIntensity);
        return (
            (ambientColour.X + ((luma - ambientColour.X) * greying)) * scale,
            (ambientColour.Y + ((luma - ambientColour.Y) * greying)) * scale,
            (ambientColour.Z + ((luma - ambientColour.Z) * greying)) * scale);
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
        if (wanted == _dropCapacity && _vertices.Length > 0)
        {
            return;
        }

        _dropCapacity = wanted;
        _drops = new Drop[_dropCapacity];

        int quads = _dropCapacity + MaximumSplashes + MaximumCurtains;
        _vertices = new Vertex3D[quads * 4];
        _indices = QuadIndices.Build(quads);
        _liveDrops = Math.Min(_liveDrops, _dropCapacity);
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
                $"Storm sized for {_dropCapacity} drop(s), {MaximumSplashes} splash(es), {MaximumCurtains} sheet(s)."));
    }

    private void Spawn(
        ref Drop drop,
        Vector3d eye,
        PrecipitationSettings s,
        float windX,
        float windY,
        float windZ,
        float fall)
    {
        // Anywhere over the disc around the eye: the square root spreads the drops evenly over its area
        // instead of crowding them at its middle.
        float radius = MathF.Sqrt((float)_random.NextDouble()) * MathF.Max(0f, s.SpawnRadiusMetres);
        float bearing = (float)_random.NextDouble() * MathF.Tau;
        float height = s.SpawnAboveCameraMetres + ((float)_random.NextDouble() * s.SpawnHeightMetres);

        drop.Position = new Vector3d(
            eye.X + (MathF.Cos(bearing) * radius),
            eye.Y + (MathF.Sin(bearing) * radius),
            eye.Z + height);
        drop.Previous = drop.Position;

        // The wind it is born in, kept for life; see DriftAt.
        drop.WindX = windX;
        drop.WindY = windY;

        (drop.DriftX, drop.DriftY, drop.DriftZ) = SpawnDrift(
            s.SpreadMetresPerSecondSquared,
            (float)_random.NextDouble(),
            (float)_random.NextDouble(),
            (float)_random.NextDouble());

        drop.Scale = SmallestDropScale + ((float)_random.NextDouble() * (1f - SmallestDropScale));
        drop.Terminal = TerminalFor(fall, drop.Scale);

        // Born already falling at its own terminal speed, so it falls in a straight line from the start.
        drop.Velocity = new Vector3f(0f, 0f, windZ - drop.Terminal);

        float pull = 1f + ((((float)_random.NextDouble() * 2f) - 1f) * GravitySpread);
        drop.Gravity = MathF.Max(0f, s.GravityMetresPerSecondSquared) * pull;

        float share = ShortestLifeShare + ((float)_random.NextDouble() * (1f - ShortestLifeShare));
        drop.Life = MathF.Max(0f, s.LifetimeSeconds) * share;

        drop.Seed = (float)_random.NextDouble();
        drop.Cell = _random.Next(SheetCells);
        drop.Age = 0f;
    }

    /// <summary>The acceleration the air gives a drop, in metres per second squared.</summary>
    /// <param name="spread">The strongest push the air may give, from the settings.</param>
    /// <param name="magnitudeUnit">A random number, zero to one, for how hard this drop is pushed.</param>
    /// <param name="angleUnit">A random number, zero to one, for its compass direction.</param>
    /// <param name="elevationUnit">A random number, zero to one, for how far above or below level.</param>
    /// <returns>The acceleration on all three axes.</returns>
    /// <remarks>
    /// <para>
    /// Three dimensions, not two. A push confined to the horizontal plane leaves the fall looking the
    /// same from above as from the side, and every drop's streak the same length: the whole variety of
    /// it then lives in one plane, which is exactly what "it looks the same at every viewing angle"
    /// describes. Air moves up and down as well as along.
    /// </para>
    /// <para>
    /// A direction and a magnitude rather than a number per axis, and the direction is even over the
    /// SPHERE — the elevation is drawn as a cosine rather than as an angle, because drawing the angle
    /// evenly crowds the poles. The magnitude takes a cube root so that the magnitudes fill the ball's
    /// VOLUME evenly instead of crowding its middle.
    /// </para>
    /// </remarks>
    internal static (float X, float Y, float Z) SpawnDrift(
        float spread,
        float magnitudeUnit,
        float angleUnit,
        float elevationUnit)
    {
        if (!(spread > 0f))
        {
            return (0f, 0f, 0f);
        }

        float magnitude = spread * MathF.Cbrt(Math.Clamp(magnitudeUnit, 0f, 1f));
        float bearing = Math.Clamp(angleUnit, 0f, 1f) * MathF.Tau;
        float cosine = (Math.Clamp(elevationUnit, 0f, 1f) * 2f) - 1f;
        float sine = MathF.Sqrt(MathF.Max(0f, 1f - (cosine * cosine)));

        return (
            MathF.Cos(bearing) * sine * magnitude,
            MathF.Sin(bearing) * sine * magnitude,
            cosine * magnitude);
    }

    /// <summary>The sideways velocity a drop has drifted to by a given age, in metres per second.</summary>
    /// <param name="windX">The wind the drop was born in, east.</param>
    /// <param name="windY">The wind the drop was born in, north.</param>
    /// <param name="driftX">The drop's own acceleration, east.</param>
    /// <param name="driftY">The drop's own acceleration, north.</param>
    /// <param name="ageSeconds">How long it has been falling.</param>
    /// <returns>The horizontal velocity.</returns>
    /// <remarks>
    /// The wind is the same for every drop born at that moment and the drift is the drop's own, so what
    /// separates two streaks is how long each has been falling. This is why the fall shows a range of
    /// angles at all: it is a population of ages, not a population of velocities.
    /// </remarks>
    internal static (float X, float Y) DriftAt(
        float windX,
        float windY,
        float driftX,
        float driftY,
        float ageSeconds)
    {
        float age = MathF.Max(0f, ageSeconds);
        return (windX + (driftX * age), windY + (driftY * age));
    }

    /// <summary>How fast a drop of a given size settles to falling, in metres per second.</summary>
    /// <param name="fallMetresPerSecond">The settings' fall speed, which the largest drop reaches.</param>
    /// <param name="scale">The drop's size, from the smallest share up to one.</param>
    /// <returns>The speed, never below half a metre a second.</returns>
    /// <remarks>
    /// Straight-line in size: the largest drop falls at the full speed and the smallest at half of it.
    /// Small drops falling slower is what lets one sideways push lean them further than the big ones,
    /// which is a good part of why the column does not read as parallel lines.
    /// </remarks>
    internal static float TerminalFor(float fallMetresPerSecond, float scale)
    {
        float share = 0.5f + (0.5f * (scale - SmallestDropScale) / (1f - SmallestDropScale));
        return MathF.Max(0.5f, fallMetresPerSecond * share);
    }

    /// <summary>Raises this tick's splashes on the ground around the eye.</summary>
    /// <remarks>
    /// See <see cref="PrecipitationSettings.SplashesPerSecond"/> for why these are spawned here at a
    /// rate rather than where a drop lands. The short version: a drop's landing is almost never
    /// somewhere the player is looking, and a storm whose ground detail is correct and invisible is
    /// worth less than one whose ground detail is approximate and there.
    /// </remarks>
    private void SpawnSplashes(float step, Vector3d eye, PrecipitationSettings s, double ground)
    {
        if (!s.Splashes || !double.IsFinite(ground))
        {
            return;
        }

        _splashCredit += MathF.Max(0f, s.SplashesPerSecond * Math.Clamp(s.Intensity, 0f, 1f)) * step;
        int wanted = (int)_splashCredit;
        _splashCredit -= wanted;

        float reach = MathF.Max(s.SplashReachMetres, 1f);
        for (int n = 0; n < wanted; n++)
        {
            float radius = MathF.Sqrt((float)_random.NextDouble()) * reach;
            float bearing = (float)_random.NextDouble() * MathF.Tau;
            double x = eye.X + (MathF.Cos(bearing) * radius);
            double y = eye.Y + (MathF.Sin(bearing) * radius);

            double surface = SurfaceUnder(x, y, ground);
            if (!double.IsFinite(surface))
            {
                continue;
            }

            if (s.ShelterHeightMetres > 0f && IsSheltered(x, y, surface, s.ShelterHeightMetres))
            {
                continue;
            }

            if (_random.NextDouble() < s.SplashSmallFraction)
            {
                RaiseDroplet(x, y, surface, s);
                RaiseDroplet(x, y, surface, s);
                continue;
            }

            ref Splash crown = ref _splashes[Claim()];
            crown.X = x;
            crown.Y = y;
            crown.Z = surface + s.SplashLiftMetres;
            crown.Velocity = default;
            crown.Age = 0f;
            crown.Life = MathF.Max(0f, s.SplashLifetimeSeconds)
                * (ShortestCrownLifeShare + ((float)_random.NextDouble() * (1f - ShortestCrownLifeShare)));
            crown.Radius = MathF.Max(
                0.005f,
                s.SplashRadiusMetres * (SmallestCrownShare + ((float)_random.NextDouble() * (1f - SmallestCrownShare))));

            float pick = (float)_random.NextDouble();
            crown.Variant = pick < FirstVariantShare ? 0 : pick < SecondVariantShare ? 1 : 2;
            crown.Small = false;
            crown.FlipU = _random.NextDouble() < 0.5;
        }
    }

    /// <summary>Throws one small droplet up out of an impact.</summary>
    /// <remarks>
    /// Some die before they have risen at all and some arc over and fall back: the spread of lives is wide
    /// on purpose, because a field of droplets that all live as long reads as a pulse.
    /// </remarks>
    private void RaiseDroplet(double x, double y, double surface, PrecipitationSettings s)
    {
        ref Splash droplet = ref _splashes[Claim()];
        float bearing = (float)_random.NextDouble() * MathF.Tau;
        float side = (float)_random.NextDouble() * DropletSideSpeed;
        float up = DropletUpSpeed * (1f + ((float)_random.NextDouble() * DropletUpSpread));

        droplet.X = x;
        droplet.Y = y;
        droplet.Z = surface + s.SplashLiftMetres;
        droplet.Velocity = new Vector3f(MathF.Cos(bearing) * side, MathF.Sin(bearing) * side, up);
        droplet.Age = 0f;
        droplet.Life = DropletShortestLife + ((float)_random.NextDouble() * (DropletLongestLife - DropletShortestLife));
        droplet.Radius = s.SplashRadiusMetres * DropletSizeShare
            * (DropletSizeLow + ((float)_random.NextDouble() * (DropletSizeHigh - DropletSizeLow)));
        droplet.Variant = 0;
        droplet.Small = true;
        droplet.FlipU = _random.NextDouble() < 0.5;
    }

    /// <summary>Takes a splash slot, replacing an arbitrary live one once the pool is full.</summary>
    /// <remarks>
    /// Oldest-first would be tidier and is not worth the bookkeeping: a splash is a fraction of a second,
    /// so whichever one is replaced was nearly over anyway.
    /// </remarks>
    private int Claim() => _liveSplashes < MaximumSplashes ? _liveSplashes++ : _random.Next(MaximumSplashes);

    private void AgeSplashes(float step)
    {
        int survivors = 0;
        for (int i = 0; i < _liveSplashes; i++)
        {
            ref Splash splash = ref _splashes[i];
            splash.Age += step;
            if (splash.Age >= splash.Life)
            {
                continue;
            }

            // A droplet flies: pulled down, slowed by the air, moved. A crown stands where it struck.
            if (splash.Small)
            {
                Vector3f velocity = splash.Velocity;
                velocity = new Vector3f(velocity.X, velocity.Y, velocity.Z - (DropletGravity * step));
                float keep = MathF.Max(0f, 1f - (DropletDrag * step));
                velocity = new Vector3f(velocity.X * keep, velocity.Y * keep, velocity.Z * keep);
                splash.Velocity = velocity;
                splash.X += velocity.X * step;
                splash.Y += velocity.Y * step;
                splash.Z += velocity.Z * step;
            }

            if (survivors != i)
            {
                _splashes[survivors] = splash;
            }

            survivors++;
        }

        _liveSplashes = survivors;
    }

    private void StepCurtains(float step, Vector3d eye, PrecipitationSettings s, float windX, float windY)
    {
        if (!s.Curtains || s.CurtainCount <= 0)
        {
            _liveCurtains = 0;
            return;
        }

        int wanted = Math.Clamp(s.CurtainCount, 0, MaximumCurtains);
        float reach = CurtainReach(s);

        for (int i = 0; i < wanted; i++)
        {
            ref Curtain curtain = ref _curtains[i];
            if (i >= _liveCurtains)
            {
                PlaceCurtain(ref curtain, eye, reach);
                continue;
            }

            // The sheets drift with the wind rather than falling, and are recycled around the eye when
            // they wander out of reach -- they are the far haze, so they only have to be somewhere out
            // there, not to have come from anywhere in particular.
            curtain.X += (windX * s.CurtainMetresPerSecond * 0.1f) * step;
            curtain.Y += (windY * s.CurtainMetresPerSecond * 0.1f) * step;
            curtain.Z -= s.CurtainMetresPerSecond * 0.25f * step;

            double dx = curtain.X - eye.X;
            double dy = curtain.Y - eye.Y;
            if ((dx * dx) + (dy * dy) > reach * reach || curtain.Z < eye.Z - reach)
            {
                PlaceCurtain(ref curtain, eye, reach);
            }
        }

        _liveCurtains = wanted;
    }

    private void PlaceCurtain(ref Curtain curtain, Vector3d eye, float reach)
    {
        float angle = (float)_random.NextDouble() * MathF.Tau;

        // Never right on top of the eye: a sheet the camera is inside is a wall of fog, not weather.
        float radius = reach * (0.35f + ((float)_random.NextDouble() * 0.6f));
        curtain.X = eye.X + (MathF.Cos(angle) * radius);
        curtain.Y = eye.Y + (MathF.Sin(angle) * radius);

        // Below the eye, not above it. A sheet's opacity falls away as the direction back to the camera
        // turns upwards, so rain too far off to resolve reads as haze lying over the ground; the same haze
        // painted across the sky above the player is a cloud that has come indoors.
        curtain.Z = eye.Z + ((((float)_random.NextDouble() * 0.5f) - 0.45f) * reach);
        curtain.Seed = (float)_random.NextDouble();
    }

    /// <summary>The faint cool cast of a drop, which refracts mostly sky.</summary>
    private static readonly (float R, float G, float B) WaterTint = (0.80f, 0.90f, 1f);

    /// <summary>A drop's added light, rolled off rather than clipped: bright light approaches white, never sits on it.</summary>
    private static float WaterLevel(float light)
    {
        float level = MathF.Max(light, 0f);
        return Math.Clamp(level / (0.85f + level), 0f, 1f);
    }

    /// <summary>A repeatable number in [0, 1) for a drop, so its brightness does not flicker frame to frame.</summary>
    private static float Hash01(float seed)
    {
        float value = MathF.Sin(seed * 12.9898f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    private int BuildDrops(
        Vector3d eye,
        int firstQuad,
        float interpolation,
        in Vector3f sunDirection,
        in Vector3f sunColour,
        in Vector3f ambientColour,
        float seconds,
        ReadOnlySpan<StormLamp> lamps,
        float veil)
    {
        PrecipitationSettings s = _settings;
        float alphaScale = Math.Clamp(s.Alpha, 0f, 1f) * Math.Clamp(s.Intensity, 0f, 1f);

        // The gust field: on only when asked for and given a size it can be sampled at.
        float gustAmount = Math.Clamp(s.GustAmount, 0f, 1f);
        bool gusting = gustAmount > 0f && s.GustSizeMetres > 0.01f;
        float gustScale = gusting ? 1f / s.GustSizeMetres : 0f;
        float gustTravel = gusting ? seconds * s.GustMetresPerSecond * gustScale : 0f;

        float blend = Math.Clamp(interpolation, 0f, 1f);
        float nearFade = MathF.Max(0.01f, s.NearFadeMetres);
        float cull = nearFade * 0.35f;

        // Nearer than this, a drop is drawn from the defocused strip; nought draws every drop sharp.
        float focusDistance = MathF.Max(0f, s.FocusDistanceMetres);
        Vector3f sun = sunDirection.NormalizedOr(-Vector3f.UnitZ, 1e-6f);

        // Never darker than the veil. A drop refracts the light around it, so in an even light it is as
        // bright as what is behind it and all but disappears -- which is what a night lit only by its sky
        // does to rain, and the eye, adapted to that night, still sees it as a faint veil. The floor is
        // set in what the frame is exposed to (the caller divides a display level by the exposure), so it
        // is nothing by day, when the sky outshines it many times over, and a dim grey streak by night.
        (float lightR, float lightG, float lightB) = Ambient(s, Veiled(ambientColour, veil));

        float baseR = (s.Colour.R * lightR) + s.EmissiveIntensity;
        float baseG = (s.Colour.G * lightG) + s.EmissiveIntensity;
        float baseB = (s.Colour.B * lightB) + s.EmissiveIntensity;

        int quads = 0;
        for (int i = 0; i < _liveDrops; i++)
        {
            ref Drop drop = ref _drops[i];

            double px = drop.Previous.X + ((drop.Position.X - drop.Previous.X) * blend);
            double py = drop.Previous.Y + ((drop.Position.Y - drop.Previous.Y) * blend);
            double pz = drop.Previous.Z + ((drop.Position.Z - drop.Previous.Z) * blend);

            float lx = (float)(px - eye.X);
            float ly = (float)(py - eye.Y);
            float lz = (float)(pz - eye.Z);
            float distance = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));

            // Rejected by polarity, so a NaN distance fails the test rather than passing it.
            if (!(distance > cull))
            {
                continue;
            }

            if (s.ShelterHeightMetres > 0f && IsSheltered(px, py, pz, s.ShelterHeightMetres))
            {
                continue;
            }

            // A hard switch between the two strips, never a blend: a drop is either in the depth the lens
            // holds sharp or it is not, and a mix of the two strips is a streak with a ghost beside it.
            bool focused = !(focusDistance > 0f && distance < focusDistance);
            float v0 = focused ? 0f : _sheetSplitV;
            float v1 = focused ? _sheetSplitV : 1f;

            float alpha = alphaScale;

            // Gusts: the rain thins where the smooth field is low and keeps its full density where it is
            // high. The field travels at a slant through all three axes, so the gaps sweep across the yard
            // rather than marching along one of the world's axes.
            if (gusting)
            {
                float field = SmoothNoise(
                    ((float)px * gustScale) + (gustTravel * 0.74f),
                    ((float)py * gustScale) + (gustTravel * 0.52f),
                    ((float)pz * gustScale) - (gustTravel * 0.31f));
                float density = Math.Clamp((field - 0.2f) / 0.6f, 0f, 1f);
                alpha *= 1f - (gustAmount * (1f - density));
            }

            // Faded in from the lens: this column is centred on the eye, so without a ramp a defocused
            // streak the size of a hand snaps into existence a metre away.
            alpha *= Math.Clamp((distance - cull) / nearFade, 0f, 1f);

            if (!(alpha > 0.002f))
            {
                continue;
            }

            Vector3f velocity = drop.Velocity;
            Vector3f axis = velocity.NormalizedOr(-Vector3f.UnitZ, 1e-6f);
            float speed = MathF.Sqrt(
                (velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));

            // The streak is the distance the drop covers while the shutter is open, so it leans the way
            // the drop is actually travelling instead of hanging vertical in a crosswind. Divided by how
            // much of its cell the painted streak actually fills, so the tuning keeps meaning the streak
            // rather than the quad the streak is printed on.
            float length =
                (s.DropLengthMetres + (speed * s.MotionStretch / TicksPerSecond)) * drop.Scale / StreakLengthFill;
            float width =
                (s.DropWidthMetres + (distance * s.WidthBoostPerMetre)) * drop.Scale / FocusedStreakFill;

            float half = length * 0.5f;
            float halfWidth = width * 0.5f;

            float ex = -lx / distance, ey = -ly / distance, ez = -lz / distance;
            Vector3f side = (new Vector3f(
                (axis.Y * ez) - (axis.Z * ey),
                (axis.Z * ex) - (axis.X * ez),
                (axis.X * ey) - (axis.Y * ex))).NormalizedOr(-Vector3f.UnitZ, 1e-6f);

            float red = baseR * AmbientGlint, green = baseG * AmbientGlint, blue = baseB * AmbientGlint;

            // The lamps, which are what light rain at night: a drop falling past a fitting catches it and
            // the rain shows in a cone under every light. The same windowed inverse square and facing the
            // deferred lighting gives the lamps, so the drops are lit by what lights the ground beside
            // them. A drop is lit from every side, so a lamp reaches it as a sphere's average cosine rather
            // than a surface's n.l, and one seen against the lamp -- the lamp behind it -- glows through it
            // as the sun's backlighting does.
            for (int l = 0; l < lamps.Length; l++)
            {
                StormLamp lamp = lamps[l];
                float dx = lamp.Position.X - lx, dy = lamp.Position.Y - ly, dz = lamp.Position.Z - lz;
                float lampDistanceSq = (dx * dx) + (dy * dy) + (dz * dz);
                float reachSq = lamp.Reach * lamp.Reach;

                if (!(lampDistanceSq < reachSq))
                {
                    continue;
                }

                float ratio = lampDistanceSq / MathF.Max(reachSq, 1e-4f);
                float window = Math.Clamp(1f - (ratio * ratio), 0f, 1f);
                float attenuation = (window * window) / (lampDistanceSq + 1f);

                float inverse = 1f / MathF.Max(MathF.Sqrt(lampDistanceSq), 1e-4f);
                float tx0 = dx * inverse, ty0 = dy * inverse, tz0 = dz * inverse;

                if ((lamp.Facing.X * lamp.Facing.X) + (lamp.Facing.Y * lamp.Facing.Y) + (lamp.Facing.Z * lamp.Facing.Z) > 0.25f)
                {
                    attenuation *= MathF.Max(-((lamp.Facing.X * tx0) + (lamp.Facing.Y * ty0) + (lamp.Facing.Z * tz0)), 0f);
                }

                if (!(attenuation > 0f))
                {
                    continue;
                }

                float lit = attenuation * SphereAverageCosine;
                red += s.Colour.R * lamp.Radiance.X * lit;
                green += s.Colour.G * lamp.Radiance.Y * lit;
                blue += s.Colour.B * lamp.Radiance.Z * lit;

                if (focused && s.Backlighting > 0f)
                {
                    float through = Math.Clamp(-((ex * tx0) + (ey * ty0) + (ez * tz0)), 0f, 1f);
                    float glow = through * through * s.Backlighting * attenuation;
                    red += s.BacklightTint.R * lamp.Radiance.X * glow;
                    green += s.BacklightTint.G * lamp.Radiance.Y * glow;
                    blue += s.BacklightTint.B * lamp.Radiance.Z * glow;
                }
            }

            // Backlighting, and only on the sharp drops -- the same depth switch that picks the strip, so
            // the near defocused ones take none of it. A drop is backlit when
            // the eye looks through it towards the sun, which is the dot of the eye-to-drop direction
            // with the direction towards the sun; the eye-to-drop vector is the negation of the
            // drop-to-eye one built just above.
            if (focused && s.Backlighting > 0f)
            {
                float backlit = Math.Clamp(-((ex * sun.X) + (ey * sun.Y) + (ez * sun.Z)), 0f, 1f);
                float glow = backlit * backlit * s.Backlighting;
                red += s.BacklightTint.R * sunColour.X * glow;
                green += s.BacklightTint.G * sunColour.Y * glow;
                blue += s.BacklightTint.B * sunColour.Z * glow;
            }

            // Water, not paint. What a drop adds is the light it bends into the eye, a little of what surrounds it,
            // so it rolls off softly instead of clipping: clipped, every drop in daylight became the same flat
            // white. A faint cool cast for the sky it mostly refracts; each drop a little brighter or dimmer than
            // the next, as their sizes and the light catching them differ.
            float shimmer = 0.8f + (0.4f * Hash01((drop.Seed * 97.13f) + drop.Cell));
            Bgra32 colour = new(
                (byte)(WaterLevel(red * WaterTint.R * shimmer) * 255f),
                (byte)(WaterLevel(green * WaterTint.G * shimmer) * 255f),
                (byte)(WaterLevel(blue * WaterTint.B * shimmer) * 255f),
                (byte)(MathF.Sqrt(Math.Clamp(alpha, 0f, 1f)) * 255f));

            float u0 = drop.Cell / (float)SheetCells;
            float u1 = (drop.Cell + 1) / (float)SheetCells;

            float tx = axis.X * half, ty = axis.Y * half, tz = axis.Z * half;
            float sx = side.X * halfWidth, sy = side.Y * halfWidth, sz = side.Z * halfWidth;

            // The axis points along the velocity, so the +axis end leads the fall. The painted streaks
            // carry their bead of light at the top of the image and smear downwards from it — that is
            // the head of the drop and its tail — so the top row of the sheet belongs on the leading
            // end, and v runs backwards along the quad to put it there.
            Vector3f trailingLeft = new(lx - tx - sx, ly - ty - sy, lz - tz - sz);
            Vector3f trailingRight = new(lx - tx + sx, ly - ty + sy, lz - tz + sz);
            Vector3f leadingRight = new(lx + tx + sx, ly + ty + sy, lz + tz + sz);
            Vector3f leadingLeft = new(lx + tx - sx, ly + ty - sy, lz + tz - sz);

            if (!IsFinite(trailingLeft) || !IsFinite(leadingRight))
            {
                continue;
            }

            // Facing the eye, as the far sheets already do. It pointed the other way, which the fully
            // emissive override happens to make invisible -- but a billboard whose normal faces away
            // from the camera is a trap set for whatever reads normals next.
            Vector3f normal = new(ex, ey, ez);
            int v = (firstQuad + quads) * 4;
            _vertices[v] = new Vertex3D(trailingLeft, normal, u0, v1, colour: colour);
            _vertices[v + 1] = new Vertex3D(trailingRight, normal, u1, v1, colour: colour);
            _vertices[v + 2] = new Vertex3D(leadingRight, normal, u1, v0, colour: colour);
            _vertices[v + 3] = new Vertex3D(leadingLeft, normal, u0, v0, colour: colour);
            quads++;
        }

        return quads;
    }

    private int BuildSplashes(Vector3d eye, int firstQuad, in Vector3f ambientColour)
    {
        PrecipitationSettings s = _settings;
        float alphaScale = Math.Clamp(s.SplashAlpha, 0f, 1f) * Math.Clamp(s.Intensity, 0f, 1f);
        float reach = MathF.Max(s.SplashReachMetres, 1f);

        // Splashes take the scene's own light on their base colour, and no glimmer: water thrown off a
        // pavement does not sparkle the way a falling drop does.
        (float lightR, float lightG, float lightB) = Ambient(s, ambientColour);
        float baseR = s.SplashColour.R * lightR;
        float baseG = s.SplashColour.G * lightG;
        float baseB = s.SplashColour.B * lightB;

        Bgra32 Tint(float alpha) => new(
            (byte)(Math.Clamp(baseR, 0f, 1f) * 255f),
            (byte)(Math.Clamp(baseG, 0f, 1f) * 255f),
            (byte)(Math.Clamp(baseB, 0f, 1f) * 255f),
            (byte)(Math.Clamp(alpha, 0f, 1f) * 255f));

        int quads = 0;
        for (int i = 0; i < _liveSplashes; i++)
        {
            ref Splash splash = ref _splashes[i];
            float life = Math.Clamp(splash.Age / MathF.Max(splash.Life, 1e-4f), 0f, 1f);

            float lx = (float)(splash.X - eye.X);
            float ly = (float)(splash.Y - eye.Y);
            float lz = (float)(splash.Z - eye.Z);
            float distance = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
            if (!(distance > 0.05f))
            {
                continue;
            }

            float alpha = alphaScale;

            // A thrown droplet is a couple of millimetres across and goes under a pixel long before the edge
            // of the splash disc, so it fades out over the disc rather than vanishing at its rim. A crown
            // keeps its strength: it is the part of the splash that still reads from across the yard.
            if (splash.Small)
            {
                alpha *= 1f - Math.Clamp(distance / reach, 0f, 1f);
            }

            // Held at full strength until the flipbook has most of its water in the air, then let go.
            // The animation dissipates on its own; fading it from the first frame throws away the frames
            // that were drawn to be seen.
            alpha *= 1f - Math.Clamp((life - 0.65f) / 0.35f, 0f, 1f);
            if (!(alpha > 0.004f))
            {
                continue;
            }

            // Eight frames over the life, and the last one has to be reachable: with a plain floor a
            // splash that reaches exactly the end of its life indexes past the strip, and clamping the
            // life first is what keeps that from wrapping to frame zero for one frame.
            // Four across, two down, counted in reading order, and the variant chooses which pair of
            // strips, because each of the three animations was unpacked from one colour channel of the
            // source into two strips of its own.
            int frame = Math.Clamp((int)(life * SplashFrames), 0, SplashFrames - 1);
            int variant = Math.Clamp(splash.Variant, 0, SplashVariants - 1);
            int column = splash.Small ? 0 : frame % SplashColumns;
            int row = splash.Small
                ? SplashBlobRow
                : (variant * SplashFrameRows) + (frame / SplashColumns);
            (float u0, float u1) = SplashColumnUv(column, splash.FlipU);
            float v0 = row / (float)SplashRows;
            float v1 = (row + 1) / (float)SplashRows;

            int v = (firstQuad + quads) * 4;

            if (splash.Small)
            {
                // A droplet in the air, so it faces the eye squarely rather than lying flat: it has left
                // the ground and is on its way back down, and a flat quad seen edge-on disappears at
                // exactly the moment the arc is most visible.
                float radius = splash.Radius;
                Bgra32 colour = Tint(alpha);
                Vector3f towards = new(-lx / distance, -ly / distance, -lz / distance);

                // Any axis will do for a round blob; the world's vertical crossed with the view gives a
                // stable one, and its degenerate case is a droplet directly overhead.
                Vector3f across = (new Vector3f(-towards.Y, towards.X, 0f)).NormalizedOr(-Vector3f.UnitZ, 1e-6f);
                Vector3f down = new(
                    (towards.Y * across.Z) - (towards.Z * across.Y),
                    (towards.Z * across.X) - (towards.X * across.Z),
                    (towards.X * across.Y) - (towards.Y * across.X));

                Vector3f a = new(
                    lx - (across.X * radius) - (down.X * radius),
                    ly - (across.Y * radius) - (down.Y * radius),
                    lz - (across.Z * radius) - (down.Z * radius));
                Vector3f b = new(
                    lx + (across.X * radius) - (down.X * radius),
                    ly + (across.Y * radius) - (down.Y * radius),
                    lz + (across.Z * radius) - (down.Z * radius));
                Vector3f c = new(
                    lx + (across.X * radius) + (down.X * radius),
                    ly + (across.Y * radius) + (down.Y * radius),
                    lz + (across.Z * radius) + (down.Z * radius));
                Vector3f d = new(
                    lx - (across.X * radius) + (down.X * radius),
                    ly - (across.Y * radius) + (down.Y * radius),
                    lz - (across.Z * radius) + (down.Z * radius));
                if (!IsFinite(a) || !IsFinite(c))
                {
                    continue;
                }

                _vertices[v] = new Vertex3D(a, towards, u0, v0, colour: colour);
                _vertices[v + 1] = new Vertex3D(b, towards, u1, v0, colour: colour);
                _vertices[v + 2] = new Vertex3D(c, towards, u1, v1, colour: colour);
                _vertices[v + 3] = new Vertex3D(d, towards, u0, v1, colour: colour);
                quads++;
                continue;
            }

            // The crown stands up out of the ground. Every frame in the strip is painted with the water
            // rising from the bottom edge of its cell, so the quad is anchored at its base and grows
            // upwards, turned to the eye about the vertical. Drawn flat on the floor, which is what these
            // were, a crown is seen from directly above at every angle and the animation reads as a stain
            // spreading rather than as water being thrown.
            float half = splash.Radius;
            float height = half * 2f;

            float flat = MathF.Max(MathF.Sqrt((lx * lx) + (ly * ly)), 1e-4f);
            float rx = -ly / flat;
            float ry = lx / flat;

            Bgra32 crown = Tint(alpha);
            Vector3f facing = new(-lx / distance, -ly / distance, 0f);

            Vector3f bottomLeft = new(lx - (rx * half), ly - (ry * half), lz);
            Vector3f bottomRight = new(lx + (rx * half), ly + (ry * half), lz);
            Vector3f topRight = new(lx + (rx * half), ly + (ry * half), lz + height);
            Vector3f topLeft = new(lx - (rx * half), ly - (ry * half), lz + height);
            if (!IsFinite(bottomLeft) || !IsFinite(topRight))
            {
                continue;
            }

            // v runs from the top of the cell at the top of the quad: the sheet is painted with the
            // ground at the bottom of each frame, and the image's first row is its top.
            _vertices[v] = new Vertex3D(bottomLeft, facing, u0, v1, colour: crown);
            _vertices[v + 1] = new Vertex3D(bottomRight, facing, u1, v1, colour: crown);
            _vertices[v + 2] = new Vertex3D(topRight, facing, u1, v0, colour: crown);
            _vertices[v + 3] = new Vertex3D(topLeft, facing, u0, v0, colour: crown);
            quads++;
        }

        return quads;
    }

    private int BuildCurtains(Vector3d eye, int firstQuad)
    {
        PrecipitationSettings s = _settings;
        if (!s.Curtains)
        {
            return 0;
        }

        float alphaScale = Math.Clamp(s.CurtainAlpha, 0f, 1f) * Math.Clamp(s.Intensity, 0f, 1f);
        if (!(alphaScale > 0.001f))
        {
            return 0;
        }

        int quads = 0;
        for (int i = 0; i < _liveCurtains; i++)
        {
            ref Curtain curtain = ref _curtains[i];
            float lx = (float)(curtain.X - eye.X);
            float ly = (float)(curtain.Y - eye.Y);
            float lz = (float)(curtain.Z - eye.Z);
            float distance = MathF.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
            if (!(distance > 1f))
            {
                continue;
            }

            // Faded in with distance, the opposite of the drops: these exist to fill the depth the
            // drops have stopped resolving at, so one close enough to read as a shape is wrong. Measured
            // against the radius they are actually scattered over, not a different one.
            float reach = CurtainReach(s);
            float near = MathF.Max(s.SpawnRadiusMetres, 1f);
            float alpha = alphaScale * Math.Clamp((distance - near) / MathF.Max(reach - near, 1f), 0f, 1f);

            // Weighted by where the sheet sits against the eye: level with it, as tuned; below it, up to
            // twice as strong, because haze lying over far ground is where far rain is seen; above it,
            // fading to nothing well before overhead, so the drizzle never paints the sky.
            float rise = lz / distance;
            alpha = MathF.Min(alpha * Math.Clamp(1f - (2.5f * rise), 0f, 2f), 1f);
            if (!(alpha > 0.002f))
            {
                continue;
            }

            float half = MathF.Max(s.CurtainSizeMetres, 1f) * 0.5f * (0.7f + (curtain.Seed * 0.6f));

            // Upright and turned to face the eye about the vertical: a sheet of falling drizzle hangs,
            // it does not tilt to look at the camera.
            float rx = -ly / MathF.Max(MathF.Sqrt((lx * lx) + (ly * ly)), 1e-4f);
            float ry = lx / MathF.Max(MathF.Sqrt((lx * lx) + (ly * ly)), 1e-4f);

            Bgra32 colour = new(
                (byte)(Math.Clamp(s.Colour.R + 0.25f, 0f, 1f) * 255f),
                (byte)(Math.Clamp(s.Colour.G + 0.25f, 0f, 1f) * 255f),
                (byte)(Math.Clamp(s.Colour.B + 0.28f, 0f, 1f) * 255f),
                (byte)(Math.Clamp(alpha, 0f, 1f) * 255f));
            Vector3f normal = new(-lx / distance, -ly / distance, 0f);

            Vector3f a = new(lx - (rx * half), ly - (ry * half), lz - half);
            Vector3f b = new(lx + (rx * half), ly + (ry * half), lz - half);
            Vector3f c = new(lx + (rx * half), ly + (ry * half), lz + half);
            Vector3f d = new(lx - (rx * half), ly - (ry * half), lz + half);
            if (!IsFinite(a) || !IsFinite(c))
            {
                continue;
            }

            int v = (firstQuad + quads) * 4;
            _vertices[v] = new Vertex3D(a, normal, 0f, 0f, colour: colour);
            _vertices[v + 1] = new Vertex3D(b, normal, 1f, 0f, colour: colour);
            _vertices[v + 2] = new Vertex3D(c, normal, 1f, 1f, colour: colour);
            _vertices[v + 3] = new Vertex3D(d, normal, 0f, 1f, colour: colour);
            quads++;
        }

        return quads;
    }

    private static float CurtainReach(PrecipitationSettings s) =>
        MathF.Max(s.SpawnRadiusMetres, 1f) * MathF.Max(s.CurtainReachScale, 1.1f);

    /// <summary>The height of what the rain lands on at a point, or the flat plane if nothing is known.</summary>
    private double SurfaceUnder(double x, double y, double fallback)
    {
        double sampled = _surface.At(x, y);
        return double.IsFinite(sampled) ? sampled : fallback;
    }

    /// <summary>Reports whether a roof stands between a point and the sky.</summary>
    /// <param name="x">The world X, in metres.</param>
    /// <param name="y">The world Y, in metres.</param>
    /// <param name="z">The world Z, in metres.</param>
    /// <param name="searchMetres">How far above the point to look for the roof.</param>
    /// <remarks>
    /// One read of the level's roof raster. What it replaced was the bounding box of every task named
    /// <c>Building_</c>, which said the ground was sheltered wherever one of those boxes reached -- and
    /// a building standing at an angle to the world axes fills barely a third of its own box, so the
    /// rain stopped over the open apron in front of it while going on falling through the roof of
    /// anything the level had not labelled a building.
    /// </remarks>
    private bool IsSheltered(double x, double y, double z, float searchMetres) =>
        _shelter.IsSheltered(x, y, z, ShelterHeightField.SurfaceHeadroomMetres, searchMetres);

    private static bool IsFinite(in Vector3f value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

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

        (Bgra32[] pixels, int width, int height, float splitV) = BuildDropSheet(_logger);
        _sheetSplitV = splitV;

        _dropSprite = UploadSheet(pixels, width, height, width / SheetCells);
        return _dropSprite;
    }

    /// <summary>Stacks the two streak strips into the one sheet the drops are drawn from.</summary>
    /// <remarks>
    /// <para>
    /// The two images do not share a width — the defocused streaks are painted at twice the resolution,
    /// because a blurred shape needs the room a sharp one does not — so the narrower strip is expanded
    /// onto the wider one's grid by whole texels. Nearest, deliberately: an integer expansion of a
    /// sharp image is exact, and it is the render's own bilinear sampling that softens it back, whereas
    /// filtering here would blur the sharp strip twice.
    /// </para>
    /// <para>
    /// <b>Nothing bleeds across the seam.</b> Both strips are painted with black margins at top and
    /// bottom — the streaks stop about a twentieth of the image short of each edge — so the rows either
    /// side of the join are empty in both, and a bilinear tap that straddles it picks up nothing. The
    /// mip chain would not cross it either — both strips are the same height, so the join sits at the
    /// halfway row of every level and no box in a halving chain straddles the middle of a power-of-two
    /// axis — but there is no chain, and that was measured rather than assumed.
    /// </para>
    /// <para>
    /// <b>Why the sheet is not mipped, having been.</b> A streak is painted ten texels wide inside a
    /// fifty-texel cell and drawn across a handful of screen pixels, which is exactly the case mips
    /// exist for, and building one all but emptied the sky: a box filter spreads a thin bright line
    /// into its black surround and drops its peak in proportion, the vertex alpha then multiplies what
    /// is left, and the rain past twenty metres stopped being drawn at all. That is the physically
    /// honest answer to a sub-pixel streak and it is the wrong picture — the boost in
    /// <see cref="PrecipitationSettings.WidthBoostPerMetre"/> exists precisely to stop distant rain
    /// falling under a pixel. Level zero it is, at the cost of some sparkle at range, which the
    /// temporal filter takes most of. If this is revisited, it needs a chain that preserves the
    /// streak's peak rather than its integral, not a plain box.
    /// </para>
    /// </remarks>
    /// <param name="logger">Told about a sheet that could not be read; the fallback is drawn instead.</param>
    /// <returns>The sheet, and the v the defocused strip starts at.</returns>
    internal static (Bgra32[] Pixels, int Width, int Height, float SplitV) BuildDropSheet(IOpenIgiLogger? logger = null)
    {
        if (TryLoadMask(FocusedSheetAsset, logger, out Bgra32[] focused, out int focusedWidth, out int focusedHeight)
            && TryLoadMask(DefocusedSheetAsset, logger, out Bgra32[] soft, out int softWidth, out int softHeight))
        {
            int width = Math.Max(focusedWidth, softWidth);
            int height = focusedHeight + softHeight;
            Bgra32[] sheet = new Bgra32[width * height];
            Stretch(focused, focusedWidth, focusedHeight, sheet, width, 0);
            Stretch(soft, softWidth, softHeight, sheet, width, focusedHeight);
            return (sheet, width, height, focusedHeight / (float)height);
        }

        return BuildFallbackSheet();
    }

    /// <summary>Reads a greyscale mask and turns it into a white sprite whose alpha is the shape.</summary>
    private static bool TryLoadMask(
        string asset,
        IOpenIgiLogger? logger,
        out Bgra32[] pixels,
        out int width,
        out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "weather", asset);

        try
        {
            (Bgra32[] read, int w, int h) = PngReader.Read(path);
            if (w <= 0 || h <= 0 || read.Length < w * h)
            {
                return false;
            }

            for (int i = 0; i < read.Length; i++)
            {
                // The mask is painted in luminance, and the blue channel is as good a read of a grey
                // pixel as any. White times the vertex tint is what the fragment then multiplies.
                read[i] = new Bgra32(255, 255, 255, read[i].B);
            }

            pixels = read;
            width = w;
            height = h;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger?.Warning(
                LogCategory,
                FormattableString.Invariant($"Storm sheet '{asset}' could not be read ({ex.Message}); drawing generated streaks."));
            return false;
        }
    }

    /// <summary>Copies one strip into the sheet, expanding it by whole texels to the sheet's width.</summary>
    private static void Stretch(
        ReadOnlySpan<Bgra32> source,
        int sourceWidth,
        int sourceHeight,
        Span<Bgra32> destination,
        int destinationWidth,
        int destinationTop)
    {
        for (int y = 0; y < sourceHeight; y++)
        {
            int sourceRow = y * sourceWidth;
            int destinationRow = (destinationTop + y) * destinationWidth;
            for (int x = 0; x < destinationWidth; x++)
            {
                int sx = Math.Min(x * sourceWidth / destinationWidth, sourceWidth - 1);
                destination[destinationRow + x] = source[sourceRow + sx];
            }
        }
    }

    /// <summary>Generated streaks, for when the sheets are missing.</summary>
    /// <remarks>
    /// <para>
    /// Kept because the alternative failure is silent and expensive: an asset that did not reach the output
    /// directory would otherwise mean no rain at all, which reads as a bug in the storm rather than as a
    /// missing file.
    /// </para>
    /// <para>
    /// Drawn as what a falling drop looks like through a shutter, not as a line of paint. A drop is a lens: it
    /// is almost clear through the middle, and bright only along its two edges, where it bends the sky round
    /// into the eye and reflects it at a grazing angle -- so a streak is a thin tube with two bright rims and a
    /// faint body. The drop itself sits at the leading end as a small bead; behind it the smear thins out along
    /// the path it swept. Out of focus, near the lens, a drop spreads into a faint ring, which the defocused
    /// strip carries as a soft, hollow streak. Every cell varies the tube's weight and its rims, so one storm is
    /// not one repeated sprite.
    /// </para>
    /// </remarks>
    private static (Bgra32[] Pixels, int Width, int Height, float SplitV) BuildFallbackSheet()
    {
        const int Cell = 32;
        const int Strip = 256;

        int width = Cell * SheetCells;
        int height = Strip * 2;
        Bgra32[] pixels = new Bgra32[width * height];

        for (int cell = 0; cell < SheetCells; cell++)
        {
            // Per cell: how wide the tube is in its cell, where its rims sit, and how bright they are.
            float variation = cell / (float)Math.Max(SheetCells - 1, 1);
            float halfWidth = 0.30f + (0.14f * variation);
            float rimAt = 0.62f + (0.18f * ((cell * 7) % SheetCells) / SheetCells);
            float rimLevel = 0.75f + (0.25f * ((cell * 3) % SheetCells) / SheetCells);

            for (int strip = 0; strip < 2; strip++)
            {
                bool defocused = strip == 1;
                for (int y = 0; y < Strip; y++)
                {
                    // v runs from the leading end (-1, the top row) to the tail (+1).
                    float v = (((y + 0.5f) / Strip) * 2f) - 1f;
                    float along01 = (v + 1f) * 0.5f;

                    // Rounded ends, and a smear that thins out behind the drop.
                    float ends = MathF.Sqrt(Math.Clamp(1f - (v * v * v * v), 0f, 1f));
                    float trail = defocused ? 1f : 0.35f + (0.65f * MathF.Pow(1f - along01, 0.7f));

                    // The drop itself, a bead at the leading end of the sharp streak.
                    float beadOffset = (v + 0.82f) / 0.07f;
                    float bead = defocused ? 0f : 0.55f * MathF.Exp(-0.5f * beadOffset * beadOffset);

                    for (int x = 0; x < Cell; x++)
                    {
                        float u = ((x + 0.5f) / Cell * 2f) - 1f;
                        float r = MathF.Abs(u) / (defocused ? 0.95f : halfWidth);

                        float value;
                        if (defocused)
                        {
                            // A soft ring: bright towards its edge, all but clear inside.
                            float ring = (r - 0.78f) / 0.16f;
                            value = r > 1.15f ? 0f : (0.30f * MathF.Exp(-0.5f * ring * ring)) + (0.04f * (1f - Math.Min(r, 1f)));
                        }
                        else
                        {
                            float rim = (r - rimAt) / 0.14f;
                            float rims = rimLevel * MathF.Exp(-0.5f * rim * rim);
                            float body = r < 1f ? 0.14f * (1f - (r * r)) : 0f;
                            value = r > 1.2f ? 0f : rims + body;
                            value += bead * MathF.Exp(-2.5f * r * r);
                        }

                        value *= ends * trail;
                        byte alpha = (byte)(Math.Clamp(value, 0f, 1f) * 255f);
                        pixels[(((strip * Strip) + y) * width) + (cell * Cell) + x] =
                            new Bgra32(255, 255, 255, alpha);
                    }
                }
            }
        }

        return (pixels, width, height, 0.5f);
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

        (Bgra32[] pixels, int width, int height) = BuildSplashSheet(_logger);
        _splashSprite = UploadSheet(pixels, width, height, width / 4);
        return _splashSprite;
    }

    /// <summary>Uploads a sheet with as much of a mip chain as its cells can take.</summary>
    /// <param name="pixels">Level zero.</param>
    /// <param name="width">Its width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="cellWidth">The narrowest cell across the sheet, in texels.</param>
    /// <returns>The texture.</returns>
    /// <remarks>
    /// A streak is a few texels of a painted line, and drawn a few pixels across without a chain it is sampled
    /// at scattered texels -- which broke every drop into a row of hard white dashes, rain as a dotted line of
    /// paint rather than water. The chain lets a small drop read as the soft, faint thing it is. It stops while a
    /// cell is still four texels wide, so the averaging never mixes one cell's streak into its neighbour's.
    /// </remarks>
    private TextureHandle UploadSheet(Bgra32[] pixels, int width, int height, int cellWidth)
    {
        int levels = 1;
        for (int cell = cellWidth; cell >= 8 && levels < LinearMipChain.LevelCount(width, height); cell >>= 1)
        {
            levels++;
        }

        if (levels == 1)
        {
            return _device.CreateTexture(SheetTexture(width, height), pixels);
        }

        (Bgra32[] chain, _) = LinearMipChain.Build(pixels, width, height);
        int texels = 0;
        for (int level = 0; level < levels; level++)
        {
            texels += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        return _device.CreateTexture(
            SheetTexture(width, height) with { MipLevels = levels },
            chain.AsSpan(0, texels));
    }

    /// <summary>How every one of the storm's sprite sheets is described to the device.</summary>
    /// <param name="width">The sheet's width in texels.</param>
    /// <param name="height">Its height in texels.</param>
    /// <returns>The description.</returns>
    /// <remarks>
    /// <para>
    /// <b>One mip level, and asking for none is not a way to ask for a chain.</b>
    /// <see cref="TextureDescription"/> counts levels and requires at least one; zero makes level zero
    /// itself out of range, and the upload throws an <see cref="ArgumentOutOfRangeException"/> before a
    /// texel is written. There is no mip generation behind this interface — a chain is uploaded level by
    /// level or it does not exist — so a sheet that wants one has to build it, not request it.
    /// </para>
    /// <para>
    /// Asking for zero here took the WHOLE storm off the screen, not just the sheet that asked: the
    /// sprites are resolved together at the top of the draw, inside one try, and the splash sheet is
    /// resolved before the drops. One bad description and the curtains, the splashes and the rain all
    /// stopped being drawn at once.
    /// </para>
    /// </remarks>
    internal static TextureDescription SheetTexture(int width, int height) =>
        new(width, height, MipLevels: 1, TextureFilter.Linear, TextureAddressMode.Clamp);

    /// <summary>Where one splash cell sits across the sheet, mirrored when asked.</summary>
    /// <param name="column">The cell's column.</param>
    /// <param name="flip">Whether to read the cell right to left.</param>
    /// <returns>The left and right texture coordinates, swapped when flipped.</returns>
    /// <remarks>
    /// Every splash has an even chance of being mirrored. Three animations over eight frames is not
    /// many, and without the mirror a heavy shower is visibly the same handful of stamps repeating.
    /// </remarks>
    internal static (float U0, float U1) SplashColumnUv(int column, bool flip)
    {
        float left = column / (float)SplashColumns;
        float right = (column + 1) / (float)SplashColumns;
        return flip ? (right, left) : (left, right);
    }

    /// <summary>Unpacks the three channel-packed splash animations and adds the small splashes' blob.</summary>
    /// <param name="logger">Told about a sheet that could not be read; the stand-in is drawn instead.</param>
    /// <returns>The sheet, four cells across by <see cref="SplashRows"/> down.</returns>
    internal static (Bgra32[] Pixels, int Width, int Height) BuildSplashSheet(IOpenIgiLogger? logger = null)
    {
        if (TryLoadSheetPixels(SplashSheetAsset, logger, out Bgra32[] source, out int sourceWidth, out int sourceHeight)
            && sourceWidth % SplashColumns == 0
            && sourceHeight % SplashFrameRows == 0)
        {
            int cell = sourceHeight / SplashFrameRows;
            int width = sourceWidth;
            int height = cell * SplashRows;
            Bgra32[] sheet = new Bgra32[width * height];

            for (int variant = 0; variant < SplashVariants; variant++)
            {
                int top = variant * SplashFrameRows * cell;
                for (int y = 0; y < sourceHeight; y++)
                {
                    for (int x = 0; x < sourceWidth; x++)
                    {
                        Bgra32 texel = source[(y * sourceWidth) + x];
                        byte mask = variant switch
                        {
                            0 => texel.R,
                            1 => texel.G,
                            _ => texel.B,
                        };

                        // White with the mask in alpha, which is the shape every sheet here carries.
                        sheet[(((top + y) * width) + x)] = new Bgra32(255, 255, 255, mask);
                    }
                }
            }

            FillBlobStrip(sheet, width, cell, logger);
            return (sheet, width, height);
        }

        // No image: the stand-in, drawn at the same layout so every index still addresses what it means.
        return LoadSheet(
            string.Empty,
            logger,
            128,
            SplashColumns,
            SplashRows,
            static (u, v, column, row) =>
            {
                if (row >= SplashBlobRow)
                {
                    float square = MathF.Max(MathF.Abs(u), MathF.Abs(v));
                    return Math.Clamp(1f - square, 0f, 1f) * Math.Clamp(1f - square, 0f, 1f);
                }

                int frame = ((row % SplashFrameRows) * SplashColumns) + column;
                float life = (frame + 0.5f) / SplashFrames;
                float radius = MathF.Sqrt((u * u) + ((v + 1f) * (v + 1f)));
                float ring = Math.Clamp(1f - (MathF.Abs(radius - (0.3f + (life * 1.1f))) / 0.22f), 0f, 1f);
                return ring * ring * (1f - life) * Math.Clamp(-v, 0f, 1f);
            });
    }

    /// <summary>Writes the small splashes' round blob across the sheet's last strip.</summary>
    /// <remarks>
    /// Repeated across all four columns so that the blob strip is addressed by the same column
    /// arithmetic as every other, and a droplet that picks a column other than zero still lands on it.
    /// </remarks>
    private static void FillBlobStrip(Bgra32[] sheet, int width, int cell, IOpenIgiLogger? logger)
    {
        int top = SplashBlobRow * cell;

        if (!TryLoadMask(DropletSheetAsset, logger, out Bgra32[] blob, out int blobWidth, out int blobHeight)
            || blobWidth <= 0
            || blobHeight <= 0)
        {
            // A soft round blob, standing in when the image is missing.
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = ((((x % cell) + 0.5f) / cell) * 2f) - 1f;
                    float v = (((y + 0.5f) / cell) * 2f) - 1f;
                    float fade = Math.Clamp(1f - MathF.Sqrt((u * u) + (v * v)), 0f, 1f);
                    sheet[(((top + y) * width) + x)] = new Bgra32(255, 255, 255, (byte)(fade * fade * 255f));
                }
            }

            return;
        }

        for (int y = 0; y < cell; y++)
        {
            int sourceY = Math.Min(blobHeight - 1, y * blobHeight / cell);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(blobWidth - 1, (x % cell) * blobWidth / cell);
                sheet[(((top + y) * width) + x)] = blob[(sourceY * blobWidth) + sourceX];
            }
        }
    }

    /// <summary>Reads a sheet's pixels as they are, colour channels intact.</summary>
    /// <remarks>
    /// <see cref="TryLoadMask"/> whitens the colour and moves blue into alpha, which is right for every
    /// sheet whose shape is one greyscale mask and wrong for the splash sheet, whose three channels are
    /// three different animations.
    /// </remarks>
    private static bool TryLoadSheetPixels(
        string asset,
        IOpenIgiLogger? logger,
        out Bgra32[] pixels,
        out int width,
        out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "weather", asset);

        try
        {
            (Bgra32[] read, int w, int h) = PngReader.Read(path);
            if (w <= 0 || h <= 0 || read.Length < w * h)
            {
                return false;
            }

            pixels = read;
            width = w;
            height = h;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger?.Warning(
                LogCategory,
                FormattableString.Invariant($"Storm sheet '{asset}' could not be read ({error.Message}); drawing generated splashes."));
            return false;
        }
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

        (Bgra32[] pixels, int width, int height) = LoadSheet(
            CurtainSheetAsset,
            _logger,
            256,
            1,
            1,
            static (u, v, _, _) =>
            {
                float radius = MathF.Sqrt((u * u) + (v * v));
                float body = Math.Clamp(1f - radius, 0f, 1f);
                return body * body * body * (0.75f + (0.25f * MathF.Sin((u * 19f) + (v * 3f))));
            });

        _curtainSprite = UploadSheet(pixels, width, height, width);
        return _curtainSprite;
    }

    /// <summary>Reads a mask sheet, or draws the given stand-in at the given size when it is missing.</summary>
    /// <param name="asset">The file, under the executable's weather assets.</param>
    /// <param name="logger">Told when the file could not be read.</param>
    /// <param name="cell">The stand-in's cell edge in texels, used only when the file is missing.</param>
    /// <param name="columns">How many cells across the stand-in is.</param>
    /// <param name="rows">How many cells down it is.</param>
    /// <param name="shape">
    /// The stand-in's alpha, given the position within the current cell from -1 to 1 on both axes and
    /// the cell's column and row.
    /// </param>
    private static (Bgra32[] Pixels, int Width, int Height) LoadSheet(
        string asset,
        IOpenIgiLogger? logger,
        int cell,
        int columns,
        int rows,
        Func<float, float, int, int, float> shape)
    {
        // An empty name is a caller that has already decided to draw the stand-in.
        if (asset.Length > 0
            && TryLoadMask(asset, logger, out Bgra32[] loaded, out int loadedWidth, out int loadedHeight))
        {
            return (loaded, loadedWidth, loadedHeight);
        }

        int width = cell * columns;
        int height = cell * rows;
        Bgra32[] pixels = new Bgra32[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = y / cell;
            float v = ((((y - (row * cell)) + 0.5f) / cell) * 2f) - 1f;
            for (int x = 0; x < width; x++)
            {
                int column = x / cell;
                float u = ((((x - (column * cell)) + 0.5f) / cell) * 2f) - 1f;
                byte alpha = (byte)(Math.Clamp(shape(u, v, column, row), 0f, 1f) * 255f);
                pixels[(y * width) + x] = new Bgra32(255, 255, 255, alpha);
            }
        }

        return (pixels, width, height);
    }

    /// <summary>The height of whatever the rain would land on, sampled on a grid that follows the eye.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Rain should stop where it meets the world, so that it lands on roofs,
    /// steps, crates and the tops of walls. This port had one flat plane taken from the player's feet,
    /// so rain fell through every raised surface and every splash sat at one height. On a level of
    /// stacked concrete that is not a subtle difference: the ground floor rains and nothing else does.
    /// </para>
    /// <para>
    /// <b>Why a cache and not a ray per drop.</b> A ray per drop per tick is thousands of traces a
    /// second through the object index, which is the reason the plane was there in the first place. But
    /// the answer a drop needs — how high is the world under this square metre — barely changes, and it
    /// is the same answer for every drop above that square. So the grid is sampled a few cells a tick,
    /// round-robin, and every drop reads it for free. A full sweep of the grid costs about as much as
    /// one drop's worth of tracing did, and it takes under a second, which is far quicker than the
    /// player can walk out of it.
    /// </para>
    /// <para>
    /// <b>It follows the eye in whole cells.</b> Re-centring on a fractional position would shift every
    /// cached height sideways under the drops; snapping to the lattice means a step only ever scrolls
    /// whole cells in and out, and what scrolls in is marked unknown rather than being wrong.
    /// </para>
    /// </remarks>
    private sealed class SurfaceHeights
    {
        /// <summary>Cells across the grid; the reach is this times <see cref="CellMetres"/>, halved.</summary>
        public const int Cells = 48;

        /// <summary>How much ground one cell stands for, in metres.</summary>
        public const float CellMetres = 1.25f;

        /// <summary>How many cells are re-traced each tick.</summary>
        /// <remarks>
        /// The whole grid over about twenty ticks. Higher is fresher and costs linearly; the number that
        /// matters is that a surface which appears -- a door opening, a lift arriving -- is rained on
        /// within a second, and nobody notices a second.
        /// </remarks>
        public const int CellsPerTick = 128;

        private readonly double[] _heights = new double[Cells * Cells];
        private int _originX;
        private int _originY;
        private int _cursor;
        private bool _centred;

        /// <summary>Gets the height under a point, or negative infinity where nothing is known yet.</summary>
        /// <param name="x">World X in metres.</param>
        /// <param name="y">World Y in metres.</param>
        /// <returns>The world height in metres, or negative infinity.</returns>
        public double At(double x, double y)
        {
            int cellX = (int)Math.Floor(x / CellMetres) - _originX;
            int cellY = (int)Math.Floor(y / CellMetres) - _originY;
            if (!_centred || cellX < 0 || cellY < 0 || cellX >= Cells || cellY >= Cells)
            {
                return double.NegativeInfinity;
            }

            return _heights[(cellY * Cells) + cellX];
        }

        /// <summary>Re-centres on the eye and re-traces this tick's share of the grid.</summary>
        /// <param name="eyeMetres">Where the eye is now, in world metres.</param>
        /// <param name="cast">The world trace, in world units.</param>
        /// <param name="unitsPerMetre">How many world units make a metre.</param>
        /// <param name="fallbackMetres">The height to assume where the trace finds nothing.</param>
        public void Tick(Vector3d eyeMetres, WorldRayCast cast, double unitsPerMetre, double fallbackMetres)
        {
            int wantedX = (int)Math.Floor(eyeMetres.X / CellMetres) - (Cells / 2);
            int wantedY = (int)Math.Floor(eyeMetres.Y / CellMetres) - (Cells / 2);

            if (!_centred || wantedX != _originX || wantedY != _originY)
            {
                // Anything the move brought in is unknown until it is traced; anything that stayed keeps
                // its height, because the world under it did not move.
                Shift(wantedX - _originX, wantedY - _originY, _centred);
                _originX = wantedX;
                _originY = wantedY;
                _centred = true;
            }

            // From well above the eye to well below it. The span has to clear the tallest thing rain can
            // land on from where the player stands and still reach a floor a storey down.
            double from = (eyeMetres.Z + 40.0) * unitsPerMetre;
            double to = (eyeMetres.Z - 60.0) * unitsPerMetre;

            for (int n = 0; n < CellsPerTick; n++)
            {
                int index = _cursor;
                _cursor = (_cursor + 1) % (Cells * Cells);

                double x = (_originX + (index % Cells) + 0.5) * CellMetres * unitsPerMetre;
                double y = (_originY + (index / Cells) + 0.5) * CellMetres * unitsPerMetre;

                _heights[index] = cast(new Vector3d(x, y, from), new Vector3d(x, y, to), out WorldRayHit hit)
                    ? hit.Position.Z / unitsPerMetre
                    : fallbackMetres;
            }
        }

        /// <summary>Forgets everything, as changing level does.</summary>
        public void Clear()
        {
            _centred = false;
            _cursor = 0;
        }

        private void Shift(int byX, int byY, bool knew)
        {
            if (!knew || Math.Abs(byX) >= Cells || Math.Abs(byY) >= Cells)
            {
                Array.Fill(_heights, double.NegativeInfinity);
                return;
            }

            // Copied in the direction that does not overwrite a cell before it has been read.
            int stepY = byY > 0 ? 1 : -1;
            int stepX = byX > 0 ? 1 : -1;
            int firstY = byY > 0 ? 0 : Cells - 1;
            int firstX = byX > 0 ? 0 : Cells - 1;

            for (int i = 0; i < Cells; i++)
            {
                int destY = firstY + (i * stepY);
                int sourceY = destY + byY;
                for (int j = 0; j < Cells; j++)
                {
                    int destX = firstX + (j * stepX);
                    int sourceX = destX + byX;
                    _heights[(destY * Cells) + destX] =
                        sourceX >= 0 && sourceX < Cells && sourceY >= 0 && sourceY < Cells
                            ? _heights[(sourceY * Cells) + sourceX]
                            : double.NegativeInfinity;
                }
            }
        }
    }

    private struct Drop
    {
        public Vector3d Position;
        public Vector3d Previous;
        public Vector3f Velocity;
        public float WindX;
        public float WindY;

        /// <summary>The drop's own sideways acceleration, integrated over its age. See SpawnDrift.</summary>
        public float DriftX;
        public float DriftY;
        public float DriftZ;
        public float Gravity;
        public float Terminal;
        public float Life;
        public float Age;
        public float Seed;
        public float Scale;
        public int Cell;
    }

    private struct Splash
    {
        public double X;
        public double Y;
        public double Z;
        public Vector3f Velocity;
        public float Age;
        public float Life;
        public float Radius;
        public int Variant;
        public bool Small;

        /// <summary>Whether this splash's cell is read right to left. See SplashColumnUv.</summary>
        public bool FlipU;
    }

    private struct Curtain
    {
        public double X;
        public double Y;
        public double Z;
        public float Seed;
    }
}
