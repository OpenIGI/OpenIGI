using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Pins that a distance field written to disk and read back is the same field.
/// </summary>
/// <remarks>
/// <para>
/// This matters more than the equivalent check on a probe volume, because of how a wrong distance field
/// fails. It does not look wrong. Nothing goes black, nothing flickers, no exception is thrown — rays
/// simply land on the wrong side of walls, and the symptom is light leaking through geometry somewhere
/// else entirely, days later. The only tool that can see it is <c>r_sdf_validate</c>, which compares
/// clipmaps against the exact field and which nobody runs by habit.
/// </para>
/// <para>
/// So the assertions are behavioural rather than structural: the reloaded field is asked the same
/// questions the renderer asks it — how far is the nearest surface from here, does a ray from here hit
/// anything — and has to give the same answers. Comparing voxels field by field would pass on a file
/// whose placements had been silently transposed, which is exactly the mistake the format invites: the
/// orientation stored per instance is already the inverse of the one the renderer draws with, so a round
/// trip that "helpfully" inverts it once more is wrong in a way no voxel comparison would notice.
/// </para>
/// </remarks>
public sealed class DistanceFieldCacheTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "openigi-sdf-cache-tests", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void A_saved_field_reloads_as_the_same_field()
    {
        SceneDistanceField original = BuildScene();
        DistanceFieldCache cache = Cache();

        Assert.NotNull(cache.Save(missionId: 3, resolution: 8, original));

        SceneDistanceField? loaded = cache.TryLoad(missionId: 3, resolution: 8);

        Assert.NotNull(loaded);
        Assert.Equal(original.FieldCount, loaded!.FieldCount);
        Assert.Equal(original.InstanceCount, loaded.InstanceCount);
        Assert.Equal(original.BytesUsed, loaded.BytesUsed);

        // The questions the renderer actually asks, over a grid that crosses both placements and the
        // empty air around them.
        for (int x = -6; x <= 10; x++)
        {
            for (int y = -6; y <= 10; y++)
            {
                for (int z = -4; z <= 6; z++)
                {
                    var point = new Vector3d(x * 0.7, y * 0.7, z * 0.7);
                    Assert.Equal(original.Sample(point), loaded.Sample(point), 5);
                }
            }
        }

        // And the trace, which is what the gather rays do: same hit, same distance.
        foreach (Vector3f direction in (ReadOnlySpan<Vector3f>)
            [Vector3f.UnitX, Vector3f.UnitY, Vector3f.UnitZ, new(0.577f, 0.577f, 0.577f)])
        {
            var origin = new Vector3d(-5.0, -5.0, 0.5);
            bool hitBefore = original.TryTrace(origin, direction, 40f, out float distanceBefore);
            bool hitAfter = loaded.TryTrace(origin, direction, 40f, out float distanceAfter);

            Assert.Equal(hitBefore, hitAfter);
            if (hitBefore)
            {
                Assert.Equal(distanceBefore, distanceAfter, 4);
            }
        }
    }

    /// <summary>
    /// Every placement comes back where it was, with its rotation applied the same number of times.
    /// </summary>
    /// <remarks>
    /// The specific trap this guards. <c>Describe()</c> hands out an orientation that has already been
    /// inverted, and <c>FromInstances</c> stores what it is given verbatim — so the round trip is correct
    /// only as long as neither end decides to be clever. A rotated placement is what makes that visible;
    /// an identity one would round-trip through a transpose without complaint.
    /// </remarks>
    [Fact]
    public void A_rotated_placement_is_not_transposed_by_the_round_trip()
    {
        SceneDistanceField original = BuildScene();
        DistanceFieldCache cache = Cache();
        cache.Save(missionId: 1, resolution: 8, original);

        SceneDistanceField loaded = cache.TryLoad(missionId: 1, resolution: 8)!;

        SceneDistanceField.InstanceView[] before = original.Describe();
        SceneDistanceField.InstanceView[] after = loaded.Describe();

        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].FieldIndex, after[i].FieldIndex);
            Assert.Equal(before[i].Position.X, after[i].Position.X, 6);
            Assert.Equal(before[i].Position.Y, after[i].Position.Y, 6);
            Assert.Equal(before[i].Position.Z, after[i].Position.Z, 6);

            Assert.Equal(before[i].Orientation.M11, after[i].Orientation.M11, 6);
            Assert.Equal(before[i].Orientation.M12, after[i].Orientation.M12, 6);
            Assert.Equal(before[i].Orientation.M21, after[i].Orientation.M21, 6);
            Assert.Equal(before[i].Orientation.M22, after[i].Orientation.M22, 6);
            Assert.Equal(before[i].Orientation.M33, after[i].Orientation.M33, 6);

            // Recomputed on load rather than stored, so this is also a check that they agree.
            Assert.Equal(before[i].BoundsMin.X, after[i].BoundsMin.X, 5);
            Assert.Equal(before[i].BoundsMax.Z, after[i].BoundsMax.Z, 5);
        }
    }

    /// <summary>A field baked at another resolution is a miss, not a stale hit.</summary>
    [Fact]
    public void A_field_baked_at_another_resolution_is_a_miss()
    {
        DistanceFieldCache cache = Cache();
        cache.Save(missionId: 2, resolution: 32, BuildScene());

        Assert.Null(cache.TryLoad(missionId: 2, resolution: 46));
        Assert.NotNull(cache.TryLoad(missionId: 2, resolution: 32));
    }

    /// <summary>A mission with nothing cached is a miss rather than a failure.</summary>
    [Fact]
    public void An_absent_field_is_a_miss()
    {
        Assert.Null(Cache().TryLoad(missionId: 9, resolution: 32));
    }

    /// <summary>A truncated or corrupt file is a miss, so the level re-bakes rather than dying.</summary>
    [Fact]
    public void A_corrupt_file_is_a_miss_rather_than_a_throw()
    {
        DistanceFieldCache cache = Cache();
        string path = cache.Save(missionId: 4, resolution: 8, BuildScene())!;

        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        Assert.Null(cache.TryLoad(missionId: 4, resolution: 8));
    }

    /// <summary>Clearing the cache removes what was written, so the next load re-bakes.</summary>
    [Fact]
    public void DeleteAll_clears_the_writable_cache()
    {
        DistanceFieldCache cache = Cache();
        cache.Save(missionId: 5, resolution: 8, BuildScene());
        cache.Save(missionId: 6, resolution: 8, BuildScene());

        Assert.Equal(2, cache.DeleteAll());
        Assert.Null(cache.TryLoad(missionId: 5, resolution: 8));
    }

    private DistanceFieldCache Cache() => new(_directory, [_directory]);

    /// <summary>Two distinct fields, placed three times, one of them rotated a quarter turn.</summary>
    private static SceneDistanceField BuildScene()
    {
        MeshDistanceField block = Field(6, 5, 4, seed: 11);
        MeshDistanceField post = Field(3, 3, 9, seed: 29);

        // A quarter turn about Z, which a transpose would turn into the opposite quarter turn.
        var turned = new Matrix3f(
            0f, 1f, 0f,
            -1f, 0f, 0f,
            0f, 0f, 1f);

        return SceneDistanceField.FromInstances(
            [block, post],
            [
                (0, new Vector3d(0.0, 0.0, 0.0), Matrix3f.Identity),
                (1, new Vector3d(3.5, -1.25, 0.75), turned),
                (0, new Vector3d(-2.0, 4.0, -0.5), turned),
            ]);
    }

    private static MeshDistanceField Field(int sizeX, int sizeY, int sizeZ, int seed)
    {
        byte[] distances = new byte[sizeX * sizeY * sizeZ];
        var random = new Random(seed);
        random.NextBytes(distances);

        return new MeshDistanceField(
            new Vector3f(-1f, -1f, -0.5f),
            new Vector3f(0.25f, 0.3f, 0.2f),
            sizeX,
            sizeY,
            sizeZ,
            range: 1.5f,
            distances);
    }
}
