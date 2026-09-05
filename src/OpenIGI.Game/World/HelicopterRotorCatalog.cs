namespace OpenIGI.Game.World;

/// <summary>Retail helicopter rotor model names recovered from the shipped heli QVM definitions.</summary>
/// <remarks>
/// The editor parity branch uses these names as a safe fallback when an ATTA child is not classified by
/// the magic-object table. OpenIGI normally gets the same classification from <see cref="MagicObjectRegistry"/>,
/// but keeping the verified model mapping here prevents a missing or incomplete table from freezing a rotor
/// as an ordinary rigid attachment. LOD variants share the same model family and are accepted as well.
/// </remarks>
public static class HelicopterRotorCatalog
{
    private static readonly string[] KnownRotorBases =
    [
        "711_01", "711_02",             // Bell
        "700_02", "700_03", "700_04",  // Mil
    ];

    /// <summary>Returns whether <paramref name="modelName"/> is a known retail helicopter rotor model.</summary>
    public static bool IsKnownRotorModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return false;
        }

        int separator = modelName.LastIndexOf('_');
        if (separator <= 0 || separator == modelName.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> lod = modelName.AsSpan(separator + 1);
        for (int i = 0; i < lod.Length; i++)
        {
            if (!char.IsAsciiDigit(lod[i]))
            {
                return false;
            }
        }

        string baseName = modelName[..separator];
        foreach (string knownBase in KnownRotorBases)
        {
            if (string.Equals(baseName, knownBase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
