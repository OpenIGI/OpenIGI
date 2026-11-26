using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Replays;

/// <summary>
/// A running 64-bit fingerprint of where a level's simulation stands, folded value by value in a fixed order: two runs that
/// agree to the bit fold to the same number, and one bit apart anywhere folds to another.
/// </summary>
/// <remarks>
/// Floating-point values are folded by their bits, not their values, so <c>0.0</c> and <c>-0.0</c> differ and a NaN is
/// whatever NaN it is: the point is to notice any difference at all, not to judge one. FNV-1a over 64-bit words, mixed once
/// more at the end so that neighbouring states do not give neighbouring numbers.
/// </remarks>
public struct SimulationHash
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _value;
    private bool _started;

    /// <summary>Gets the fingerprint of everything folded so far.</summary>
    public readonly ulong Value
    {
        get
        {
            // The final avalanche (MurmurHash3's fmix64).
            ulong h = _started ? _value : Offset;
            h ^= h >> 33;
            h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33;
            h *= 0xc4ceb9fe1a85ec53UL;
            h ^= h >> 33;
            return h;
        }
    }

    /// <summary>Folds a 64-bit word.</summary>
    /// <param name="value">The word.</param>
    public void Add(ulong value)
    {
        if (!_started)
        {
            _value = Offset;
            _started = true;
        }

        for (int shift = 0; shift < 64; shift += 8)
        {
            _value ^= (value >> shift) & 0xff;
            _value *= Prime;
        }
    }

    /// <summary>Folds a signed 64-bit integer.</summary>
    /// <param name="value">The integer.</param>
    public void Add(long value) => Add(unchecked((ulong)value));

    /// <summary>Folds a 32-bit integer.</summary>
    /// <param name="value">The integer.</param>
    public void Add(int value) => Add(unchecked((ulong)(uint)value));

    /// <summary>Folds an unsigned 32-bit integer.</summary>
    /// <param name="value">The integer.</param>
    public void Add(uint value) => Add((ulong)value);

    /// <summary>Folds a truth.</summary>
    /// <param name="value">The truth.</param>
    public void Add(bool value) => Add(value ? 1UL : 0UL);

    /// <summary>Folds a float by its bits.</summary>
    /// <param name="value">The float.</param>
    public void Add(float value) => Add(unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    /// <summary>Folds a double by its bits.</summary>
    /// <param name="value">The double.</param>
    public void Add(double value) => Add(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    /// <summary>Folds a point, its three coordinates in order.</summary>
    /// <param name="value">The point.</param>
    public void Add(Vector3d value)
    {
        Add(value.X);
        Add(value.Y);
        Add(value.Z);
    }

    /// <summary>Folds a text, its length and then each character.</summary>
    /// <param name="value">The text; <see langword="null"/> folds as an empty one.</param>
    public void Add(string? value)
    {
        string text = value ?? string.Empty;
        Add(text.Length);
        foreach (char c in text)
        {
            Add((uint)c);
        }
    }

    /// <summary>Folds a random stream's state: where it stands, so every draw it will make.</summary>
    /// <param name="random">The stream, or <see langword="null"/> for none.</param>
    public void Add(IgiRandom? random)
    {
        Add(random is not null);
        if (random is not null)
        {
            (uint a, uint b, uint c) = random.State;
            Add(a);
            Add(b);
            Add(c);
        }
    }
}
