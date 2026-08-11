using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenIGI.Game.World;

/// <summary>
/// Provides normalization of model IDs (e.g. converting "001011" to "001_01_1")
/// and lookup of friendly model names from the IGIModels.json catalog.
/// </summary>
public static partial class ModelNameRegistry
{
    private static readonly Dictionary<string, string> ModelIdToName = LoadCatalog();

    [GeneratedRegex(@"^(\d{3})(\d{2})(\d{1})(.*)$")]
    private static partial Regex RawModelIdRegex();

    /// <summary>
    /// Normalizes a model ID by inserting underscores into unformatted 6-digit IDs (e.g., "001011" -> "001_01_1").
    /// </summary>
    /// <param name="rawId">The raw model ID or placement instance name.</param>
    /// <returns>The normalized model ID in GGG_VV_L format.</returns>
    public static string FormatModelId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return rawId ?? string.Empty;
        }

        // If it already contains underscores (e.g. "001_01_1"), keep it as is
        if (rawId.Contains('_'))
        {
            return rawId;
        }

        Match match = RawModelIdRegex().Match(rawId);
        if (match.Success)
        {
            string group = match.Groups[1].Value;
            string variant = match.Groups[2].Value;
            string lod = match.Groups[3].Value;
            string suffix = match.Groups[4].Value;
            return $"{group}_{variant}_{lod}{suffix}";
        }

        return rawId;
    }

    /// <summary>
    /// Attempts to find the friendly model name for a model ID from the catalog.
    /// </summary>
    /// <param name="modelId">The model ID (raw or normalized).</param>
    /// <param name="modelName">Receives the friendly model name if found.</param>
    /// <returns><see langword="true"/> if a matching name was found in the catalog.</returns>
    public static bool TryGetModelName(string modelId, out string modelName)
    {
        string normalized = FormatModelId(modelId);
        return ModelIdToName.TryGetValue(normalized, out modelName!);
    }

    /// <summary>
    /// Formats a model label by normalizing its ID and appending the friendly model name if available.
    /// </summary>
    /// <param name="rawId">The raw instance name or model ID.</param>
    /// <returns>Formatted string, e.g. "001_01_1 (AITYPE_SNIPER)".</returns>
    public static string DescribeModel(string rawId)
    {
        string normalized = FormatModelId(rawId);
        if (TryGetModelName(normalized, out string name))
        {
            return $"{normalized} ({name})";
        }

        return normalized;
    }

    private static Dictionary<string, string> LoadCatalog()
    {
        Dictionary<string, string> catalog = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            Assembly assembly = typeof(ModelNameRegistry).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream("OpenIGI.Game.Resources.IGIModels.json");
            if (stream is null)
            {
                return catalog;
            }

            using JsonDocument doc = JsonDocument.Parse(stream);
            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("ModelId", out JsonElement idProp) &&
                    element.TryGetProperty("ModelName", out JsonElement nameProp))
                {
                    string? id = idProp.GetString();
                    string? name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        catalog[id] = name;
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully if catalog loading fails
        }

        return catalog;
    }
}
