using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Iff;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// A pack opened and bound: its <c>.mtp</c> tables, its models keyed by name, and the archives its
/// textures live in.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>Pack_Open</c> (<c>0x4CEFA0</c>) followed by <c>Pack_Bind</c> (<c>0x4CF080</c>): load
/// <c>&lt;dir&gt;textures/&lt;name&gt;.res</c> and <c>&lt;dir&gt;models/&lt;name&gt;.res</c> into the
/// resource registry, then walk the pack's <c>INST</c> records resolving each instance's model and
/// per-slot textures.
/// </para>
/// <para>
/// Models are keyed by the bare name because that is the id space <c>MODS</c> defines, and the archive's
/// own entry order is <em>not</em> that order. The original leans on the resource registry for the same
/// effect: exploding <c>models/&lt;pack&gt;.res</c> registers each model under
/// <c>LOCAL:models/&lt;name&gt;.mef</c>, which is exactly the key <c>Model_Load</c> then asks for.
/// </para>
/// </remarks>
public sealed class LoadedPack
{
    private const string LogCategory = "Pack";

    /// <summary>The directory a pack's <c>BANM</c> animation sets load from.</summary>
    /// <remarks>
    /// Not the pack's own directory: <c>0x4CF910</c> prefers a second root whenever it is non-empty,
    /// and <c>Mission_Load</c> pins that root to the literal <c>"LOCAL:common/"</c> (<c>0x415C92</c>)
    /// before either pack opens. The shipped sets are at <c>common/anims/</c>; the pack directories
    /// hold no <c>anims/</c> at all.
    /// </remarks>
    public const string AnimationSetRoot = "LOCAL:common/";

    private readonly Dictionary<string, MefModel> _models;
    private HashSet<string>? _registered;
    private static readonly ConditionalWeakTable<ResourceRegistry, TextureArchiveBinding> TextureArchives = new();

    private sealed class TextureArchiveBinding
    {
        public string? Path { get; set; }
    }

    private LoadedPack(
        string name,
        string directory,
        MtpPack pack,
        Dictionary<string, MefModel> models,
        IReadOnlyList<string> missingModels,
        IReadOnlyList<BoneAnimationSet> animationSets)
    {
        Name = name;
        Directory = directory;
        Pack = pack;
        _models = models;
        MissingModels = missingModels;
        AnimationSets = animationSets;
    }

    /// <summary>Gets the pack name, for example <c>level1</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the directory the pack was opened from, with its trailing slash.</summary>
    public string Directory { get; }

    /// <summary>Gets the decoded <c>.mtp</c> tables.</summary>
    public MtpPack Pack { get; }

    /// <summary>Gets the names listed in <c>MODS</c> that the models archive does not hold.</summary>
    /// <remarks>Empty for every retail pack; a mod that trims its archive would populate it.</remarks>
    public IReadOnlyList<string> MissingModels { get; }

    /// <summary>Gets the instances the pack places, in <c>VNAM</c> order.</summary>
    public IReadOnlyList<MtpInstance> Instances => Pack.Instances;

    /// <summary>Gets the animation sets the pack's <c>BANM</c> chunk names, in declaration order.</summary>
    /// <remarks>
    /// <c>location0</c> names the six shipped sets; retail level packs name none. Each set carries the
    /// skeleton id out of its own <c>BOSH</c> chunk, which is the id a soldier's <c>Bone Heirachy</c>
    /// parameter indexes — see the original.
    /// </remarks>
    public IReadOnlyList<BoneAnimationSet> AnimationSets { get; }

    /// <summary>Opens and binds a pack.</summary>
    /// <param name="directory">The resource directory the pack lives in, for example <c>LOCAL:missions/location0/level1/</c>.</param>
    /// <param name="name">The pack name, for example <c>level1</c>.</param>
    /// <param name="resources">Where the pack's files are read from.</param>
    /// <param name="logger">The logger for load diagnostics.</param>
    /// <returns>The bound pack.</returns>
    /// <exception cref="ArgumentException">A required argument is null or empty.</exception>
    /// <exception cref="EngineFatalException">The pack or one of its archives is missing or malformed.</exception>
    public static LoadedPack Open(string directory, string name, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(resources);

        IIgiLogger log = logger ?? NullLogger.Instance;
        string dir = directory.EndsWith('/') ? directory : directory + "/";

        MtpPack pack = ReadPack($"{dir}{name}.mtp", resources);

        // The animation sets come first because Pack_Bind (0x4CF080) loads them first, before either
        // archive binds - and from the shared common root, not the pack's own directory.
        List<BoneAnimationSet> sets = [];
        foreach (string setName in pack.AnimationSetNames)
        {
            string path = $"{AnimationSetRoot}anims/{setName}.iff";
            if (!resources.TryLoad(path, out ReadOnlyMemory<byte> bytes))
            {
                log.Warning(LogCategory, $"Animation set '{path}' is absent.");
                continue;
            }

            try
            {
                sets.Add(BoneAnimationSet.Read(bytes));
            }
            catch (IffFormatException ex)
            {
                log.Warning(LogCategory, $"Animation set '{path}' did not parse: {ex.Message}");
            }
        }

        Dictionary<string, MefModel> models = new(StringComparer.OrdinalIgnoreCase);
        if (TryRegisterArchive($"{dir}models/{name}.res", resources, log) is { } archive)
        {
            foreach (IresEntry entry in archive.Entries)
            {
                string key = BareName(entry.Name);
                try
                {
                    models[key] = MefModel.Read(entry.Data);
                }
                catch (Exception ex) when (ex is PackFormatException or IlffFormatException)
                {
                    log.Warning(LogCategory, $"Model '{entry.Name}' in '{name}' did not parse: {ex.Message}");
                }
            }
        }

        List<string> missing = [];
        foreach (string model in pack.ModelNames)
        {
            if (!models.ContainsKey(model))
            {
                missing.Add(model);
            }
        }

        if (missing.Count > 0)
        {
            log.Warning(LogCategory, $"Pack '{name}' lists {missing.Count} model(s) its archive does not hold.");
        }

        // The textures archive is registered but not decoded here: a texture becomes pixels only when
        // the renderer asks for it, and the original defers the same way.
        // Texture names are global registry keys, but level packs may reuse a name with different
        // pixels (notably 015_05_1 on the human face). Rebind the current pack's texture namespace so
        // a previous mission preview cannot leave stale pixels in the running mission. The previous
        // archive is freed only after the replacement has been validated and registered successfully.
        string textureArchivePath = $"{dir}textures/{name}.res";
        TextureArchiveBinding textureBinding = TextureArchives.GetOrCreateValue(resources);
        string? previousTextureArchivePath = textureBinding.Path;
        _ = TryRegisterArchive(textureArchivePath, resources, log, replaceExisting: true);
        if (previousTextureArchivePath is { }
            && !previousTextureArchivePath.Equals(textureArchivePath, StringComparison.OrdinalIgnoreCase))
        {
            resources.Free(previousTextureArchivePath);
        }

        textureBinding.Path = textureArchivePath;

        log.Information(
            LogCategory,
            $"Opened pack '{name}': {models.Count} model(s), {pack.TextureNames.Count} texture(s), {pack.Instances.Count} instance(s).");

        return new LoadedPack(
            name,
            dir,
            pack,
            models,
            new ReadOnlyCollection<string>(missing),
            new ReadOnlyCollection<BoneAnimationSet>(sets));
    }

    /// <summary>Resolves an instance's model.</summary>
    /// <param name="instance">The instance.</param>
    /// <param name="model">Receives the model when the pack holds it.</param>
    /// <returns><see langword="true"/> when the model resolved.</returns>
    /// <remarks>
    /// Only level-local model ids resolve here. No retail pack reaches into the common id space, and a
    /// caller that needs to has both packs to hand and can dispatch on <see cref="PackId.IsCommon"/>.
    /// </remarks>
    public bool TryGetModel(MtpInstance instance, out MefModel? model)
    {
        model = null;
        if (instance.Model.IsNone || instance.Model.IsCommon || instance.Model.Index >= Pack.ModelNames.Count)
        {
            return false;
        }

        return _models.TryGetValue(Pack.ModelNames[instance.Model.Index], out model);
    }

    /// <summary>Resolves a model by its bare name.</summary>
    /// <param name="name">The model name, for example <c>210_01_1</c>.</param>
    /// <param name="model">Receives the model when the pack holds it.</param>
    /// <returns><see langword="true"/> when the model resolved.</returns>
    public bool TryGetModel(string name, out MefModel? model)
    {
        // A name resolves only if the pack registered it. The original searches a per-pack list built from
        // MODS (0x4CEE30), and a models archive can hold entries the pack never names — which matters
        // because a LOD chain ends at the first name that does not resolve, so an unregistered stray would
        // lengthen the chain and shift every baked lightmap index after it.
        _registered ??= new HashSet<string>(Pack.ModelNames, StringComparer.OrdinalIgnoreCase);
        model = null;
        return _registered.Contains(name) && _models.TryGetValue(name, out model);
    }

    /// <summary>Resolves the diffuse texture a draw record samples.</summary>
    /// <param name="instance">The instance being drawn.</param>
    /// <param name="materialSlot">The draw record's material slot.</param>
    /// <param name="textureName">Receives the texture's bare name.</param>
    /// <returns><see langword="false"/> when the record draws untextured.</returns>
    public bool TryGetTextureName(MtpInstance instance, int materialSlot, out string textureName)
    {
        textureName = string.Empty;
        if (!Pack.TryGetMaterial(instance, materialSlot, out MtpMaterial material)
            || material.Texture.IsNone
            || material.Texture.IsCommon
            || material.Texture.Index >= Pack.TextureNames.Count)
        {
            return false;
        }

        textureName = Pack.TextureNames[material.Texture.Index];
        return true;
    }

    /// <summary>Builds the resource name a texture's pixels are registered under.</summary>
    /// <param name="textureName">The bare texture name.</param>
    /// <returns>The resource name, matching the key the textures archive registers.</returns>
    /// <remarks>The original formats exactly this in <c>Pack_LoadTextures</c> (<c>0x4CF790</c>).</remarks>
    public static string TextureResourceName(string textureName) => $"LOCAL:textures/{textureName}.tex";

    /// <inheritdoc/>
    public override string ToString() => $"pack '{Name}' ({_models.Count} models, {Instances.Count} instances)";

    private static string BareName(string resourceName)
    {
        ReadOnlySpan<char> span = resourceName.Replace('\\', '/');
        int slash = span.LastIndexOf('/');
        if (slash >= 0)
        {
            span = span[(slash + 1)..];
        }

        int dot = span.LastIndexOf('.');
        return new string(dot > 0 ? span[..dot] : span);
    }

    private static MtpPack ReadPack(string path, ResourceRegistry resources)
    {
        if (!resources.TryLoad(path, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException($"Pack_Open(): couldn't load '{path}'.");
        }

        try
        {
            return MtpPack.Read(bytes);
        }
        catch (PackFormatException ex)
        {
            throw new EngineFatalException($"Pack_Open(): '{path}' is malformed: {ex.Message}", ex);
        }
    }

    private static IresArchive? TryRegisterArchive(
        string path,
        ResourceRegistry resources,
        IIgiLogger logger,
        bool replaceExisting = false)
    {
        if (!resources.TryLoad(path, out ReadOnlyMemory<byte> bytes))
        {
            logger.Warning(LogCategory, $"Archive '{path}' is absent.");
            return null;
        }

        try
        {
            IresArchive archive = IresArchive.Read(bytes);
            if (replaceExisting)
            {
                resources.Free(path);
                resources.Register(path, bytes, replaceExisting: true);
            }

            return archive;
        }
        catch (IlffFormatException ex)
        {
            throw new EngineFatalException($"Pack_Bind(): '{path}' is malformed: {ex.Message}", ex);
        }
    }
}
