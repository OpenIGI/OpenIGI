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
    /// <param name="magic">The magic object types attach points are looked up in, or <see langword="null"/> to take every name as a model.</param>
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
        IOpenIgiLogger? logger = null,
        Igi2MagicObjTypes? magic = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(packs);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        // The walk is given the models because one type cannot be placed without them: a train's
        // carriages are spaced by the distance between their model's two couplings, which nothing
        // authors. See Igi2Train.
        Igi2WorldScene placed = Igi2WorldScene.Build(levelRoot, Models(table, packs));

        List<WorldPlacement> drawable = [];
        int unresolved = 0;
        int lit = 0;

        // The task the tree being emitted belongs to, which every node of it shares: its level id names
        // the lightmap entries, and its type and queue are the object's, not the attachment's.
        int placementId = 0;
        string placementType = string.Empty;
        int placementGroup = WorldPlacement.DefaultRenderGroup;
        string placementDestroyed = string.Empty;
        string placementMountedOn = string.Empty;
        IReadOnlyList<string>? placementWears = null;

        int attached = 0;
        int attachmentsUnresolved = 0;

        // The parts hanging directly off each placed object, kept with the object's task id. Only a
        // RotateAttachment consumes this, and only this moment has both in hand.
        List<Igi2AttachedPlacement> parts = [];

        // One node of an object's tree: its own model, then everything hanging off it. Returns false
        // only when the model itself cannot be found, which for a root is what "unresolved" counts.
        //
        // ModelObj_CreateAttachObjects (igi2 0x56D620) walks the model's attachment array and
        // ModelObj_CreateAttachObject (0x56D0B0) makes each one an object in its own right: it composes
        // the attachment's position and matrix onto the parent's, looks the attachment's NAME up as a
        // magic-object type (Igi2MagicObjTypes), which names the model it wears -- and when it is not one
        // uses that same name AS A MODEL and spawns a RigidMagicObj.
        bool Emit(
            string modelName,
            Igi2Pose pose,
            string mounted,
            Matrix3f mountedOrientation,
            IIgi2PlacementGeometry? geometry,
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

            Vector3d unitsPosition = pose.Position;
            Matrix3f orientation = pose.ToMatrix();

            // Claimed before the children are walked, and claimed whether or not this model is
            // lightmapped: RigidDynCubeObj_EnumerateLightmapVirModelsHandler (igi2 0x53C6D0) enumerates
            // the node's own model, increments nSubQTaskID unconditionally, and only then recurses into
            // the attach objects and the task children, all sharing the one counter. So the numbering is
            // depth first over the whole tree and a diffuse node still consumes its place in it.
            int own = index++;

            // A model this placement deforms rather than draws as it ships -- a wire stretched down
            // its span, a spline segment bent along its curve. The geometry is the placement's own and
            // not the model's, because the renderer's mesh cache is keyed by name and two wires of one
            // model are not one mesh.
            MeshGeometry? bent = null;
            if (geometry is not null)
            {
                bent = geometry.Build(model);
                if (bent is null)
                {
                    return false;
                }
            }

            // A deformed mesh is not looked up in the bake index. Its lightmap coordinates are the
            // model's, but the bake belongs to the OBJECT, and for a spline segment that object is one
            // the level data never declares -- SplineObjSegment_New (igi2ed 0x611E80) makes it, and what
            // number SplineObjWaypoint_EnumerateLightmapHandler (0x643BB0) gives it is not established
            // here. Binding it to the owning waypoint's own number would be a guess, and a wrong bake
            // looks worse on screen than no bake, so it goes unlit until the numbering is read.
            string entry = string.Empty;
            if (geometry is null
                && lightmaps is not null
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
                BentGeometry = bent,
                LevelId = placementId,

                // The queue this object is drawn in. Every I.G.I. 2 level authors it, and it is what
                // separates a decal or a pane of glass from the geometry it has to come after.
                RenderGroup = placementGroup,

                // What the object is drawn as once it is destroyed. Only the object's own model is swapped; its parts
                // hang on as they were.
                DestroyedReplacement = depth == 0 ? Replacement(placementDestroyed) : null,

                // The models a switch wears by its state; only the object's own model changes.
                Variants = depth == 0 && placementWears is { } wears && wears.Select(Replacement).ToArray() is var bound
                    && Array.Exists(bound, static binding => binding is not null)
                        ? bound
                        : null,
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
            if (mounted.Length > 0 && (depth > 0 || placementMountedOn.Length == 0 || magic is null))
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

                if (Emit(mounted, Igi2Pose.FromMatrix(mountPosition, mountedOrientation), string.Empty, default, null, variant, depth + 1, ref index))
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
                // The magic table decides what the point becomes: its type's model, its own name, or -- for a
                // shadow volume or a mark -- nothing drawn, which enumerates no lightmap and takes no number.
                if ((magic is null ? attachment.Name : magic.ModelOf(attachment.Name)) is not { } attachedModel)
                {
                    continue;
                }

                // ModelObj_CreateAttachObject (igi2 0x56D0B0): the parent's rows dotted with the record's
                // offset, and the parent's matrix times the record's matrix TRANSPOSED -- the loader points
                // the attachment array straight at the ATTA chunk's bytes, and the composition reads them
                // column for row. The collision world places the same tree through the same pose, so what
                // is drawn is what is stood on.
                // A model mounted on an attach object rather than on the host goes with that object, facing as it faces.
                int at = drawable.Count;
                Igi2Pose attachedPose = pose.Attach(attachment);
                bool carries = depth == 0 && placementMountedOn.Length > 0 && magic?.Find(attachment.Name)?.TaskType == placementMountedOn;
                if (Emit(
                    attachedModel,
                    attachedPose,
                    carries ? mounted : string.Empty,
                    carries ? attachedPose.ToMatrix() : default,
                    geometry: null,
                    variant,
                    depth + 1,
                    ref index))
                {
                    attached++;

                    // Only an object's OWN parts, because that is the list the original walks: the
                    // attachment list of the task's model, not of everything under it.
                    if (depth == 0)
                    {
                        parts.Add(new Igi2AttachedPlacement(placementId, attachment.Name, drawable[at]));
                    }
                }
                else
                {
                    // A model the packs do not hold. It still consumed its index above, so the numbering of
                    // everything after it holds.
                    attachmentsUnresolved++;
                }
            }

            return true;
        }

        foreach (Igi2Placement placement in placed.StaticPlacements)
        {
            placementId = placement.LevelId;
            // The renderer caches a deformed model's geometry under the task's name rather than the
            // model's, because the deformation is the placement's and not the model's. Two wires, or two
            // segments of one road, must not collide in that cache.
            placementType = placement.Geometry is null
                ? placement.TypeName
                : $"{placement.TypeName}:{placement.LevelId}";
            placementGroup = placement.RenderGroup;
            placementDestroyed = placement.DestroyedModel;
            placementMountedOn = placement.MountedOn;
            placementWears = placement.Wears;

            int index = 0;
            if (!Emit(
                placement.Model,
                placement.Frame is { } frame
                    ? Igi2Pose.FromMatrix(placement.Position, frame)
                    : Igi2Pose.FromAngles(placement.Position, placement.Orientation),
                placement.MountedModel,
                WorldScene.ToOrientation(placement.MountedOrientation),
                placement.Geometry,
                placement.ModelVariant,
                depth: 0,
                ref index))
            {
                unresolved++;
            }
        }

        WorldModelBinding? Replacement(string modelName) =>
            modelName.Length > 0
            && table.TryResolveMesh(modelName, out string? mesh)
            && TryFind(packs, mesh, out LoadedPack? pack, out MefModel? model)
            && table.TryGetInstance(modelName, out MtpInstance instance)
                ? new WorldModelBinding(instance, pack, model)
                : null;

        log.Information(
            LogCategory,
            $"World: {drawable.Count} drawable placements ({attached} attached), {lit} lightmapped, "
                + $"{unresolved} unresolved, {attachmentsUnresolved} attachment(s) not a model.");

        return new Igi2DrawableSceneResult(
            WorldScene.FromPlacements(mission, drawable),
            placed,
            parts,
            unresolved,
            lit,
            attached);
    }

    /// <summary>Resolves a placement's model name to its mesh: through the pack table, then the packs nearest first.</summary>
    /// <param name="table">The level's pack table.</param>
    /// <param name="packs">The packs, in load order.</param>
    /// <returns>The lookup; it answers <see langword="null"/> for a name nothing the level ships holds.</returns>
    public static Func<string, MefModel?> Models(Igi2ModelTable table, IReadOnlyList<LoadedPack> packs)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(packs);
        return name => table.TryResolveMesh(name, out string? mesh) && TryFind(packs, mesh, out _, out MefModel? model)
            ? model
            : null;
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
/// <param name="Attachments">
/// Every part hanging directly off a placed object, with the task id of the object carrying it. Kept
/// because nothing downstream can get from a placement back to the task it belonged to, and a
/// <c>RotateAttachment</c> needs exactly that. See <see cref="Igi2RotateAttachments"/>.
/// </param>
/// <param name="UnresolvedCount">How many static placements named something the open packs do not hold.</param>
/// <param name="LightmappedCount">How many drawable placements a baked lightmap covers.</param>
/// <param name="AttachedCount">
/// How many of the drawable placements are parts hanging off another object rather than something the
/// level itself placed. See <c>ModelObj_CreateAttachObjects</c> (<c>igi2 0x56D620</c>).
/// </param>
public readonly record struct Igi2DrawableSceneResult(
    WorldScene Scene,
    Igi2WorldScene Placed,
    IReadOnlyList<Igi2AttachedPlacement> Attachments,
    int UnresolvedCount,
    int LightmappedCount,
    int AttachedCount);
