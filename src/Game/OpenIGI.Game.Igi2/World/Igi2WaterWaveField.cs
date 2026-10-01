using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One wave train, in the form the surface is actually evaluated in.</summary>
/// <param name="DirectionX">The east component of the direction the train travels.</param>
/// <param name="DirectionY">The north component.</param>
/// <param name="SpatialFrequency">Radians of phase per engine unit travelled.</param>
/// <param name="AmplitudeUnits">How far the crest rises, in engine units.</param>
/// <param name="Speed">Radians of phase per second.</param>
/// <remarks>
/// Derived from a <c>WaterLayer</c>'s four authored parameters by
/// <c>WaterLayer_UpdateInternalDataHandler</c> (<c>igi2ed 0x647750</c>), which is five lines long and
/// settles every one of them:
/// <code>
/// [19] = sin(Angle * 0.017453292)      // the direction pair, from degrees
/// [20] = cos(Angle * 0.017453292)
/// [13] = 6.2831855 / (Frequency * 4096) // so "Frequency" is a wavelength, in metres
/// [14] = Amplitude * 4096               // metres to engine units
/// </code>
/// <para>
/// The one that matters is <c>[13]</c>. <c>Frequency</c> is the parameter's name and a
/// <em>wavelength</em> is what it holds — the engine divides by it. A reader that took the name at face
/// value would get the sea inside out, with the short waves long and the long ones short.
/// </para>
/// <para>
/// The direction pair is <c>(sin, cos)</c> and not the other way round, so a zero angle travels along
/// <b>+Y</b> — a compass bearing, matching the camera's own zero heading.
/// </para>
/// </remarks>
public readonly record struct Igi2WaterWaveTrain(
    float DirectionX,
    float DirectionY,
    float SpatialFrequency,
    float AmplitudeUnits,
    float Speed);

/// <summary>
/// The height and the normal of a water sheet: a sum of sine trains, evaluated per vertex.
/// </summary>
/// <remarks>
/// <para>
/// <b>This corrects an earlier claim of this port's, which said the displacement was locked inside a
/// compiled vertex shader and could not be recovered.</b> It is not: <c>Water_VertexFunction</c>
/// (<c>igi2ed 0x645B30</c>) sums the trains on the CPU and builds the normal by finite difference. The
/// misreading came from the name of <c>Water_SetVertexFunctionParameters</c> — those parameters are
/// arguments to an ordinary function, and <c>Water_EnterHandler</c> passes a pointer to it into the
/// renderer's draw method as a callback. What <em>is</em> a shader is the reflection lookup, which is a
/// separate question.
/// </para>
/// <para>
/// The whole surface, then:
/// </para>
/// <code>
/// z(x, y, t) = Σ sin((x·dirX + y·dirY + phase)·spatialFreq + t·speed) · amplitude · attenuation
/// </code>
/// <para>
/// with <c>x</c> and <c>y</c> measured from the sheet's own centre — <c>Water_IsUnderWater</c>
/// (<c>0x6477D0</c>) subtracts the sheet's position before calling, and the height that comes back is
/// relative to the sheet too.
/// </para>
/// <para>
/// Two sheet-wide constants come out of <c>Water_UpdateInternalDataHandler</c>'s last three lines
/// (<c>0x6460B0</c>): the phase is <c>Size × 8192</c>, which is twice the sheet's radius in engine units
/// and is there to keep the sine's argument positive right across a sheet that spans ±radius; and the
/// attenuation divisor is <c>Fadeout / 4096</c>, so amplitude falls away as
/// <c>1 / max(1, metres from centre × Fadeout)</c> and the far edge of a large sheet lies flatter than
/// its middle.
/// </para>
/// <para>
/// <b>Not reproduced: the ripples.</b> <c>Water_VertexFunction</c> also walks a vector of transient
/// disturbances — a raised cosine of <c>(1 - cos((d - inner)·2π / 2·width)) × amplitude</c> inside an
/// annulus — which <c>Water_GunShotEffect</c> and the physics hit handler push into. Those are runtime
/// events rather than level data, and belong with whatever drives them.
/// </para>
/// </remarks>
public sealed class Igi2WaterWaveField
{
    /// <summary>How the sheet's <c>Size</c> becomes the phase added to every sample.</summary>
    /// <remarks><c>water[216] = Size * 8192</c> — twice the radius, in engine units.</remarks>
    public const float PhaseScale = 8192f;

    /// <summary>How the sheet's <c>Fadeout</c> becomes the attenuation divisor.</summary>
    /// <remarks><c>water[220] = Fadeout * 0.00024414062</c>, which is a division by 4,096.</remarks>
    public const float AttenuationScale = 1f / 4096f;

    /// <summary>What fraction of the mean wavelength the finite difference steps by.</summary>
    /// <remarks>
    /// <c>Water_SetVertexFunctionParameters</c> sets
    /// <c>sampleScale = 40.959999 / layers × Σ Frequency</c>, which is <c>40.96 ×</c> the mean
    /// wavelength in metres — and since a metre is 4,096 units, exactly <b>a hundredth of the mean
    /// wavelength</b>. A step tied to the waves it measures rather than a fixed epsilon, which is what
    /// keeps the normal meaningful across sheets whose wavelengths differ by two orders of magnitude.
    /// </remarks>
    public const float SampleScale = 40.959999f;

    private readonly Igi2WaterWaveTrain[] _trains;

    private Igi2WaterWaveField(Igi2WaterWaveTrain[] trains, float phase, float attenuation, float sampleStep)
    {
        _trains = trains;
        Phase = phase;
        AttenuationPerUnit = attenuation;
        SampleStep = sampleStep;
    }

    /// <summary>Gets the trains that are actually evaluated, at most four.</summary>
    public IReadOnlyList<Igi2WaterWaveTrain> Trains => _trains;

    /// <summary>Gets the phase added to every sample before the spatial frequency multiplies it.</summary>
    public float Phase { get; }

    /// <summary>Gets how fast amplitude falls away with distance from the sheet's centre.</summary>
    public float AttenuationPerUnit { get; }

    /// <summary>Gets how far apart the two samples the normal is differenced from are.</summary>
    public float SampleStep { get; }

    /// <summary>Gets a value indicating whether the sheet has any waves to evaluate.</summary>
    public bool IsFlat => _trains.Length == 0;

    /// <summary>Prepares a sheet's waves for evaluation.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <returns>The field.</returns>
    /// <remarks>
    /// Takes the first <see cref="Igi2WaterSurface.MaximumActiveLayers"/> trains and no more, because
    /// that is where <c>Water_SetVertexFunctionParameters</c> breaks out of its walk — a fifth
    /// <c>WaterLayer</c> is authored data the engine never reads, and it does not count toward the mean
    /// the sample step is taken from either.
    /// </remarks>
    public static Igi2WaterWaveField For(Igi2WaterSheet sheet)
    {
        int count = Math.Min(sheet.Waves.Count, Igi2WaterSurface.MaximumActiveLayers);
        Igi2WaterWaveTrain[] trains = new Igi2WaterWaveTrain[count];
        float wavelengthTotal = 0f;

        for (int i = 0; i < count; i++)
        {
            Igi2WaterWave wave = sheet.Waves[i];
            float radians = wave.Angle * MathF.PI / 180f;

            trains[i] = new Igi2WaterWaveTrain(
                MathF.Sin(radians),
                MathF.Cos(radians),
                MathF.Tau / (wave.Frequency * (float)WorldScene.UnitsPerMetre),
                wave.Amplitude * (float)WorldScene.UnitsPerMetre,
                wave.Speed);

            wavelengthTotal += wave.Frequency;
        }

        float step = count == 0 ? 0f : SampleScale / count * wavelengthTotal;

        // Size is authored in metres and the sheet already carries its radius in units, so the phase is
        // twice that radius rather than a second conversion.
        float phase = (float)(sheet.Radius * 2.0);

        return new Igi2WaterWaveField(trains, phase, sheet.FadeOut * AttenuationScale, step);
    }

    /// <summary>Gets the surface height at a point on the sheet.</summary>
    /// <param name="x">How far east of the sheet's centre, in engine units.</param>
    /// <param name="y">How far north of it.</param>
    /// <param name="seconds">The wave clock, from <see cref="Igi2WaterSurface.TimeFor"/>.</param>
    /// <param name="radius">
    /// How far the point is from the centre, which attenuates the waves. Zero leaves them at full
    /// height, which is what <c>Water_IsUnderWater</c> passes — it wants the surface, not a fade.
    /// </param>
    /// <returns>The height, in engine units, relative to the sheet's own plane.</returns>
    public float HeightAt(float x, float y, double seconds, float radius = 0f)
    {
        float attenuation = AttenuationAt(radius);
        float height = 0f;

        for (int i = 0; i < _trains.Length; i++)
        {
            Igi2WaterWaveTrain train = _trains[i];
            float along = (x * train.DirectionX) + (y * train.DirectionY) + Phase;
            float angle = (along * train.SpatialFrequency) + ((float)seconds * train.Speed);
            height += MathF.Sin(angle) * train.AmplitudeUnits * attenuation;
        }

        return height;
    }

    /// <summary>Gets how much a train's amplitude survives at one distance from the centre.</summary>
    /// <param name="radius">The distance, in engine units.</param>
    /// <returns>The multiplier, at most one.</returns>
    /// <remarks>
    /// <c>1 / max(1, radius × Fadeout / 4096)</c>. The clamp is upward, on the divisor, so inside the
    /// first metre — and whenever <c>Fadeout</c> is nought — the waves are at full height rather than
    /// unbounded.
    /// </remarks>
    public float AttenuationAt(float radius)
    {
        float divisor = radius * AttenuationPerUnit;
        return divisor < 1f ? 1f : 1f / divisor;
    }

    /// <summary>Gets the surface normal at a point on the sheet.</summary>
    /// <param name="x">How far east of the sheet's centre, in engine units.</param>
    /// <param name="y">How far north of it.</param>
    /// <param name="seconds">The wave clock.</param>
    /// <param name="radius">How far the point is from the centre.</param>
    /// <returns>The unit normal, pointing up out of the water.</returns>
    /// <remarks>
    /// A forward difference, not an analytic derivative, because that is what the original does: it
    /// samples the height at the point, at one <see cref="SampleStep"/> east and at one north, and takes
    /// <c>(-s·dzEast, -s·dzNorth, s²)</c> normalised — the cross product of the two tangents. The
    /// third component being positive is one more confirmation that Z is up.
    /// </remarks>
    public Vector3f NormalAt(float x, float y, double seconds, float radius = 0f)
    {
        if (IsFlat)
        {
            return Vector3f.UnitZ;
        }

        float here = HeightAt(x, y, seconds, radius);
        float east = HeightAt(x + SampleStep, y, seconds, radius) - here;
        float north = HeightAt(x, y + SampleStep, seconds, radius) - here;

        Vector3f normal = new(-(SampleStep * east), -(SampleStep * north), SampleStep * SampleStep);
        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));

        // The original guards the same way, and for the same reason: a degenerate step would divide by
        // nothing rather than merely give a poor normal.
        return length > 0f ? new Vector3f(normal.X / length, normal.Y / length, normal.Z / length) : Vector3f.UnitZ;
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 water, {_trains.Length} wave train(s)";
}
