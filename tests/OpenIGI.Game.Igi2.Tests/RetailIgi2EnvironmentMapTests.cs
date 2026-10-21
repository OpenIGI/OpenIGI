using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The baked environment cube maps, loaded, and the face order checked against the pixels rather than
/// only against the binary.
/// </summary>
/// <remarks>
/// The order was read twice, from two functions that never call each other:
/// <c>Envmap_CubeMapRender</c> (<c>igi2ed 0x581930</c>) bakes face <c>i</c> with a camera pointed along
/// one of six directions, and the water vertex shader at <c>0x6B95E8</c> samples the cube with the
/// reflection vector swizzled <c>.xzy</c>. Those two agree. This suite adds the third, independent
/// witness: the shipped faces themselves, which line up along the edges they share only if both the
/// order and the orientation are right.
/// </remarks>
public sealed class RetailIgi2EnvironmentMapTests
{
    /// <summary>Every environment map the tree ships loads, and its six faces agree on one size.</summary>
    /// <remarks>
    /// 31 maps and 186 faces is the whole corpus, the same count the naming test reaches from the other
    /// side. Loading them all is what says the decode covers the corpus and not just the first file.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_environment_map_loads_its_six_faces()
    {
        int maps = 0;
        int faces = 0;
        HashSet<int> edges = [];

        foreach ((WaterTask sheet, IresArchive archive) in Sheets())
        {
            foreach (Igi2EnvironmentMap map in Igi2EnvironmentMap.LoadAll(sheet, archive))
            {
                maps++;
                edges.Add(map.Edge);

                Assert.Equal(sheet.Id, map.TaskId);

                foreach (Igi2CubeFace face in AllFaces)
                {
                    faces++;
                    Assert.Equal(map.Edge * map.Edge, map[face].Length);
                }
            }
        }

        Assert.Equal(31, maps);
        Assert.Equal(186, faces);
        Assert.Equal<int[]>([16, 64, 128, 256, 512], [.. edges.Order()]);
    }

    /// <summary>The archive a water names is the archive its faces are in.</summary>
    /// <remarks>
    /// <c>Water_GetResourceFilename</c> (<c>igi2ed 0x6465C0</c>) builds <c>water_%d.res</c> from the
    /// level task id. Nothing in the archive says which water it belongs to, so the id is the only link,
    /// and this is the cheap end-to-end check that it is the right id.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Each_water_finds_its_archive_by_task_id()
    {
        int found = 0;

        foreach ((WaterTask sheet, IresArchive _) in Sheets())
        {
            Assert.Equal($"water_{sheet.Id}.res", Igi2EnvironmentMap.ArchiveName(sheet.Id));
            found++;
        }

        // 22 of the 23 sheets the tree places: one authors no environment map at all, so nothing ever
        // writes its archive.
        Assert.Equal(22, found);
    }

    /// <summary>
    /// The four faces around the horizon meet: each one's right edge is the next one's left edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that makes the face order a measurement rather than a reading. Under the order
    /// taken from the binary, the horizon ring runs
    /// <see cref="Igi2CubeFace.PositiveX"/> → <see cref="Igi2CubeFace.NegativeY"/> →
    /// <see cref="Igi2CubeFace.NegativeX"/> → <see cref="Igi2CubeFace.PositiveY"/> and back, left to
    /// right, with world-up at the top of all four. If any face were in the wrong slot, or upside down,
    /// or rotated, the columns either side of a seam would be looking in different directions and would
    /// not resemble each other.
    /// </para>
    /// <para>
    /// Two controls, because "the columns resemble each other" is only evidence if something else
    /// resembles them less. The first reverses one column, which is what a vertical flip of that face
    /// would do to it; the second pairs each face with the one <em>opposite</em> it, which shares no edge
    /// at all.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_faces_around_the_horizon_meet_along_the_edges_they_share()
    {
        int seams = 0;
        int beatsFlipped = 0;
        int beatsOpposite = 0;

        foreach ((WaterTask sheet, IresArchive archive) in Sheets())
        {
            foreach (Igi2EnvironmentMap map in Igi2EnvironmentMap.LoadAll(sheet, archive))
            {
                foreach ((Igi2CubeFace left, Igi2CubeFace right) in HorizonRing)
                {
                    seams++;

                    double[] a = Column(map, left, map.Edge - 1);
                    double[] b = Column(map, right, 0);
                    double[] opposite = Column(map, Opposite(right), 0);

                    double joined = MeanDifference(a, b);
                    double flipped = MeanDifference(a, [.. b.Reverse()]);
                    double unrelated = MeanDifference(a, opposite);

                    beatsFlipped += joined <= flipped ? 1 : 0;
                    beatsOpposite += joined <= unrelated ? 1 : 0;
                }
            }
        }

        Assert.Equal(124, seams);

        // Measured, not a threshold picked to pass: every one of the 124 seams in the corpus matches its
        // neighbour better than it matches that neighbour turned upside down, and better than it matches
        // the face across the cube.
        Assert.Equal(124, beatsFlipped);
        Assert.Equal(124, beatsOpposite);
    }

    /// <summary>The face the engine bakes looking up is the bright one.</summary>
    /// <remarks>
    /// <para>
    /// <c>Envmap_CubeMapRender</c> case 2 points the camera along engine +Z and case 3 along −Z, and the
    /// sampler's <c>.xzy</c> swizzle puts those in Direct3D's <c>POSITIVE_Y</c> and <c>NEGATIVE_Y</c>
    /// slots. If that pairing were the wrong way round every reflection in the game would show the ground
    /// where the sky belongs, so it is worth one check that leans on nothing geometric: sky is brighter
    /// than ground.
    /// </para>
    /// <para>
    /// 26 of the 31 maps are lit enough for the question to mean anything, and all 26 agree. The other
    /// five are all one water — 4069, a night level — whose whole cube averages under 5 of 255; four of
    /// them still come out the right way round and the fifth is a tie at 3.96 against 4.01. They are
    /// excluded by a stated threshold rather than quietly tolerated, because black over black is not
    /// evidence either way.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_face_that_looks_up_is_brighter_than_the_face_that_looks_down()
    {
        const double lit = 5.0;

        int maps = 0;
        int decisive = 0;
        List<string> darker = [];

        foreach ((WaterTask sheet, IresArchive archive) in Sheets())
        {
            foreach (Igi2EnvironmentMap map in Igi2EnvironmentMap.LoadAll(sheet, archive))
            {
                maps++;
                double up = MeanLuma(map[Igi2CubeFace.PositiveZ]);
                double down = MeanLuma(map[Igi2CubeFace.NegativeZ]);
                if (Math.Max(up, down) < lit)
                {
                    continue;
                }

                decisive++;
                if (up <= down)
                {
                    darker.Add($"water {map.TaskId} map {map.Index}: up {up:F3}, down {down:F3}");
                }
            }
        }

        Assert.Equal(31, maps);
        Assert.Equal<string[]>([], [.. darker]);
        Assert.Equal(26, decisive);
    }

    /// <summary>The sampling rule is a reflection, clamped, with the engine's Y and Z exchanged.</summary>
    /// <remarks>
    /// From the shader source at <c>igi2ed 0x6B95E8</c>:
    /// <c>mad oT0.xyz, v1.xzy, r3.xxx, -r0.xzy</c> over <c>r3.x = 2·max(V·N, vMaxReflectionAngle)</c>.
    /// The three cases here are the ones a wrong sign, a missed swizzle or a dropped clamp could not
    /// survive: looking straight down at flat water reflects straight up, onto the face the bake pointed
    /// upwards; looking down at 45° reflects forwards and up by the same amount; and a view that skims
    /// along the surface reflects along the horizon until <c>Max Reflection Angle</c> lifts it off.
    /// </remarks>
    [Fact]
    public void The_reflection_is_swizzled_into_the_cube_the_way_the_shader_swizzles_it()
    {
        Vector3f up = new(0f, 0f, 1f);

        // Straight down at flat water: the reflection is straight back up, and up is the engine's +Z,
        // which the swizzle puts on the sampler's +Y — the face baked looking up.
        Vector3f down = Igi2EnvironmentMap.CubeDirection(new Vector3f(0f, 0f, -1f), up, -1f);
        Assert.Equal(Igi2CubeFace.PositiveZ, Igi2EnvironmentMap.Project(down).Face);
        Assert.True(down.Y > 0.99f, $"Looking down should sample straight up, not {down}.");

        // Looking north and down at 45°: the reflection is north and up at 45°, so it lands on the +Y
        // face or the up face, never on either of the ±X ones.
        Vector3f north = Igi2EnvironmentMap.CubeDirection(new Vector3f(0f, 1f, -1f), up, -1f);
        Assert.True(north.Z > 0.5f, $"A northward view should reflect northward, not {north}.");
        Assert.True(north.Y > 0.5f, $"A downward view should reflect upward, not {north}.");

        // The clamp is the sheet's Max Reflection Angle: at a grazing view the cosine cannot fall below
        // it, so the reflection is pulled back towards the normal instead of opening out to the horizon.
        Vector3f skim = new(0f, 1f, -0.01f);
        Vector3f open = Igi2EnvironmentMap.CubeDirection(skim, up, -1f);
        Vector3f held = Igi2EnvironmentMap.CubeDirection(skim, up, 0.5f);

        Assert.True(open.Y < 0.05f, $"Unclamped, a skimming view reflects along the horizon, not {open}.");
        Assert.True(held.Y > 0.9f, $"Clamped at 0.5, it should be lifted well off the horizon, not {held}.");
    }

    /// <summary>Every face's own directions come back to it, and its corners are its neighbours' corners.</summary>
    /// <remarks>
    /// <see cref="Igi2EnvironmentMap.Direction"/> and <see cref="Igi2EnvironmentMap.Project"/> have to be
    /// inverses, or the seam test above would be checking its own arithmetic rather than the pixels.
    /// </remarks>
    [Fact]
    public void A_point_on_a_face_projects_back_onto_that_face()
    {
        foreach (Igi2CubeFace face in AllFaces)
        {
            foreach (float u in new[] { 0.05f, 0.5f, 0.95f })
            {
                foreach (float v in new[] { 0.05f, 0.5f, 0.95f })
                {
                    (Igi2CubeFace hit, float su, float sv) = Igi2EnvironmentMap.Project(
                        Igi2EnvironmentMap.Direction(face, u, v));

                    Assert.Equal(face, hit);
                    Assert.Equal(u, su, 4);
                    Assert.Equal(v, sv, 4);
                }
            }
        }
    }

    /// <summary>The horizon ring, as the binary lays it out: each pair shares one vertical edge.</summary>
    private static IEnumerable<(Igi2CubeFace Left, Igi2CubeFace Right)> HorizonRing =>
    [
        (Igi2CubeFace.PositiveX, Igi2CubeFace.NegativeY),
        (Igi2CubeFace.NegativeY, Igi2CubeFace.NegativeX),
        (Igi2CubeFace.NegativeX, Igi2CubeFace.PositiveY),
        (Igi2CubeFace.PositiveY, Igi2CubeFace.PositiveX),
    ];

    private static IEnumerable<Igi2CubeFace> AllFaces =>
    [
        Igi2CubeFace.PositiveX,
        Igi2CubeFace.NegativeX,
        Igi2CubeFace.PositiveZ,
        Igi2CubeFace.NegativeZ,
        Igi2CubeFace.PositiveY,
        Igi2CubeFace.NegativeY,
    ];

    private static Igi2CubeFace Opposite(Igi2CubeFace face) =>
        face switch
        {
            Igi2CubeFace.PositiveX => Igi2CubeFace.NegativeX,
            Igi2CubeFace.NegativeX => Igi2CubeFace.PositiveX,
            Igi2CubeFace.PositiveY => Igi2CubeFace.NegativeY,
            Igi2CubeFace.NegativeY => Igi2CubeFace.PositiveY,
            Igi2CubeFace.PositiveZ => Igi2CubeFace.NegativeZ,
            _ => Igi2CubeFace.PositiveZ,
        };

    private static double[] Column(Igi2EnvironmentMap map, Igi2CubeFace face, int x)
    {
        ReadOnlySpan<Bgra32> pixels = map[face];
        double[] column = new double[map.Edge];

        for (int y = 0; y < map.Edge; y++)
        {
            column[y] = Luma(pixels[(y * map.Edge) + x]);
        }

        return column;
    }

    private static double MeanDifference(double[] left, double[] right)
    {
        double total = 0.0;
        for (int i = 0; i < left.Length; i++)
        {
            total += Math.Abs(left[i] - right[i]);
        }

        return total / left.Length;
    }

    private static double MeanLuma(ReadOnlySpan<Bgra32> pixels)
    {
        double total = 0.0;
        foreach (Bgra32 pixel in pixels)
        {
            total += Luma(pixel);
        }

        return total / pixels.Length;
    }

    private static double Luma(Bgra32 pixel) => (0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B);

    private static IEnumerable<(WaterTask Sheet, IresArchive Archive)> Sheets()
    {
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.Combine(
                Path.GetDirectoryName(script)!,
                Igi2EnvironmentMap.ArchiveDirectory);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            foreach (WaterTask sheet in TasksOf<WaterTask>(harness.LevelRoot))
            {
                string path = Path.Combine(directory, Igi2EnvironmentMap.ArchiveName(sheet.Id));
                if (File.Exists(path))
                {
                    yield return (sheet, IresArchive.Read(File.ReadAllBytes(path)));
                }
            }
        }
    }

    private static IEnumerable<T> TasksOf<T>(GameTask root)
        where T : GameTask
    {
        foreach (GameTask child in root.Children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in TasksOf<T>(child))
            {
                yield return nested;
            }
        }
    }
}
