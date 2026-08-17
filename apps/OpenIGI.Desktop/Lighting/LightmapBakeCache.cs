using System.IO.Compression;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>
/// Reads and writes path-traced object-lightmap bakes to disk, so a level's beauty pass is a build
/// artifact loaded in a blink rather than traced afresh every load.
/// </summary>
/// <remarks>
/// The bake is the same shape of cost as the probe field's (<see cref="GiBakeCache"/>): expensive to trace,
/// but the answer never changes, because the geometry is static and the sun is a fixed direction per
/// mission. A file is keyed by the mission and the sun direction, colour and sky it was baked under, plus a
/// format version, so any change to those is a miss and a re-bake rather than a stale hit. The payload is
/// gzip-compressed, which the sRGB LDR pages take well.
/// </remarks>
public sealed class LightmapBakeCache
{
    private const uint Magic = 0x424D4C49; // "ILMB"
    private const byte FormatVersion = 1;
    private const float ColourEpsilon = 0.01f;

    private readonly string _writeDirectory;
    private readonly IReadOnlyList<string> _readDirectories;
    private readonly IIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="LightmapBakeCache"/> class.</summary>
    /// <param name="writeDirectory">Where newly-baked pages are written (created on first write).</param>
    /// <param name="readDirectories">Directories searched for a cached bake, in order; the first hit wins.</param>
    /// <param name="logger">Where to report a load or save failure.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public LightmapBakeCache(string writeDirectory, IReadOnlyList<string> readDirectories, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(writeDirectory);
        ArgumentNullException.ThrowIfNull(readDirectories);
        _writeDirectory = writeDirectory;
        _readDirectories = readDirectories;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Builds a cache with the shipped location searched first and the per-user location written to.</summary>
    /// <param name="shippedDirectory">The read-only, shippable directory beside the exe.</param>
    /// <param name="userDirectory">The writable per-user directory.</param>
    /// <param name="logger">Where to report a failure.</param>
    /// <returns>The cache.</returns>
    public static LightmapBakeCache Create(string shippedDirectory, string userDirectory, IIgiLogger? logger = null) =>
        new(userDirectory, [shippedDirectory, userDirectory], logger);

    /// <summary>Loads a cached bake for a mission, if one matching the bake inputs is present.</summary>
    /// <param name="missionId">The mission the bake is for.</param>
    /// <param name="sunDirection">The sun direction it must have been baked under.</param>
    /// <param name="sunColour">The sun colour it must have been baked under.</param>
    /// <param name="skyColour">The sky colour it must have been baked under.</param>
    /// <returns>The bake, or <see langword="null"/> on a miss.</returns>
    public BakedLightmaps? TryLoad(int missionId, Vector3f sunDirection, Vector3f sunColour, Vector3f skyColour)
    {
        foreach (string directory in _readDirectories)
        {
            string path = Path.Combine(directory, FileName(missionId));
            if (!File.Exists(path))
            {
                continue;
            }

            if (TryReadFile(path, sunDirection, sunColour, skyColour, out BakedLightmaps? baked))
            {
                _logger.Information("Lightmap", $"Loaded a path-traced lightmap bake for mission {missionId} from {path}.");
                return baked;
            }
        }

        return null;
    }

    /// <summary>Writes a baked set for a mission, so subsequent loads read it instead of tracing.</summary>
    /// <param name="missionId">The mission the bake is for.</param>
    /// <param name="sunDirection">The sun direction it was baked under.</param>
    /// <param name="sunColour">The sun colour it was baked under.</param>
    /// <param name="skyColour">The sky colour it was baked under.</param>
    /// <param name="baked">The composited pages.</param>
    /// <returns>The path written, or <see langword="null"/> on failure.</returns>
    public string? Save(int missionId, Vector3f sunDirection, Vector3f sunColour, Vector3f skyColour, BakedLightmaps baked)
    {
        ArgumentNullException.ThrowIfNull(baked);

        try
        {
            Directory.CreateDirectory(_writeDirectory);
            string path = Path.Combine(_writeDirectory, FileName(missionId));
            using var file = File.Create(path);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            using var writer = new BinaryWriter(gzip);

            writer.Write(Magic);
            writer.Write(FormatVersion);
            WriteVector(writer, sunDirection);
            WriteVector(writer, sunColour);
            WriteVector(writer, skyColour);
            baked.Write(writer);

            _logger.Information("Lightmap", $"Path-traced lightmap bake for mission {missionId} written to {path}.");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Lightmap", $"Could not write the lightmap bake for mission {missionId}: {ex.Message}");
            return null;
        }
    }

    private static string FileName(int missionId) => $"mission-{missionId}.lmb";

    private bool TryReadFile(
        string path, Vector3f sunDirection, Vector3f sunColour, Vector3f skyColour, out BakedLightmaps? baked)
    {
        baked = null;
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);

            if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
            {
                return false;
            }

            Vector3f storedSun = ReadVector(reader);
            Vector3f storedSunColour = ReadVector(reader);
            Vector3f storedSky = ReadVector(reader);
            if (!Match(storedSun, sunDirection) || !Match(storedSunColour, sunColour) || !Match(storedSky, skyColour))
            {
                return false; // baked under different inputs; a miss, not a stale hit
            }

            baked = BakedLightmaps.Read(reader);
            return baked is not null;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            _logger.Warning("Lightmap", $"A lightmap bake at {path} could not be read; it will be re-baked: {ex.Message}");
            return false;
        }
    }

    private static void WriteVector(BinaryWriter writer, Vector3f value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vector3f ReadVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static bool Match(Vector3f a, Vector3f b) =>
        MathF.Abs(a.X - b.X) <= ColourEpsilon
        && MathF.Abs(a.Y - b.Y) <= ColourEpsilon
        && MathF.Abs(a.Z - b.Z) <= ColourEpsilon;
}
