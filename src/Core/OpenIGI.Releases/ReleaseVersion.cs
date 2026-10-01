using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OpenIGI.Releases;

/// <summary>A semantic version as releases are tagged: <c>0.2.0</c>, or <c>0.2.0-beta.3</c> for a beta.</summary>
/// <remarks>
/// Ordered by semantic-versioning precedence, with numeric pre-release identifiers compared as numbers, so
/// <c>0.2.0-beta.10</c> follows <c>0.2.0-beta.9</c> and every beta of 0.2.0 precedes 0.2.0 itself. Build
/// metadata (<c>+abc1234</c>, which the SDK appends to an informational version) is accepted and ignored
/// for ordering and equality, because two builds of one release are the same release.
/// </remarks>
public readonly struct ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private readonly string[]? _prerelease;

    private ReleaseVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
    }

    /// <summary>Gets the major version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version.</summary>
    public int Patch { get; }

    /// <summary>Gets the dot-separated pre-release identifiers; empty for a stable release.</summary>
    public IReadOnlyList<string> Prerelease => _prerelease ?? [];

    /// <summary>Gets whether this is a pre-release (a beta).</summary>
    public bool IsPrerelease => Prerelease.Count > 0;

    /// <summary>Parses a version, with or without a leading <c>v</c>.</summary>
    /// <param name="text">The version text.</param>
    /// <returns>The version.</returns>
    /// <exception cref="FormatException">The text is not a semantic version.</exception>
    public static ReleaseVersion Parse(string text) =>
        TryParse(text, out ReleaseVersion version)
            ? version
            : throw new FormatException($"'{text}' is not a semantic version.");

    /// <summary>Tries to parse a version, with or without a leading <c>v</c>.</summary>
    /// <param name="text">The version text.</param>
    /// <param name="version">The version, when the text is one.</param>
    /// <returns>Whether the text is a semantic version.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        int plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        string[] prerelease = [];
        int dash = span.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = span[(dash + 1)..].ToString().Split('.');
            if (prerelease.Any(static part => part.Length == 0))
            {
                return false;
            }

            span = span[..dash];
        }

        string[] core = span.ToString().Split('.');
        if (core.Length != 3
            || !TryNumber(core[0], out int major)
            || !TryNumber(core[1], out int minor)
            || !TryNumber(core[2], out int patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <inheritdoc/>
    public int CompareTo(ReleaseVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        return result != 0 ? result : ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <inheritdoc/>
    public bool Equals(ReleaseVersion other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);
        foreach (string part in Prerelease)
        {
            hash.Add(part, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Formats the version without a leading <c>v</c>, as <c>0.2.0-beta.3</c>.</summary>
    /// <returns>The version text.</returns>
    public override string ToString()
    {
        string core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return IsPrerelease ? $"{core}-{string.Join('.', Prerelease)}" : core;
    }

    /// <summary>Compares two versions by precedence.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Whether <paramref name="left"/> precedes <paramref name="right"/>.</returns>
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    /// <summary>Compares two versions by precedence.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Whether <paramref name="left"/> follows <paramref name="right"/>.</returns>
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    /// <summary>Compares two versions by precedence.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Whether <paramref name="left"/> precedes or equals <paramref name="right"/>.</returns>
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two versions by precedence.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Whether <paramref name="left"/> follows or equals <paramref name="right"/>.</returns>
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Compares two versions for equal precedence.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Whether the two are the same release.</returns>
    public static bool operator ==(ReleaseVersion left, ReleaseVersion right) => left.Equals(right);

    /// <summary>Compares two versions for different precedence.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>Whether the two are different releases.</returns>
    public static bool operator !=(ReleaseVersion left, ReleaseVersion right) => !left.Equals(right);

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    // A release outranks any of its pre-releases; identifiers then compare pairwise, numbers numerically
    // and below any word, and a longer list wins a tie (beta.1.1 follows beta.1).
    private static int ComparePrerelease(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return right.Count.CompareTo(left.Count);
        }

        for (int i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            bool leftNumber = TryNumber(left[i], out int leftValue);
            bool rightNumber = TryNumber(right[i], out int rightValue);
            int result = (leftNumber, rightNumber) switch
            {
                (true, true) => leftValue.CompareTo(rightValue),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return left.Count.CompareTo(right.Count);
    }
}
