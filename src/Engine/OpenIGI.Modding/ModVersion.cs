using System.Globalization;

namespace OpenIGI.Modding;

/// <summary>Mods' versions, compared the way their authors number them: dotted numbers, the first that differs deciding.</summary>
/// <remarks>
/// A missing part is nought, so <c>1.2</c> is <c>1.2.0</c>. A part that is not a number (<c>1.2-beta</c>'s <c>2-beta</c>)
/// is compared by its leading digits and then by its text, so a pre-release sorts after its number's release only by text;
/// mods that need more than that say so in their own numbering.
/// </remarks>
public static class ModVersion
{
    /// <summary>Compares two versions.</summary>
    /// <param name="left">One version.</param>
    /// <param name="right">The other.</param>
    /// <returns>Less than nought when <paramref name="left"/> is older, nought when equal, more when newer.</returns>
    public static int Compare(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        string[] a = left.Trim().Split('.');
        string[] b = right.Trim().Split('.');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string x = i < a.Length ? a[i] : "0";
            string y = i < b.Length ? b[i] : "0";
            int order = LeadingNumber(x).CompareTo(LeadingNumber(y));
            if (order == 0)
            {
                order = string.CompareOrdinal(x, y);
            }

            if (order != 0)
            {
                return Math.Sign(order);
            }
        }

        return 0;
    }

    /// <summary>Gets whether a version meets a mod's requirement of it.</summary>
    /// <param name="version">The version installed.</param>
    /// <param name="requirement">The oldest version that will do, as <c>1.2</c> or <c>&gt;=1.2</c>; empty or <c>*</c> for any.</param>
    /// <returns>Whether it does.</returns>
    public static bool Satisfies(string version, string? requirement)
    {
        ArgumentNullException.ThrowIfNull(version);
        string wanted = (requirement ?? string.Empty).Trim();
        if (wanted.StartsWith(">=", StringComparison.Ordinal))
        {
            wanted = wanted[2..].Trim();
        }

        return wanted is "" or "*" || Compare(version, wanted) >= 0;
    }

    private static long LeadingNumber(string part)
    {
        int digits = 0;
        while (digits < part.Length && digits < 18 && char.IsAsciiDigit(part[digits]))
        {
            digits++;
        }

        return digits == 0 ? 0 : long.Parse(part.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
