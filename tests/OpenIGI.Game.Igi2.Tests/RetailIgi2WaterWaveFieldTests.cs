using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The wave displacement, over every sheet the game ships.</summary>
/// <remarks>
/// Worth its own suite because the port previously recorded this as unrecoverable. It is recovered, and
/// what these tests hold down is the handful of places where a plausible misreading gives a sea that
/// looks fine in a screenshot and is wrong — the wavelength that is named a frequency above all.
/// </remarks>
public sealed class RetailIgi2WaterWaveFieldTests
{
    /// <summary>Frequency is a wavelength, and the derived spatial frequency is its reciprocal.</summary>
    /// <remarks>
    /// <c>WaterLayer_UpdateInternalDataHandler</c> computes <c>2π / (Frequency × 4096)</c>. So doubling
    /// the authored number <b>halves</b> the number of crests. Taking the name at face value would put
    /// the short waves where the long ones belong across every sheet in the game, and nothing about the
    /// surface's shape or its motion would look obviously broken.
    /// </remarks>
    [Fact]
    public void The_frequency_parameter_is_really_a_wavelength()
    {
        Igi2WaterWaveField shortWave = FieldOf(new Igi2WaterWave(4f, 0.2f, 1f, 0f));
        Igi2WaterWaveField longWave = FieldOf(new Igi2WaterWave(8f, 0.2f, 1f, 0f));

        // Twice the authored number, half the spatial frequency.
        Assert.Equal(shortWave.Trains[0].SpatialFrequency / 2f, longWave.Trains[0].SpatialFrequency, 6);

        // And a full turn of phase takes twice as far to travel: 4 m against 8 m, in engine units.
        Assert.Equal(MathF.Tau / (4f * 4096f), shortWave.Trains[0].SpatialFrequency, 8);
        Assert.Equal(MathF.Tau / (8f * 4096f), longWave.Trains[0].SpatialFrequency, 8);
    }

    /// <summary>A zero angle travels north, because the direction pair is sine then cosine.</summary>
    [Fact]
    public void A_zero_angle_travels_north()
    {
        Igi2WaterWaveTrain north = FieldOf(new Igi2WaterWave(4f, 0.2f, 1f, 0f)).Trains[0];
        Assert.Equal(0f, north.DirectionX, 5);
        Assert.Equal(1f, north.DirectionY, 5);

        Igi2WaterWaveTrain east = FieldOf(new Igi2WaterWave(4f, 0.2f, 1f, 90f)).Trains[0];
        Assert.Equal(1f, east.DirectionX, 5);
        Assert.Equal(0f, east.DirectionY, 5);
    }

    /// <summary>A train's crests are one wavelength apart along the way it travels, and flat across it.</summary>
    /// <remarks>
    /// The claim that says the direction is used as a direction. A single train is a corrugation: the
    /// height has to repeat every wavelength along its heading and not vary at all at right angles to
    /// it. Either mistake — swapping the pair, or dropping it — passes a test that only samples one
    /// point.
    /// </remarks>
    [Fact]
    public void One_train_repeats_along_its_heading_and_is_flat_across_it()
    {
        const float Wavelength = 4f;
        Igi2WaterWaveField field = FieldOf(new Igi2WaterWave(Wavelength, 0.25f, 0f, 0f));
        float units = Wavelength * 4096f;

        // Compared against the amplitude rather than to a fixed number of places: the phase argument runs
        // into the hundreds of thousands of units, so single precision leaves a few thousandths on a
        // height of a thousand, and an absolute tolerance would be measuring the float and not the wave.
        float amplitude = 0.25f * 4096f;

        for (float y = -2f * units; y <= 2f * units; y += units / 8f)
        {
            // One wavelength north is the same height.
            float here = field.HeightAt(0f, y, 0.0);
            Assert.True(
                MathF.Abs(field.HeightAt(0f, y + units, 0.0) - here) < amplitude * 1e-4f,
                $"the train did not repeat after one wavelength at y = {y}.");

            // And moving east, across the train, changes nothing at all.
            Assert.True(
                MathF.Abs(field.HeightAt(9_999f, y, 0.0) - here) < amplitude * 1e-4f,
                $"the train varied across its own heading at y = {y}.");
        }
    }

    /// <summary>The sample step is a hundredth of the mean wavelength.</summary>
    /// <remarks>
    /// <c>40.959999 / layers × Σ Frequency</c> — tied to the waves it measures rather than a fixed
    /// epsilon, and taken over the layers the engine actually reads, so a fifth one does not move it.
    /// </remarks>
    [Fact]
    public void The_sample_step_is_a_hundredth_of_the_mean_wavelength()
    {
        Igi2WaterWaveField field = FieldOf(
            new Igi2WaterWave(4f, 0.2f, 1f, 0f),
            new Igi2WaterWave(8f, 0.1f, 2f, 90f));

        Assert.Equal(40.959999f / 2f * 12f, field.SampleStep, 3);
        Assert.Equal(6f * 4096f / 100f, field.SampleStep, 2);
    }

    /// <summary>A fifth layer is authored data the engine never reads.</summary>
    [Fact]
    public void Only_the_first_four_layers_are_evaluated()
    {
        Igi2WaterWave[] five =
        [
            new(4f, 0.2f, 1f, 0f),
            new(5f, 0.2f, 1f, 45f),
            new(6f, 0.2f, 1f, 90f),
            new(7f, 0.2f, 1f, 135f),
            new(8f, 0.9f, 9f, 180f),
        ];

        Igi2WaterWaveField four = FieldOf(five[..4]);
        Igi2WaterWaveField all = FieldOf(five);

        Assert.Equal(4, all.Trains.Count);
        Assert.Equal(four.SampleStep, all.SampleStep, 4);
        Assert.Equal(four.HeightAt(1_000f, 2_000f, 3.0), all.HeightAt(1_000f, 2_000f, 3.0), 4);
    }

    /// <summary>Amplitude falls away from the centre, and never grows.</summary>
    /// <remarks>
    /// The divisor is clamped upward, not the result, so a point inside the first metre — and any sheet
    /// whose <c>Fadeout</c> is nought — keeps full height rather than being multiplied by something
    /// unbounded.
    /// </remarks>
    [Fact]
    public void The_waves_flatten_outwards_and_never_grow()
    {
        Igi2WaterWaveField field = FieldOf(0.5f, new Igi2WaterWave(4f, 0.2f, 1f, 0f));

        Assert.Equal(1f, field.AttenuationAt(0f));
        Assert.Equal(1f, field.AttenuationAt(4096f));

        float previous = 1f;
        for (float radius = 0f; radius < 400_000f; radius += 4096f)
        {
            float attenuation = field.AttenuationAt(radius);
            Assert.InRange(attenuation, 0f, 1f);
            Assert.True(attenuation <= previous + 1e-6f, $"the waves grew at {radius}");
            previous = attenuation;
        }

        // A sheet that does not fade keeps its waves everywhere.
        Igi2WaterWaveField unfading = FieldOf(0f, new Igi2WaterWave(4f, 0.2f, 1f, 0f));
        Assert.Equal(1f, unfading.AttenuationAt(1_000_000f));
    }

    /// <summary>The normal is the surface's, and it points up.</summary>
    /// <remarks>
    /// Checked against the two slopes rebuilt from the same samples, so what this pins is the step from
    /// slopes to a normal — the cross product's winding, the sign of each tangent, and the normalisation.
    /// Transposing the two offsets or flipping the third component fails it. A flat sheet is the other
    /// end of the same claim: no trains, straight up.
    /// </remarks>
    [Fact]
    public void The_normal_follows_the_surface_and_points_up()
    {
        Igi2WaterWaveField field = FieldOf(new Igi2WaterWave(4f, 0.2f, 1.5f, 30f));

        for (float x = -20_000f; x <= 20_000f; x += 2_500f)
        {
            Vector3f normal = field.NormalAt(x, 7_000f, 2.0);

            Assert.Equal(1f, MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z)), 4);
            Assert.True(normal.Z > 0f, $"the normal pointed into the water at {x}");

            // Uphill to the east means the normal leans west, and by the slope. Both slopes go into the
            // expected value: the train runs at thirty degrees, so the cross-slope is not zero and a
            // one-dimensional comparison would be checking a different surface.
            float here = field.HeightAt(x, 7_000f, 2.0);
            float slopeEast = (field.HeightAt(x + field.SampleStep, 7_000f, 2.0) - here) / field.SampleStep;
            float slopeNorth = (field.HeightAt(x, 7_000f + field.SampleStep, 2.0) - here) / field.SampleStep;
            float scale = MathF.Sqrt((slopeEast * slopeEast) + (slopeNorth * slopeNorth) + 1f);

            Assert.Equal(-slopeEast / scale, normal.X, 3);
            Assert.Equal(-slopeNorth / scale, normal.Y, 3);
            Assert.Equal(1f / scale, normal.Z, 3);
        }

        Assert.Equal(Vector3f.UnitZ, FieldOf().NormalAt(0f, 0f, 0.0));
    }

    /// <summary>Every sheet the game ships evaluates to a finite, bounded surface.</summary>
    /// <remarks>
    /// The corpus check. A wavelength of nought would divide by nothing and a runaway amplitude would
    /// put the sea through the sky; neither is hypothetical, because both are authored numbers. The
    /// bound is the sum of the trains' own amplitudes, which is the most the sines can reach.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_sheet_evaluates_to_a_bounded_surface()
    {
        int sheets = 0;
        int trains = 0;
        int capped = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2WaterSurface water = Igi2WaterSurface.Build(level.Root);
            foreach (Igi2WaterSheet sheet in water.Sheets)
            {
                Igi2WaterWaveField field = Igi2WaterWaveField.For(sheet);
                sheets++;
                trains += field.Trains.Count;
                capped += sheet.Waves.Count > Igi2WaterSurface.MaximumActiveLayers ? 1 : 0;

                float ceiling = 0f;
                foreach (Igi2WaterWaveTrain train in field.Trains)
                {
                    Assert.True(float.IsFinite(train.SpatialFrequency), "a sheet authored a wavelength of nought.");
                    Assert.True(float.IsFinite(train.AmplitudeUnits));
                    ceiling += MathF.Abs(train.AmplitudeUnits);
                }

                Assert.True(float.IsFinite(field.SampleStep));
                Assert.True(field.SampleStep >= 0f);

                // Walk the sheet and check nothing leaves the envelope the amplitudes allow.
                float radius = (float)sheet.Radius;
                for (int i = 0; i <= 12; i++)
                {
                    for (int j = 0; j <= 12; j++)
                    {
                        float x = radius * ((i / 6f) - 1f);
                        float y = radius * ((j / 6f) - 1f);

                        float height = field.HeightAt(x, y, 3.5, MathF.Sqrt((x * x) + (y * y)));
                        Assert.True(float.IsFinite(height), $"sheet {sheet.TaskId} gave {height} at ({x}, {y}).");
                        Assert.InRange(height, -ceiling - 1f, ceiling + 1f);

                        Vector3f normal = field.NormalAt(x, y, 3.5);
                        Assert.True(normal.Z > 0f);
                    }
                }
            }
        }

        // Every sheet the shipped levels place, and the trains they carry between them.
        Assert.Equal(23, sheets);
        Assert.True(trains > 0, "not one shipped sheet declared a wave layer.");

        // Recorded rather than asserted either way: whether any level authors a fifth layer the engine
        // silently drops is a fact about the corpus, and the reason the cap is implemented at all.
        Assert.InRange(capped, 0, sheets);
    }

    private static Igi2WaterWaveField FieldOf(params Igi2WaterWave[] waves) => FieldOf(0f, waves);

    private static Igi2WaterWaveField FieldOf(float fadeOut, params Igi2WaterWave[] waves) =>
        Igi2WaterWaveField.For(new Igi2WaterSheet(
            1,
            default,
            50.0 * 4096.0,
            1f,
            1f,
            fadeOut,
            1f,
            default,
            [],
            string.Empty,
            string.Empty,
            waves));
}
