namespace OpenIGI.Net;

/// <summary>Maps floats onto a fixed number of bits over a range and back.</summary>
/// <remarks>
/// Both ends of the range are exactly representable, and a value always comes back within half a step of what was sent.
/// The sender quantizes its own copy too wherever the receiver's copy must match it bit for bit (prediction compares
/// against what the server sent, not what it simulated).
/// </remarks>
public static class Quantize
{
    /// <summary>Gets the bits a value quantizes to.</summary>
    /// <param name="value">The value, clamped into the range; a NaN reads as the low end.</param>
    /// <param name="min">The range's low end.</param>
    /// <param name="max">The range's high end.</param>
    /// <param name="bits">How many bits, 1 to 32.</param>
    /// <returns>The step index.</returns>
    public static uint ToBits(float value, float min, float max, int bits)
    {
        double steps = MaxSteps(bits);
        double t = (Math.Clamp(float.IsNaN(value) ? min : value, min, max) - (double)min) / ((double)max - min);
        return (uint)Math.Round(t * steps, MidpointRounding.AwayFromZero);
    }

    /// <summary>Gets the value a step index stands for.</summary>
    /// <param name="quantized">The step index.</param>
    /// <param name="min">The range's low end.</param>
    /// <param name="max">The range's high end.</param>
    /// <param name="bits">How many bits, 1 to 32.</param>
    /// <returns>The value.</returns>
    public static float FromBits(uint quantized, float min, float max, int bits) =>
        (float)(min + (((double)max - min) * Math.Min(quantized, MaxSteps(bits)) / MaxSteps(bits)));

    /// <summary>Rounds a value to what it would read back as.</summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The range's low end.</param>
    /// <param name="max">The range's high end.</param>
    /// <param name="bits">How many bits, 1 to 32.</param>
    /// <returns>The value the receiver sees.</returns>
    public static float RoundTrip(float value, float min, float max, int bits) =>
        FromBits(ToBits(value, min, max, bits), min, max, bits);

    /// <summary>Gets a 16-bit angle code for radians, a whole turn over the full range.</summary>
    /// <param name="radians">The angle, any number of turns.</param>
    /// <returns>The code.</returns>
    public static ushort AngleToBits(float radians)
    {
        double turns = radians / (2.0 * Math.PI);
        turns -= Math.Floor(turns);
        return (ushort)((uint)Math.Round(turns * 65536.0) & 0xFFFF);
    }

    /// <summary>Gets the radians a 16-bit angle code stands for, from -pi to just under pi.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The angle.</returns>
    public static float AngleFromBits(ushort code) => (float)((short)code * (Math.PI / 32768.0));

    private static double MaxSteps(int bits) => bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
}
