using System.IO.Compression;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Reads and writes baked irradiance probe volumes to disk, so a level's global-illumination bake is a
/// build artifact loaded in a blink rather than traced afresh every load.
/// </summary>
/// <remarks>
/// <para>
/// The probe bake is the one part of a level load that blocks for seconds: the geometry question — what
/// each probe can see — is expensive, and it is what the interactive load pumps and waits on. But the
/// answer never changes, because the levels are static and the sun is a fixed direction per mission
/// (that is the whole bargain the probe volume is built on). So it can be traced once, offline, and the
/// result shipped: this cache is that result. A mission with a cached bake loads its lighting instantly;
/// one without falls back to tracing it, and writes the result so the next load is fast too.
/// </para>
/// <para>
/// A cache file is keyed by the mission, the probe spacing and the sun direction it was baked under — the
/// three inputs that decide the result — plus a format version, so a change to any of them is a miss and
/// a re-bake, never a stale volume silently loaded. The payload (a few floats per probe, tens of
/// thousands of probes) is gzip-compressed, which floats of gently-varying irradiance take well.
/// </para>
/// </remarks>
public sealed class GiBakeCache
{
    private const uint Magic = 0x49474956; // "IGIV"
    private const byte FormatVersion = 1;
    private const float DirectionEpsilon = 0.01f;

    private readonly string _writeDirectory;
    private readonly IReadOnlyList<string> _readDirectories;
    private readonly IIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="GiBakeCache"/> class.</summary>
    /// <param name="writeDirectory">Where newly-baked volumes are written (created on first write).</param>
    /// <param name="readDirectories">Directories searched for a cached volume, in order; the first hit wins.</param>
    /// <param name="logger">Where to report a load or save failure.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public GiBakeCache(string writeDirectory, IReadOnlyList<string> readDirectories, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(writeDirectory);
        ArgumentNullException.ThrowIfNull(readDirectories);
        _writeDirectory = writeDirectory;
        _readDirectories = readDirectories;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Builds a cache with the shipped location searched first and the per-user location written to.</summary>
    /// <param name="shippedDirectory">The read-only, shippable directory (e.g. <c>Assets/gi</c> beside the exe).</param>
    /// <param name="userDirectory">The writable per-user directory.</param>
    /// <param name="logger">Where to report a failure.</param>
    /// <returns>The cache.</returns>
    public static GiBakeCache Create(string shippedDirectory, string userDirectory, IIgiLogger? logger = null) =>
        new(userDirectory, [shippedDirectory, userDirectory], logger);

    /// <summary>Loads a cached volume for a mission, if one matching the bake inputs is present.</summary>
    /// <param name="missionId">The mission the volume is for.</param>
    /// <param name="spacing">The probe spacing it must have been baked at.</param>
    /// <param name="sunDirection">The sun direction it must have been baked under.</param>
    /// <returns>The volume, or <see langword="null"/> on a miss.</returns>
    public IrradianceVolume? TryLoad(int missionId, float spacing, Vector3f sunDirection)
    {
        foreach (string directory in _readDirectories)
        {
            string path = Path.Combine(directory, FileName(missionId));
            if (!File.Exists(path))
            {
                continue;
            }

            if (TryReadFile(path, spacing, sunDirection, out IrradianceVolume? volume))
            {
                _logger.Information("Light", $"Loaded a baked probe field for mission {missionId} from {path}.");
                return volume;
            }
        }

        return null;
    }

    /// <summary>Writes a baked volume for a mission, so subsequent loads read it instead of tracing.</summary>
    /// <param name="missionId">The mission the volume is for.</param>
    /// <param name="spacing">The probe spacing it was baked at.</param>
    /// <param name="sunDirection">The sun direction it was baked under.</param>
    /// <param name="volume">The baked volume.</param>
    /// <returns>The path written, or <see langword="null"/> on failure.</returns>
    public string? Save(int missionId, float spacing, Vector3f sunDirection, IrradianceVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        try
        {
            Directory.CreateDirectory(_writeDirectory);
            string path = Path.Combine(_writeDirectory, FileName(missionId));
            WriteFile(path, spacing, sunDirection, volume);
            _logger.Information("Light", $"Baked probe field for mission {missionId} written to {path}.");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Light", $"Could not write the baked probe field for mission {missionId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Deletes every baked field from the writable cache, so each mission re-traces on next load.</summary>
    /// <returns>How many files were removed.</returns>
    /// <remarks>
    /// For the live re-bake: clears what was traced before so the light is generated afresh rather than
    /// reloaded. Only the per-user write directory is touched -- a shipped, read-only <c>Assets/gi</c> is
    /// left alone, being a build artifact rather than a cache the game is free to discard.
    /// </remarks>
    public int DeleteAll()
    {
        int removed = 0;

        try
        {
            if (!Directory.Exists(_writeDirectory))
            {
                return 0;
            }

            foreach (string path in Directory.EnumerateFiles(_writeDirectory, "mission-*.giv"))
            {
                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warning("Light", $"Could not delete a baked field at {path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Light", $"Could not clear the baked-field cache: {ex.Message}");
        }

        return removed;
    }

    private static string FileName(int missionId) => $"mission-{missionId}.giv";

    private bool TryReadFile(string path, float spacing, Vector3f sunDirection, out IrradianceVolume? volume)
    {
        volume = null;
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);

            if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
            {
                return false;
            }

            float storedSpacing = reader.ReadSingle();
            var storedSun = new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (MathF.Abs(storedSpacing - spacing) > 1e-4f || !DirectionsMatch(storedSun, sunDirection))
            {
                return false; // baked under different inputs; a miss, not a stale hit
            }

            var origin = new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            int countX = reader.ReadInt32();
            int countY = reader.ReadInt32();
            int countZ = reader.ReadInt32();
            if (countX < 1 || countY < 1 || countZ < 1)
            {
                return false;
            }

            var built = new IrradianceVolume(origin, storedSpacing, countX, countY, countZ);
            int probeCount = built.ProbeCount;
            Span<float> coefficients = stackalloc float[IrradianceVolume.CoefficientsPerProbe];
            for (int probe = 0; probe < probeCount; probe++)
            {
                for (int i = 0; i < IrradianceVolume.CoefficientsPerProbe; i++)
                {
                    coefficients[i] = reader.ReadSingle();
                }

                float meanDistance = reader.ReadSingle();
                float meanSquaredDistance = reader.ReadSingle();
                built.SetProbe(probe, coefficients, meanDistance, meanSquaredDistance);
            }

            volume = built;
            return true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            _logger.Warning("Light", $"A baked probe field at {path} could not be read; it will be re-baked: {ex.Message}");
            return false;
        }
    }

    private static void WriteFile(string path, float spacing, Vector3f sunDirection, IrradianceVolume volume)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzip);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(spacing);
        writer.Write(sunDirection.X);
        writer.Write(sunDirection.Y);
        writer.Write(sunDirection.Z);
        writer.Write(volume.Origin.X);
        writer.Write(volume.Origin.Y);
        writer.Write(volume.Origin.Z);
        writer.Write(volume.CountX);
        writer.Write(volume.CountY);
        writer.Write(volume.CountZ);

        foreach (float value in volume.Coefficients)
        {
            writer.Write(value);
        }

        foreach (float value in volume.Distances)
        {
            writer.Write(value);
        }
    }

    private static bool DirectionsMatch(Vector3f a, Vector3f b) =>
        MathF.Abs(a.X - b.X) <= DirectionEpsilon
        && MathF.Abs(a.Y - b.Y) <= DirectionEpsilon
        && MathF.Abs(a.Z - b.Z) <= DirectionEpsilon;
}
