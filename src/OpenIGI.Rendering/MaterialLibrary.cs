using System.Text.Json;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering;

/// <summary>
/// What each of a level's textures is made of, as far as the renderer is concerned.
/// </summary>
/// <remarks>
/// <para>
/// <b>The obvious design does not work here, and it is worth saying why.</b> The usual way to give an
/// old game surface materials is to match on texture names — <c>snow*</c> is a rough dielectric,
/// <c>metal*</c> is a conductor, and so on. I.G.I.'s textures are not named that way: they are numbered.
/// Level 1 alone draws <c>203_04_1</c>, <c>216_01_1_argb8888</c>, <c>219_02_1</c> and a hundred more
/// like them, and no rule over those strings can know which is glass.
/// </para>
/// <para>
/// So there are two things to match on instead. The first is the one piece of meaning the names do
/// carry: the <c>_argb8888</c> suffix marks the textures the packs store with an alpha channel, which
/// in this game is the glass, the wire fences and the foliage — everything cut out or seen through.
/// The second is the texture's own number, listed explicitly. That is laborious, and it is also the
/// only honest way: deciding that <c>216_03_1</c> is polished metal means looking at it.
/// </para>
/// <para>
/// Data rather than code, and loaded the same way the fog presets are, so a surface can be re-judged
/// by editing a file rather than by rebuilding.
/// </para>
/// </remarks>
public sealed class MaterialLibrary
{
    private const string LogCategory = "Material";

    private readonly Dictionary<string, SurfaceMaterial> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Suffix, SurfaceMaterial Material)> _bySuffix = [];
    private readonly Dictionary<string, SurfaceMaterial> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly SurfaceMaterial _default;

    private MaterialLibrary(SurfaceMaterial defaultMaterial) => _default = defaultMaterial;

    /// <summary>Gets a library in which every surface is the default material.</summary>
    public static MaterialLibrary Empty { get; } = new(SurfaceMaterial.Default);

    /// <summary>Gets the material a texture with no rule of its own takes.</summary>
    public SurfaceMaterial Default => _default;

    /// <summary>Gets how many textures are named explicitly.</summary>
    public int NamedCount => _byName.Count;

    /// <summary>Gets how many suffix rules were read.</summary>
    public int SuffixCount => _bySuffix.Count;

    /// <summary>Reads a material file.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="logger">Where to report a file that could not be read.</param>
    /// <returns>The library, or <see cref="Empty"/> when the file is missing or malformed.</returns>
    /// <remarks>
    /// A broken file never stops the game: every surface falls back to the default, which is the
    /// behaviour from before this file existed.
    /// </remarks>
    public static MaterialLibrary Load(string path, IIgiLogger? logger = null)
    {
        IIgiLogger log = logger ?? NullLogger.Instance;

        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

            JsonElement root = document.RootElement;
            MaterialLibrary library = new(
                root.TryGetProperty("default", out JsonElement fallback)
                    ? ReadMaterial(fallback, SurfaceMaterial.Default)
                    : SurfaceMaterial.Default);

            if (root.TryGetProperty("textures", out JsonElement textures)
                && textures.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in textures.EnumerateObject())
                {
                    if (entry.Name.StartsWith('*'))
                    {
                        library._bySuffix.Add(
                            (entry.Name[1..], ReadMaterial(entry.Value, library._default)));
                    }
                    else
                    {
                        library._byName[entry.Name] = ReadMaterial(entry.Value, library._default);
                    }
                }
            }

            log.Information(
                LogCategory,
                $"Materials: {library._byName.Count} texture(s), {library._bySuffix.Count} suffix rule(s).");

            return library;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warning(LogCategory, $"Could not read {path}: {ex.Message}. Every surface takes the default.");
            return Empty;
        }
    }

    /// <summary>Gets the material a texture is made of.</summary>
    /// <param name="textureName">The texture's name, as the pack records it.</param>
    /// <returns>Its material, or the default when nothing matches.</returns>
    /// <remarks>
    /// Cached per name, because this is asked once per sub-mesh per frame and a level draws several
    /// hundred of them — and the answer, unlike almost everything else in a frame, never changes.
    /// </remarks>
    public SurfaceMaterial For(string? textureName)
    {
        if (string.IsNullOrEmpty(textureName))
        {
            return _default;
        }

        if (_resolved.TryGetValue(textureName, out SurfaceMaterial cached))
        {
            return cached;
        }

        SurfaceMaterial material = Resolve(textureName);
        _resolved[textureName] = material;
        return material;
    }

    private SurfaceMaterial Resolve(string textureName)
    {
        // An explicit name beats a suffix rule: the suffix says what a whole family is usually made
        // of, and the name is someone having looked at this one.
        if (_byName.TryGetValue(textureName, out SurfaceMaterial named))
        {
            return named;
        }

        foreach ((string suffix, SurfaceMaterial material) in _bySuffix)
        {
            if (textureName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return material;
            }
        }

        return _default;
    }

    private static SurfaceMaterial ReadMaterial(JsonElement element, in SurfaceMaterial fallback)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        return new SurfaceMaterial(
            Number(element, "roughness") ?? fallback.Roughness,
            Number(element, "metallic") ?? fallback.Metallic,
            Number(element, "emissive") ?? fallback.Emissive,
            Flag(element, "castsOcclusion") ?? fallback.CastsOcclusion);
    }

    private static bool? Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static float? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? (float)value.GetDouble()
            : null;
}
