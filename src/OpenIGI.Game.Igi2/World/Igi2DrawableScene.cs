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

        foreach (Igi2Placement placement in placed.StaticPlacements)
        {
            if (!table.TryResolveMesh(placement.Model, out string? mesh))
            {
                unresolved++;
                continue;
            }

            if (!TryFind(packs, mesh, out LoadedPack? pack, out MefModel? model))
            {
                unresolved++;
                continue;
            }

            // The instance is what carries the material slots, so two placements of one mesh in
            // different uniforms stay different objects. A name the table knows always has one.
            if (!table.TryGetInstance(placement.Model, out MtpInstance instance))
            {
                unresolved++;
                continue;
            }

            string entry = string.Empty;
            if (lightmaps is not null
                && lightmaps.TryGetBindings(placement, out IReadOnlyList<Igi2LightmapBinding>? bindings)
                && bindings.Count > 0)
            {
                // The first binding is the object's first model at its finest level of detail, which is
                // what the world pass draws. The rest belong to the LODs and to the object's other
                // models, and are reached through the index when a renderer wants them.
                entry = BareName(bindings[0].ResourceName);
                lit++;
            }

            drawable.Add(new WorldPlacement(
                placement.Position,
                WorldScene.ToOrientation(placement.Orientation),
                instance,
                pack,
                model,
                placement.TypeName,
                levelRoot)
            {
                LightmapEntry = entry,

                // The queue this object is drawn in. Every I.G.I. 2 level authors it, and it is what
                // separates a decal or a pane of glass from the geometry it has to come after.
                RenderGroup = placement.RenderGroup,
            });
        }

        log.Information(
            LogCategory,
            $"World: {drawable.Count} drawable placements, {lit} lightmapped, {unresolved} unresolved.");

        return new Igi2DrawableSceneResult(
            WorldScene.FromPlacements(mission, drawable),
            placed,
            unresolved,
            lit);
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
public readonly record struct Igi2DrawableSceneResult(
    WorldScene Scene,
    Igi2WorldScene Placed,
    int UnresolvedCount,
    int LightmappedCount);
