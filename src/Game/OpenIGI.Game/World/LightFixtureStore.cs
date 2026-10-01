using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Reads and writes a mission's proposed <see cref="LightFixture"/> list beside its other authored data.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are an inference -- see <see cref="LightFixture"/> and <see cref="LightFixtureExtractor"/>
/// -- and the whole point of writing them down is that the inference can be looked at and corrected. A
/// level that lights a reflective sign as though it were a lamp is fixed by opening this file and deleting
/// a line, not by editing the extractor. So the file wins over the survey whenever it exists, exactly the
/// way authored material detail wins over the detail derived from a texture: the survey runs once to
/// propose the list, and from then on the file is the truth.
/// </para>
/// <para>
/// Writing the file also takes the emissive survey's lightmap decode off the load path. The survey exists
/// to propose the list the first time; once the list is written, a load reads it and never touches a
/// lightmap for this.
/// </para>
/// </remarks>
public static partial class LightFixtureStore
{
    private const string LogCategory = "Emissive";

    /// <summary>What this file's shape and the detection behind it are worth.</summary>
    /// <remarks>
    /// <para>
    /// A file is a CACHE of an inference as much as it is a place to correct one, and the two pull in
    /// opposite directions: an edited file must survive, and a file written by a detector that has since
    /// been improved must not. The version settles it. Raise it whenever the detection changes in a way
    /// that would give a level different lamps, and every stale file is re-surveyed once and rewritten;
    /// leave it alone for a change that would not, and every edit anyone has made is kept.
    /// </para>
    /// <para>
    /// 6: the hand-written manifest covers the whole game rather than mission 1 alone, so every level
    /// gets the fittings and the lit panels only mission 1 had been given. 5: nothing is inferred to be
    /// a lamp unless it is skinned with one of the texture pages the level's own catalogued fittings
    /// draw from, which drops 263 of the 423 sub-meshes the shape test had been proposing across the
    /// fourteen levels — every soldier, chair, camera and bracket among them. 4 gave each lamp sub-mesh
    /// its own emissive strength, so one texture can be a floodlight on one model and a reading lamp on
    /// another. 3 moved emission from the texture to the sub-mesh, 2 added the direction a fixture
    /// faces, and version 1 was a bare array — which is what an absent version reads as. 7 added the
    /// emitting rectangle (long axis and half-extents) the lamp's shadow rays are aimed at.
    /// </para>
    /// </remarks>
    public const int Version = 7;

    /// <summary>Writes a mission's fixtures and emitters to a file, creating the directory if needed.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="fixtures">The fixtures to write.</param>
    /// <param name="lamps">
    /// Which textures glow, how strongly, and which sub-meshes they glow on. Stored beside the fixtures
    /// because the two are one conclusion: the lamps a level has, and the geometry that IS those lamps. A
    /// file that held only the lights would light a room from panels that stayed grey.
    /// </param>
    /// <param name="logger">Where to report the result.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void Save(
        string path,
        IReadOnlyList<LightFixture> fixtures,
        LampDetector.Lamps lamps = default,
        IIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(fixtures);

        IIgiLogger log = logger ?? NullLogger.Instance;

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Record[] records = new Record[fixtures.Count];

            for (int i = 0; i < fixtures.Count; i++)
            {
                records[i] = Record.From(fixtures[i]);
            }

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    new Document
                    {
                        Version = Version,
                        Textures = lamps.Textures is null ? [] : [.. lamps.Textures],
                        SubMeshes = lamps.SubMeshes is null
                            ? []
                            : new Dictionary<string, float>(lamps.SubMeshes, StringComparer.OrdinalIgnoreCase),
                        Fixtures = records,
                    },
                    LightFixtureJsonContext.Default.Document));
            log.Information(LogCategory, $"Wrote {fixtures.Count} light fixture(s) to {path}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            log.Warning(LogCategory, $"The light fixtures could not be written to {path}: {error.Message}");
        }
    }

    /// <summary>Reads a mission's fixtures back, if the file is present and well formed.</summary>
    /// <param name="path">Where to read from.</param>
    /// <param name="fixtures">The fixtures read, or empty when the file is absent or unreadable.</param>
    /// <param name="lamps">The lamps read, or none on the same terms.</param>
    /// <param name="logger">Where to report the result.</param>
    /// <returns><see langword="true"/> when a file was read; <see langword="false"/> when none exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static bool TryLoad(
        string path,
        out IReadOnlyList<LightFixture> fixtures,
        out LampDetector.Lamps lamps,
        IIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        IIgiLogger log = logger ?? NullLogger.Instance;
        fixtures = [];
        lamps = LampDetector.Lamps.None;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            Document? document = JsonSerializer.Deserialize(
                File.ReadAllText(path), LightFixtureJsonContext.Default.Document);

            if (document?.Fixtures is not { } records)
            {
                return false;
            }

            if (document.Version != Version)
            {
                // Written by an older detector, or by the bare-array format that predates versioning at
                // all. Refused rather than read, so the caller surveys again and overwrites it: reading
                // it would pin the level to lamps that a since-corrected detection would not propose.
                log.Information(
                    LogCategory,
                    $"The light fixtures at {path} are version {document.Version}, not {Version}; "
                        + "they will be proposed again.");
                return false;
            }

            LightFixture[] read = new LightFixture[records.Length];

            for (int i = 0; i < records.Length; i++)
            {
                read[i] = records[i].ToFixture();
            }

            fixtures = read;
            lamps = new LampDetector.Lamps(
                new HashSet<string>(document.Textures ?? [], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, float>(document.SubMeshes ?? [], StringComparer.OrdinalIgnoreCase));

            log.Information(
                LogCategory,
                $"Read {read.Length} light fixture(s) and {lamps.Count} lamp sub-mesh(es) from {path}.");
            return true;
        }
        catch (Exception error) when (error is IOException or JsonException or NotSupportedException)
        {
            // A corrupt file is not fatal: the survey can propose the list again. Reported rather than
            // swallowed, because a file that will not parse is a thing the author wants to know about.
            log.Warning(LogCategory, $"The light fixtures at {path} could not be read: {error.Message}");
            return false;
        }
    }

    /// <summary>The file: what wrote it, and what it holds.</summary>
    private sealed record Document
    {
        public int Version { get; init; }

        public string[]? Textures { get; init; }

        public Dictionary<string, float>? SubMeshes { get; init; }

        public Record[]? Fixtures { get; init; }
    }

    /// <summary>The on-disk shape of a fixture. A flat record so the file reads plainly and diffs well.</summary>
    private sealed record Record
    {
        public string Texture { get; init; } = string.Empty;

        public double PositionX { get; init; }

        public double PositionY { get; init; }

        public double PositionZ { get; init; }

        public float Radius { get; init; }

        public float ColourR { get; init; }

        public float ColourG { get; init; }

        public float ColourB { get; init; }

        public float Area { get; init; }

        public float Extent { get; init; }

        public int TriangleCount { get; init; }

        public float DirectionX { get; init; }

        public float DirectionY { get; init; }

        public float DirectionZ { get; init; }

        public float AxisX { get; init; }

        public float AxisY { get; init; }

        public float AxisZ { get; init; }

        public float HalfLength { get; init; }

        public float HalfWidth { get; init; }

        public static Record From(LightFixture fixture) => new()
        {
            Texture = fixture.Texture,
            PositionX = fixture.Position.X,
            PositionY = fixture.Position.Y,
            PositionZ = fixture.Position.Z,
            Radius = fixture.Radius,
            ColourR = fixture.Colour.X,
            ColourG = fixture.Colour.Y,
            ColourB = fixture.Colour.Z,
            Area = fixture.Area,
            Extent = fixture.Extent,
            TriangleCount = fixture.TriangleCount,
            DirectionX = fixture.Direction.X,
            DirectionY = fixture.Direction.Y,
            DirectionZ = fixture.Direction.Z,
            AxisX = fixture.Axis.X,
            AxisY = fixture.Axis.Y,
            AxisZ = fixture.Axis.Z,
            HalfLength = fixture.HalfLength,
            HalfWidth = fixture.HalfWidth,
        };

        public LightFixture ToFixture() => new(
            Texture ?? string.Empty,
            new Vector3d(PositionX, PositionY, PositionZ),
            Radius,
            new Vector3f(ColourR, ColourG, ColourB),
            Area,
            Extent,
            TriangleCount,
            new Vector3f(DirectionX, DirectionY, DirectionZ),
            new Vector3f(AxisX, AxisY, AxisZ),
            HalfLength,
            HalfWidth);
    }

    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    [JsonSerializable(typeof(Document))]
    private sealed partial class LightFixtureJsonContext : JsonSerializerContext;
}
