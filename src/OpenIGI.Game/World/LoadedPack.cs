using System.Collections.ObjectModel;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Iff;
using OpenIGI.Formats;
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
    private Dictionary<string, MtpInstance>? _byModelName;

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
    /// <param name="profile">
    /// The title's format policy, which says which mesh versions its models may be. Defaults to Project
    /// I.G.I.'s, which is what every existing caller wants; I.G.I. 2 ships mesh version 0.16 and is
    /// refused outright without this, so a pack that opened with no models was the symptom.
    /// </param>
    /// <returns>The bound pack.</returns>
    /// <exception cref="ArgumentException">A required argument is null or empty.</exception>
    /// <exception cref="EngineFatalException">The pack or one of its archives is missing or malformed.</exception>
    public static LoadedPack Open(
        string directory,
        string name,
        ResourceRegistry resources,
        IIgiLogger? logger = null,
        AssetFormatProfile? profile = null)
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
        int absentSets = 0;
        foreach (string setName in pack.AnimationSetNames)
        {
            string path = $"{AnimationSetRoot}anims/{setName}.iff";
            if (!resources.TryLoad(path, out ReadOnlyMemory<byte> bytes))
            {
                absentSets++;
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

        // Counted, not listed. A pack names hundreds of sets, and a title that keeps them somewhere
        // else is missing all of them at once -- I.G.I. 2 ships no loose .iff anywhere, its animation
        // data being inside the packs -- so a line each buried every other diagnostic in the run under
        // a thousand identical ones. A set that IS there and will not parse still gets its own line,
        // because that is one file being wrong rather than a title keeping them elsewhere.
        if (absentSets > 0)
        {
            log.Warning(
                LogCategory,
                $"{absentSets} of {pack.AnimationSetNames.Count} animation set(s) named by '{name}' are not under "
                    + $"{AnimationSetRoot}anims/; this title keeps them elsewhere.");
        }

        Dictionary<string, MefModel> models = new(StringComparer.OrdinalIgnoreCase);
        int animations = 0;
        int shadows = 0;
        if (TryRegisterArchive($"{dir}models/{name}.res", resources, log) is { } archive)
        {
            foreach (IresEntry entry in archive.Entries)
            {
                string key = BareName(entry.Name);
                // An animation in the models archive is not a broken model. The sequel keeps its bone
                // animations there rather than beside the pack as loose .iff files, so the archive holds
                // both forms -- and reading every one of them as a model reported several hundred
                // perfectly good animations as malformed, once per file.
                if (IsAnimation(entry.Data))
                {
                    animations++;
                    continue;
                }

                // Nor is a shadow volume. It carries SMES/SVTX/SFAC/EDGE and no MESH, because it is
                // silhouette geometry to be extruded rather than a surface to be shaded -- so the
                // model reader's "no MESH chunk" was right about the bytes and wrong about what they
                // are, sixty-eight times in the sequel's common pack alone.
                if (IsShadowVolume(entry.Data))
                {
                    shadows++;
                    continue;
                }

                try
                {
                    models[key] = profile is null ? MefModel.Read(entry.Data) : MefModel.Read(entry.Data, profile);
                }
                catch (Exception ex) when (ex is PackFormatException or IlffFormatException)
                {
                    log.Warning(LogCategory, $"Model '{entry.Name}' in '{name}' did not parse: {ex.Message}");
                }
            }
        }

        if (animations > 0 || shadows > 0)
        {
            log.Information(
                LogCategory,
                $"'{name}' holds {animations} bone animation(s) and {shadows} shadow volume(s) beside its "
                    + "models; neither is a render model.");
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
        // pixels (notably 015_05_1 on the human face). Registering with replacement enabled overwrites
        // only colliding texture keys while keeping common-pack-only entries mounted for WorldScene's
        // instances from both packs.
        string textureArchivePath = $"{dir}textures/{name}.res";
        _ = TryRegisterArchive(textureArchivePath, resources, log, replaceExisting: true);

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

    /// <summary>Resolves a model stored in this pack's archive even when no <c>INST</c> record names it.</summary>
    /// <remarks>
    /// Most scene geometry must remain limited to the pack's <c>MODS</c>/<c>INST</c> tables.  Retail
    /// <c>OBJECTS.QVM</c> destroyed-model fields are an exception: Level 6's terminal, for example,
    /// names <c>252_02_1</c> only as a replacement and therefore has no ordinary instance record.
    /// </remarks>
    public bool TryGetArchiveModel(string name, out MefModel? model) => _models.TryGetValue(name, out model);

    /// <summary>Resolves the diffuse texture a draw record samples.</summary>
    /// <param name="instance">The instance being drawn.</param>
    /// <param name="materialSlot">The draw record's material slot.</param>
    /// <param name="textureName">Receives the texture's bare name.</param>
    /// <returns><see langword="false"/> when the record draws untextured.</returns>
    public bool TryGetTextureName(MtpInstance instance, int materialSlot, out string textureName)
    {
        // The walk itself is the pack's, not this type's: both games resolve a slot the same way, and
        // I.G.I. 2 asks the same question of the same chunks through Igi2ModelTable.
        bool resolved = Pack.TryGetTextureName(instance, materialSlot, out string? name);
        textureName = name ?? string.Empty;
        return resolved;
    }

    /// <summary>Resolves the diffuse texture a draw record samples, for a model named directly.</summary>
    /// <param name="model">The model's name, as level data spells it.</param>
    /// <param name="materialSlot">The draw record's material slot.</param>
    /// <param name="textureName">Receives the texture's bare name.</param>
    /// <returns><see langword="false"/> when nothing in the pack answers for that model and slot.</returns>
    /// <remarks>
    /// <para>
    /// A material belongs to an <em>instance</em>, not to a mesh — the slot indexes the instance's own
    /// material range — so a caller holding only a name has to find the instance that uses it. Both
    /// spellings resolve: a name that is itself an instance's, and a name that is the mesh an instance
    /// points at. Level data uses the first for placed objects and the second for a forest stand, whose
    /// <c>Model</c> parameter names the tree mesh.
    /// </para>
    /// <para>
    /// The first instance using a mesh wins. Two instances of one mesh with different materials would be
    /// two differently textured objects, and nothing in the retail data does that for the meshes reached
    /// this way — a tree is a tree.
    /// </para>
    /// </remarks>
    public bool TryGetTextureName(string model, int materialSlot, out string textureName)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        textureName = string.Empty;
        _byModelName ??= BuildInstanceIndex();

        return _byModelName.TryGetValue(model, out MtpInstance instance)
            && TryGetTextureName(instance, materialSlot, out textureName);
    }

    private Dictionary<string, MtpInstance> BuildInstanceIndex()
    {
        Dictionary<string, MtpInstance> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (MtpInstance instance in Instances)
        {
            index.TryAdd(instance.Name, instance);

            if (!instance.Model.IsNone && !instance.Model.IsCommon && instance.Model.Index < Pack.ModelNames.Count)
            {
                index.TryAdd(Pack.ModelNames[instance.Model.Index], instance);
            }
        }

        return index;
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
        ReadOnlySpan<char> span = resourceName.AsSpan();
        int slash = span.LastIndexOfAny('\\', '/');
        if (slash >= 0)
        {
            span = span[(slash + 1)..];
        }

        int dot = span.LastIndexOf('.');
        return new string(dot > 0 ? span[..dot] : span);
    }

    /// <summary>Whether an archive entry is a bone animation rather than a model.</summary>
    /// <remarks>
    /// By the container's form type, which is the only thing that distinguishes them: both are ILFF and
    /// both are named <c>.iff</c> in the archive. <c>MINA</c> is <c>ANIM</c> as the tag is stored.
    /// </remarks>
    private static bool IsAnimation(ReadOnlyMemory<byte> data)
    {
        try
        {
            return IlffContainer.Read(data).FormType == IlffTags.Mina;
        }
        catch (IlffFormatException)
        {
            // Not a container at all, so the model reader's own message is the useful one.
            return false;
        }
    }

    /// <summary>Whether an archive entry is a shadow volume rather than a render model.</summary>
    /// <remarks>
    /// By the header chunk it carries. Both are <c>OCEM</c> containers, so the form type cannot separate
    /// them the way it separates an animation; what a shadow volume has instead of a mesh is
    /// <see cref="MefTags.ShadowMesh"/>.
    /// </remarks>
    private static bool IsShadowVolume(ReadOnlyMemory<byte> data)
    {
        try
        {
            IlffContainer container = IlffContainer.Read(data);
            if (container.FormType != IlffTags.Ocem)
            {
                return false;
            }

            bool shadow = false;
            foreach (IlffChunk chunk in container.Chunks)
            {
                if (chunk.Id == MefTags.Mesh)
                {
                    return false;
                }

                shadow |= chunk.Id == MefTags.ShadowMesh;
            }

            return shadow;
        }
        catch (IlffFormatException)
        {
            return false;
        }
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
