using System.Text;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// Turns a sound's file path into the name the game refers to it by, and reproduces the two hashes the
/// original files it under.
/// </summary>
/// <remarks>
/// <para>
/// A script never names a path. It says <c>ak47_1</c>, and the engine finds whichever file was loaded
/// under that key from wherever it was mounted. <c>0x497390</c> is what produces the key: it finds the
/// <em>first</em> dot in the path, scans back from it to the nearest <c>/</c>, <c>\</c> or <c>:</c>, and
/// copies what lies between into a 32-byte buffer. So
/// <c>LOCAL:common/sounds/ak47_1.wav</c> becomes <c>ak47_1</c>.
/// </para>
/// <para>
/// The sounds are then stored in a 101-bucket hash table (<c>0x4955F0</c> builds it, <c>0x496250</c>
/// inserts, <c>0x496450</c> looks up) under two values computed by <c>0x497450</c> and
/// <c>0x497410</c>. Both upper-case as they go, so lookup is case-insensitive — and both do it
/// <em>in place</em>, mutating the caller's buffer, which is why the original's key buffer comes back
/// upper-cased.
/// </para>
/// <para>
/// The multiplier walk is the same family the AI graph's field names use (<c>0x4F9E70</c>): start at one,
/// add a constant each character, keep it to sixteen bits. The constant differs — 317 there and in
/// <see cref="Hash"/>, but <b>318</b> in <see cref="Bucket"/>, which is easy to read straight past.
/// </para>
/// </remarks>
public static class SoundName
{
    /// <summary>The number of buckets in the original's table.</summary>
    public const int BucketCount = 101;

    /// <summary>The size of the buffer the original copies a key into.</summary>
    public const int MaximumLength = 32;

    /// <summary>Extracts the lookup key from a sound's path.</summary>
    /// <param name="path">A path such as <c>LOCAL:common/sounds/ak47_1.wav</c>.</param>
    /// <returns>The key, upper-cased, or an empty string when the path has no key in it.</returns>
    /// <remarks>
    /// <para>
    /// Returns empty exactly where the original reports <c>"SOUND - Invalid filename %s"</c> and where
    /// it silently produces nothing: a path with no dot, and — a real quirk — a bare filename with no
    /// directory separator at all. The original's backward scan runs off the front of the string and
    /// returns without copying, leaving the key buffer as the empty string it was zeroed to. It never
    /// bites in practice because every path reaching it has come through the virtual file system and so
    /// carries at least a <c>LOCAL:</c>.
    /// </para>
    /// <para>
    /// The key is upper-cased here rather than at hash time. The original upper-cases inside both hash
    /// functions, mutating the buffer as a side effect; doing it once up front is the same answer
    /// without the surprise.
    /// </para>
    /// </remarks>
    public static string ToKey(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        int dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return string.Empty;
        }

        int start = -1;
        for (int i = dot - 1; i >= 0; i--)
        {
            if (path[i] is '/' or '\\' or ':')
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0 || start >= dot)
        {
            return string.Empty;
        }

        int length = Math.Min(dot - start, MaximumLength - 1);
        return path.Substring(start, length).ToUpperInvariant();
    }

    /// <summary>Computes the identity hash the original files a sound under.</summary>
    /// <param name="key">A key from <see cref="ToKey"/>.</param>
    /// <returns>The hash.</returns>
    /// <remarks>
    /// Transcribed from <c>0x497410</c>. The multiplier starts at one and gains 317 per character, kept
    /// to sixteen bits; each character contributes <c>multiplier * (signed char)c</c> to a sum that is
    /// allowed to overflow. Characters are read as <em>signed</em> bytes, so anything above 0x7F
    /// contributes negatively.
    /// </remarks>
    public static int Hash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        int sum = 0;
        int multiplier = 1;

        foreach (byte c in Encoding.Latin1.GetBytes(key.ToUpperInvariant()))
        {
            sum += multiplier * (sbyte)c;
            multiplier = (multiplier + 317) & 0xFFFF;
        }

        return sum;
    }

    /// <summary>Computes the bucket a sound lands in.</summary>
    /// <param name="key">A key from <see cref="ToKey"/>.</param>
    /// <returns>The bucket index, 0 to <see cref="BucketCount"/> − 1.</returns>
    /// <remarks>
    /// Transcribed from <c>0x497450</c>, which differs from <see cref="Hash"/> in three ways that all
    /// matter: the multiplier gains <b>318</b> rather than 317, the modulo is taken at every character
    /// rather than once at the end, and the character is read <em>unsigned</em>.
    /// </remarks>
    public static int Bucket(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        uint accumulator = 0;
        int multiplier = 1;

        foreach (byte c in Encoding.Latin1.GetBytes(key.ToUpperInvariant()))
        {
            accumulator = (uint)(((multiplier * c) + accumulator) % BucketCount);
            multiplier = (multiplier + 318) & 0xFFFF;
        }

        return (int)accumulator;
    }
}
