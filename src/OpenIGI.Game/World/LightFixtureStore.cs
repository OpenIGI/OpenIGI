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

    /// <summary>Writes a mission's fixtures to a file, creating the directory if it is missing.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="fixtures">The fixtures to write.</param>
    /// <param name="logger">Where to report the result.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void Save(string path, IReadOnlyList<LightFixture> fixtures, IIgiLogger? logger = null)
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

            File.WriteAllText(path, JsonSerializer.Serialize(records, LightFixtureJsonContext.Default.RecordArray));
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
    /// <param name="logger">Where to report the result.</param>
    /// <returns><see langword="true"/> when a file was read; <see langword="false"/> when none exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static bool TryLoad(
        string path, out IReadOnlyList<LightFixture> fixtures, IIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        IIgiLogger log = logger ?? NullLogger.Instance;
        fixtures = [];

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            Record[]? records = JsonSerializer.Deserialize(
                File.ReadAllText(path), LightFixtureJsonContext.Default.RecordArray);

            if (records is null)
            {
                return false;
            }

            LightFixture[] read = new LightFixture[records.Length];

            for (int i = 0; i < records.Length; i++)
            {
                read[i] = records[i].ToFixture();
            }

            fixtures = read;
            log.Information(LogCategory, $"Read {read.Length} light fixture(s) from {path}.");
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
        };

        public LightFixture ToFixture() => new(
            Texture ?? string.Empty,
            new Vector3d(PositionX, PositionY, PositionZ),
            Radius,
            new Vector3f(ColourR, ColourG, ColourB),
            Area,
            Extent,
            TriangleCount);
    }

    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    [JsonSerializable(typeof(Record[]))]
    private sealed partial class LightFixtureJsonContext : JsonSerializerContext;
}
