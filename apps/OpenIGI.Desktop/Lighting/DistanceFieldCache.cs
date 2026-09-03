using System.IO.Compression;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>
/// Reads and writes a level's baked signed distance fields to disk, so the structure the real-time
/// lighting traces against is a build artifact loaded in a blink rather than re-derived every load.
/// </summary>
/// <remarks>
/// <para>
/// The distance-field bake is the one thing standing between loading a mission and seeing it lit.
/// <see cref="DistanceFieldBake"/> calls it "a multi-second job" and runs it on a worker for exactly that
/// reason — doing it on the thread holding a live GL context loses the context to the operating system's
/// watchdog. But the lighting model cannot engage until the field exists, so the frame spends those
/// seconds on the artists' baked lightmaps and then visibly crosses over to the real-time picture. That
/// crossover is what this removes.
/// </para>
/// <para>
/// It is cacheable because it is the same answer every time. The fields are unsigned distances derived
/// from static level geometry at a fixed voxel resolution: no sun, no time of day, no camera, nothing
/// about how the level is being played. That makes it a stricter version of the bargain
/// <see cref="GiBakeCache"/> already trades on — the probe volume at least depends on the sun, and this
/// does not depend on anything but the level and one integer.
/// </para>
/// <para>
/// So a file is keyed by the mission and the voxel resolution, plus a format version, and any change to
/// those is a miss and a re-bake rather than a stale field silently loaded. That matters more here than
/// it does for a probe volume: a wrong distance field is invisible in a rendered frame — it does not
/// look wrong, it makes rays land on the wrong side of walls — and the only thing that would catch it is
/// <c>r_sdf_validate</c>, which nobody runs by habit.
/// </para>
/// <para>
/// Only the fields and their placements are stored. The GPU atlas is rebuilt from them on load by
/// <see cref="DistanceFieldAtlas.Build"/>, which is a pack and a copy rather than a trace — there is no
/// point spending disk on something derived that cheaply, and it keeps the file free of anything that
/// depends on the device it was written by.
/// </para>
/// </remarks>
public sealed class DistanceFieldCache
{
    private const uint Magic = 0x46445349; // "ISDF"

    // Bumped whenever the bake produces a different field for the same inputs, so an older file is a miss
    // and a re-bake rather than a stale hit that nothing downstream could notice.
    private const byte FormatVersion = 1;

    private const string LogCategory = "DistanceField";

    private readonly string _writeDirectory;
    private readonly IReadOnlyList<string> _readDirectories;
    private readonly IIgiLogger _logger;

    /// <summary>Initialises a new instance of the <see cref="DistanceFieldCache"/> class.</summary>
    /// <param name="writeDirectory">Where newly-baked fields are written (created on first write).</param>
    /// <param name="readDirectories">Directories searched for a cached field, in order; the first hit wins.</param>
    /// <param name="logger">Where to report a load or save failure.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DistanceFieldCache(string writeDirectory, IReadOnlyList<string> readDirectories, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(writeDirectory);
        ArgumentNullException.ThrowIfNull(readDirectories);
        _writeDirectory = writeDirectory;
        _readDirectories = readDirectories;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Builds a cache with the shipped location searched first and the per-user location written to.</summary>
    /// <param name="shippedDirectory">The read-only, shippable directory (e.g. <c>Assets/sdf</c> beside the exe).</param>
    /// <param name="userDirectory">The writable per-user directory.</param>
    /// <param name="logger">Where to report a failure.</param>
    /// <returns>The cache.</returns>
    public static DistanceFieldCache Create(string shippedDirectory, string userDirectory, IIgiLogger? logger = null) =>
        new(userDirectory, [shippedDirectory, userDirectory], logger);

    /// <summary>Loads a cached field for a mission, if one baked at the same resolution is present.</summary>
    /// <param name="missionId">The mission the field is for.</param>
    /// <param name="resolution">The voxel resolution it must have been baked at.</param>
    /// <returns>The field, or <see langword="null"/> on a miss.</returns>
    public SceneDistanceField? TryLoad(int missionId, int resolution)
    {
        foreach (string directory in _readDirectories)
        {
            string path = Path.Combine(directory, FileName(missionId));
            if (!File.Exists(path))
            {
                continue;
            }

            if (TryReadFile(path, resolution, out SceneDistanceField? field))
            {
                _logger.Information(
                    LogCategory,
                    $"Loaded a baked distance field for mission {missionId} from {path}: "
                    + $"{field!.FieldCount} field(s), {field.InstanceCount} placement(s).");
                return field;
            }
        }

        return null;
    }

    /// <summary>Writes a baked field for a mission, so subsequent loads read it instead of baking.</summary>
    /// <param name="missionId">The mission the field is for.</param>
    /// <param name="resolution">The voxel resolution it was baked at.</param>
    /// <param name="field">The baked field.</param>
    /// <returns>The path written, or <see langword="null"/> on failure.</returns>
    public string? Save(int missionId, int resolution, SceneDistanceField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        try
        {
            Directory.CreateDirectory(_writeDirectory);
            string path = Path.Combine(_writeDirectory, FileName(missionId));
            WriteFile(path, resolution, field);
            _logger.Information(
                LogCategory,
                $"Baked distance field for mission {missionId} written to {path}.");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                LogCategory,
                $"Could not write the baked distance field for mission {missionId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Deletes every baked field from the writable cache, so each mission re-bakes on next load.</summary>
    /// <returns>How many files were removed.</returns>
    /// <remarks>
    /// Only the per-user write directory is touched. A shipped, read-only directory is a build artifact
    /// rather than a cache the game is free to discard -- the same rule <see cref="GiBakeCache.DeleteAll"/>
    /// follows.
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

            foreach (string path in Directory.EnumerateFiles(_writeDirectory, "mission-*.sdf"))
            {
                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.Warning(LogCategory, $"Could not delete a baked field at {path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(LogCategory, $"Could not clear the baked distance-field cache: {ex.Message}");
        }

        return removed;
    }

    private static string FileName(int missionId) => $"mission-{missionId}.sdf";

    private bool TryReadFile(string path, int resolution, out SceneDistanceField? field)
    {
        field = null;

        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);

            if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
            {
                return false;
            }

            if (reader.ReadInt32() != resolution)
            {
                return false; // baked at a different resolution; a miss, not a stale hit
            }

            int fieldCount = reader.ReadInt32();
            if (fieldCount is < 0 or > 65536)
            {
                return false;
            }

            var fields = new MeshDistanceField[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                var origin = new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var voxelSize = new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                int sizeX = reader.ReadInt32();
                int sizeY = reader.ReadInt32();
                int sizeZ = reader.ReadInt32();
                float range = reader.ReadSingle();

                if (sizeX < 1 || sizeY < 1 || sizeZ < 1)
                {
                    return false;
                }

                long voxels = (long)sizeX * sizeY * sizeZ;
                if (voxels > int.MaxValue)
                {
                    return false;
                }

                byte[] distances = reader.ReadBytes((int)voxels);
                if (distances.Length != voxels)
                {
                    return false; // truncated
                }

                fields[i] = new MeshDistanceField(origin, voxelSize, sizeX, sizeY, sizeZ, range, distances);
            }

            int instanceCount = reader.ReadInt32();
            if (instanceCount < 0)
            {
                return false;
            }

            var placements = new (int, Vector3d, Matrix3f)[instanceCount];
            for (int i = 0; i < instanceCount; i++)
            {
                int fieldIndex = reader.ReadInt32();
                if (fieldIndex < 0 || fieldIndex >= fieldCount)
                {
                    return false;
                }

                var position = new Vector3d(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
                var orientation = new Matrix3f(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                placements[i] = (fieldIndex, position, orientation);
            }

            // The bounds each placement covers are recomputed here rather than stored, so a file can never
            // carry a box that disagrees with the field it belongs to.
            field = SceneDistanceField.FromInstances(fields, placements);
            return true;
        }
        catch (Exception ex)
            when (ex is IOException or EndOfStreamException or InvalidDataException
                or ArgumentException or ArgumentOutOfRangeException)
        {
            _logger.Warning(
                LogCategory,
                $"A baked distance field at {path} could not be read; it will be re-baked: {ex.Message}");
            return false;
        }
    }

    private static void WriteFile(string path, int resolution, SceneDistanceField field)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzip);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(resolution);

        IReadOnlyList<MeshDistanceField> fields = field.Fields;
        writer.Write(fields.Count);

        foreach (MeshDistanceField baked in fields)
        {
            writer.Write(baked.Origin.X);
            writer.Write(baked.Origin.Y);
            writer.Write(baked.Origin.Z);
            writer.Write(baked.VoxelSize.X);
            writer.Write(baked.VoxelSize.Y);
            writer.Write(baked.VoxelSize.Z);
            writer.Write(baked.SizeX);
            writer.Write(baked.SizeY);
            writer.Write(baked.SizeZ);
            writer.Write(baked.Range);
            writer.Write(baked.Distances);
        }

        SceneDistanceField.InstanceView[] instances = field.Describe();
        writer.Write(instances.Length);

        foreach (SceneDistanceField.InstanceView instance in instances)
        {
            writer.Write(instance.FieldIndex);
            writer.Write(instance.Position.X);
            writer.Write(instance.Position.Y);
            writer.Write(instance.Position.Z);
            writer.Write(instance.Orientation.M11);
            writer.Write(instance.Orientation.M12);
            writer.Write(instance.Orientation.M13);
            writer.Write(instance.Orientation.M21);
            writer.Write(instance.Orientation.M22);
            writer.Write(instance.Orientation.M23);
            writer.Write(instance.Orientation.M31);
            writer.Write(instance.Orientation.M32);
            writer.Write(instance.Orientation.M33);
        }
    }
}
