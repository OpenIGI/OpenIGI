using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Turns what an I.G.I. 2 level places into the shared <see cref="WorldScene"/> a renderer draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is one world renderer, and both games use it.</b> <c>WorldRenderer</c> takes a
/// <see cref="WorldScene"/> of <see cref="WorldPlacement"/>s — a position, an orientation, an
/// <see cref="MtpInstance"/>, the pack behind it, a <see cref="MefModel"/> and a lightmap name — and
/// every one of those is a thing both games have. It named no task type, no model id and no asset path,
/// so it moved from <c>Game.Igi1</c> into the shared presentation layer unchanged.
/// </para>
/// <para>
/// What is <em>not</em> shared is how a level becomes placements. <see cref="WorldScene.Build"/> walks
/// Project I.G.I.'s task tree and knows its type names, its parameter names and even its
/// security-camera model ids. This is the sequel's equivalent, and it is short because the four
/// milestones before it already answered the hard parts: the scene walk found the placements, the pack
/// table resolved the virtual models, and the lightmap index bound the bakes.
/// </para>
/// </remarks>
public static class Igi2DrawableScene
{
    /// <summary>How deep an object's attachment tree is followed before it is assumed to be a cycle.</summary>
    /// <remarks>
    /// The original has no guard, because no shipped model attaches something that attaches it back.
    /// The corpus's deepest genuine tree is well inside this.
    /// </remarks>
    private const int MaxAttachmentDepth = 16;

    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.world";

    /// <summary>Builds the drawable scene for a loaded level.</summary>
    /// <param name="mission">The mission the level belongs to, which locates the lightmap archive.</param>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="table">The model table over the packs the game has open.</param>
    /// <param name="lightmaps">The level's baked object lightmaps, or null when it ships none.</param>
    /// <param name="packs">The loaded packs, in the order the game opens them, for resolving meshes.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The scene, and a count of what could not be placed.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Characters are skipped. <see cref="Igi2WorldScene.CharacterTypes"/> names the four types whose
    /// model is a character kind, and a character is drawn by a character presenter from a skinned mesh
    /// on a schedule the world pass does not own — the same division Project I.G.I. draws under.
    /// </remarks>
    public static Igi2DrawableSceneResult Build(
        LoadedMission mission,
        GameTask levelRoot,
        Igi2ModelTable table,
        Igi2LightmapIndex? lightmaps,
        IReadOnlyList<LoadedPack> packs,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(packs);

        IIgiLogger log = logger ?? NullLogger.Instance;
        Igi2WorldScene placed = Igi2WorldScene.Build(levelRoot);

        List<WorldPlacement> drawable = [];
        int unresolved = 0;
        int lit = 0;

        // The task the tree being emitted belongs to, which every node of it shares: its level id names
        // the lightmap entries, and its type and queue are the object's, not the attachment's.
        int placementId = 0;
        string placementType = string.Empty;
        int placementGroup = WorldPlacement.DefaultRenderGroup;

        int attached = 0;
        int attachmentsUnresolved = 0;

        // One node of an object's tree: its own model, then everything hanging off it. Returns false
        // only when the model itself cannot be found, which for a root is what "unresolved" counts.
        //
        // ModelObj_CreateAttachObjects (igi2 0x56D620) walks the model's attachment array and
        // ModelObj_CreateAttachObject (0x56D0B0) makes each one an object in its own right: it composes
        // the attachment's position and matrix onto the parent's, looks the attachment's NAME up as a
        // magic-object type, and -- when it is not one -- uses that same name AS A MODEL and spawns a
        // RigidMagicObj. 27 of the 28 attachment names level 1 uses resolve straight to a model, so for
        // static world geometry the name is simply the model.
        bool Emit(
            string modelName,
            Vector3d unitsPosition,
            Matrix3f orientation,
            string mounted,
            Matrix3f mountedOrientation,
            int variant,
            int depth,
            ref int index)
        {
            if (!table.TryResolveMesh(modelName, out string? mesh)
                || !TryFind(packs, mesh, out LoadedPack? pack, out MefModel? model)
                || !table.TryGetInstance(modelName, out MtpInstance instance))
            {
                return false;
            }

            // Claimed before the children are walked, and claimed whether or not this model is
            // lightmapped: RigidDynCubeObj_EnumerateLightmapVirModelsHandler (igi2 0x53C6D0) enumerates
            // the node's own model, increments nSubQTaskID unconditionally, and only then recurses into
            // the attach objects and the task children, all sharing the one counter. So the numbering is
            // depth first over the whole tree and a diffuse node still consumes its place in it.
            int own = index++;

            string entry = string.Empty;
            if (lightmaps is not null
                && lightmaps.TryGetBindings(new Igi2Placement(placementId, string.Empty, modelName, default, default, 0, variant), out IReadOnlyList<Igi2LightmapBinding>? bindings)
                && FinestOf(bindings, variant, own) is { } bake)
            {
                entry = BareName(bake.ResourceName);
                lit++;
            }

            drawable.Add(new WorldPlacement(
                // The walk reads the placement in engine units, because that is what everything else
                // built on it -- the sector graph, the lightmap bindings -- compares against. A
                // WorldPlacement is the renderer's, and the renderer works in metres.
                WorldScene.ToMetres(unitsPosition),
                orientation,
                instance,
                pack,
                model,
                placementType,
                levelRoot)
            {
                LightmapEntry = entry,

                // The queue this object is drawn in. Every I.G.I. 2 level authors it, and it is what
                // separates a decal or a pane of glass from the geometry it has to come after.
                RenderGroup = placementGroup,
            });

            // A model that attaches something whose model attaches it back would recurse forever. The
            // original has no guard because its data has no such cycle; inheriting that on a modified
            // file is not worth it.
            if (depth >= MaxAttachmentDepth)
            {
                return true;
            }

            // The model this one carries, on the host's first magic vertex. The vertex is a point in
            // the host's own space, and RigidDynCubeObj_GetMagicVertexPos (igi2 0x58E430) takes it to
            // the world exactly as an attachment point is taken: the host's rows dotted with it, plus
            // the host's position. A host with no magic vertex mounts at its own origin, which is what
            // that function returns when there is none to read.
            //
            // Emitted after the attachments because that is the order the lightmap enumerator walks:
            // a node's own model, then its attach objects, then its task children -- and the camera body
            // is a child task of the bracket (SCamera_SCameraLensCreateHandler, 0x48EC90).
            if (mounted.Length > 0)
            {
                Vector3d mountPosition = unitsPosition;
                if (model.TryGetMagicVertex(0, out Vector3f mount))
                {
                    Vector3f mountOffset = orientation.Transposed().Transform(mount);
                    mountPosition = new Vector3d(
                        unitsPosition.X + mountOffset.X,
                        unitsPosition.Y + mountOffset.Y,
                        unitsPosition.Z + mountOffset.Z);
                }

                if (Emit(mounted, mountPosition, mountedOrientation, string.Empty, default, variant, depth + 1, ref index))
                {
                    attached++;
                }
                else
                {
                    attachmentsUnresolved++;
                }
            }

            foreach (MefAttachment attachment in model.Attachments)
            {
                // Parent to world is the column-vector product M*v, which is what the renderer applies
                // for the parent's own orientation, and the rotation composes parent then child. This is
                // the same composition WorldScene uses for Project I.G.I.'s attachment trees, and it is
                // what 0x56D0B0 computes: the parent's rows dotted with the attachment's offset, and the
                // parent's matrix times the attachment's.
                Vector3f offset = orientation.Transposed().Transform(attachment.Position);
                Vector3d childPosition = new(
                    unitsPosition.X + offset.X,
                    unitsPosition.Y + offset.Y,
                    unitsPosition.Z + offset.Z);

                if (Emit(
                    attachment.Name,
                    childPosition,
                    orientation * attachment.Orientation,
                    string.Empty,
                    default,
                    variant,
                    depth + 1,
                    ref index))
                {
                    attached++;
                }
                else
                {
                    // A name that is a magic-object TYPE rather than a model: the original reads its
                    // model out of the magic table, which this port has none of for the sequel. It still
                    // consumed its index above, so the numbering of everything after it holds.
                    attachmentsUnresolved++;
                }
            }

            return true;
        }

        foreach (Igi2Placement placement in placed.StaticPlacements)
        {
            placementId = placement.LevelId;
            placementType = placement.TypeName;
            placementGroup = placement.RenderGroup;

            int index = 0;
            if (!Emit(
                placement.Model,
                placement.Position,
                WorldScene.ToOrientation(placement.Orientation),
                placement.MountedModel,
                WorldScene.ToOrientation(placement.MountedOrientation),
                placement.ModelVariant,
                depth: 0,
                ref index))
            {
                unresolved++;
            }
        }

        log.Information(
            LogCategory,
            $"World: {drawable.Count} drawable placements ({attached} attached), {lit} lightmapped, "
                + $"{unresolved} unresolved, {attachmentsUnresolved} attachment(s) not a model.");

        return new Igi2DrawableSceneResult(
            WorldScene.FromPlacements(mission, drawable),
            placed,
            unresolved,
            lit,
            attached);
    }

    private static bool TryFind(
        IReadOnlyList<LoadedPack> packs,
        string mesh,
        [NotNullWhen(true)] out LoadedPack? pack,
        [NotNullWhen(true)] out MefModel? model)
    {
        // Nearest first, which is the order the packs were opened in and the order the original searches.
        foreach (LoadedPack candidate in packs)
        {
            if (candidate.TryGetModel(mesh, out MefModel? found) && found is not null)
            {
                pack = candidate;
                model = found;
                return true;
            }
        }

        pack = null;
        model = null;
        return false;
    }

    /// <summary>The finest-detail bake of the model slot a placement wears, or nothing when it has none.</summary>
    /// <param name="bindings">Every bake the level's archive holds for the owning task.</param>
    /// <param name="variant">The slot, counted from one, or zero when the type has a single slot.</param>
    /// <param name="modelIndex">Which model of that slot's tree, counted depth first from zero.</param>
    /// <returns>The binding, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// The list arrives sorted by variant, then model, then level of detail, so the first entry that
    /// matches is the finest of its slot and model.
    /// </para>
    /// <para>
    /// Nothing matching is the correct answer rather than a failure: the original's enumerator creates an
    /// entry only for a lightmapped mesh with faces, so a slot holding a plain diffuse model simply has
    /// no bake.
    /// </para>
    /// </remarks>
    private static Igi2LightmapBinding? FinestOf(IReadOnlyList<Igi2LightmapBinding> bindings, int variant, int modelIndex)
    {
        foreach (Igi2LightmapBinding binding in bindings)
        {
            if (binding.Variant == variant && binding.ModelIndex == modelIndex)
            {
                return binding;
            }
        }

        // A type whose enumerator this port has not read reports slot zero, and its bakes may still
        // carry a letter -- a GenericTBA's pristine model is lettered 'a'. Falling back to the lowest
        // letter at the same model index is that first slot, which is the model a placement wears until
        // something damages it.
        if (variant != 0)
        {
            return null;
        }

        foreach (Igi2LightmapBinding binding in bindings)
        {
            if (binding.ModelIndex == modelIndex)
            {
                return binding;
            }
        }

        return null;
    }

    private static string BareName(string resourceName)
    {
        int slash = resourceName.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? resourceName[(slash + 1)..] : resourceName;
    }
}

/// <summary>What building an I.G.I. 2 level's drawable scene produced.</summary>
/// <param name="Scene">The scene, ready for the shared world renderer.</param>
/// <param name="Placed">Everything the level places, characters included, before the world filter.</param>
/// <param name="UnresolvedCount">How many static placements named something the open packs do not hold.</param>
/// <param name="LightmappedCount">How many drawable placements a baked lightmap covers.</param>
/// <param name="AttachedCount">
/// How many of the drawable placements are parts hanging off another object rather than something the
/// level itself placed. See <c>ModelObj_CreateAttachObjects</c> (<c>igi2 0x56D620</c>).
/// </param>
public readonly record struct Igi2DrawableSceneResult(
    WorldScene Scene,
    Igi2WorldScene Placed,
    int UnresolvedCount,
    int LightmappedCount,
    int AttachedCount);
