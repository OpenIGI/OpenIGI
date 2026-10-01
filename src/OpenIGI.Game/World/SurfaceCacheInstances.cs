using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// A lighting page for every placement of every card, and where each one stands in the world.
/// </summary>
/// <remarks>
/// <para>
/// <b>The split this type exists for.</b> A card's <em>surface</em> is a property of the model: one
/// crate looks like every other crate, so its albedo, normal and material are captured once and shared
/// by all two hundred placements. A card's <em>lighting</em> is not: one crate stands in sunlight and
/// another in the shadow of a hangar, and giving them the same page would light every crate in the
/// level identically.
/// </para>
/// <para>
/// So there are two page layouts over the same cards. The captured one, from
/// <see cref="SurfaceCacheCapture"/>, has a page per model card. This one has a page per
/// <em>placement</em> of each model card, and the lighting atlases are built to it. A texel of lighting
/// finds its albedo at the same position within the card, in the other atlas.
/// </para>
/// <para>
/// The alternative — one per-instance atlas holding surface and lighting together, which is what the
/// reference does — is simpler and costs the duplication of every model's albedo once per placement.
/// On level 1 that is 687 copies of 106 models' surfaces, about six and a half times the memory, for
/// data that is identical in every copy.
/// </para>
/// </remarks>
public sealed class SurfaceCacheInstances
{
    private const string LogCategory = "SurfaceCache";

    private SurfaceCacheInstances(SurfaceCacheLayout layout, Placed[] instances, int[] cardForPage)
    {
        Layout = layout;
        Instances = instances;
        CardForPage = cardForPage;
    }

    /// <summary>Gets where each placement's lighting pages sit.</summary>
    public SurfaceCacheLayout Layout { get; }

    /// <summary>Gets every placement that has cards, in the order their pages were allocated.</summary>
    public Placed[] Instances { get; }

    /// <summary>Gets, for each lighting page, which captured card it lights.</summary>
    /// <remarks>
    /// The join between the two layouts. A lighting texel reads its albedo from the captured page of
    /// the card named here, at the same position within the card — which is why both layouts allocate
    /// pages of identical size for the same card.
    /// </remarks>
    public int[] CardForPage { get; }

    /// <summary>Builds a lighting page for every placement of every captured card.</summary>
    /// <param name="capture">What the card capture produced.</param>
    /// <param name="placements">The level's placed objects.</param>
    /// <param name="modelOf">
    /// Which captured model a placement uses, or a negative number when it uses none — a placement
    /// whose geometry was bent to its own span, or whose model captured no cards.
    /// </param>
    /// <param name="maximumSize">The largest atlas edge the device will allow.</param>
    /// <param name="logger">Where to report what was allocated.</param>
    /// <returns>The instance layout, or <see langword="null"/> when nothing has cards.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static SurfaceCacheInstances? Build(
        SurfaceCacheCapture.Result capture,
        IReadOnlyList<WorldPlacement> placements,
        Func<WorldPlacement, int> modelOf,
        int maximumSize = 8192,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(modelOf);

        IIgiLogger log = logger ?? NullLogger.Instance;

        // Which captured cards belong to each model, gathered once. The capture stores the owner per
        // card, so without this every placement would rescan the whole card list.
        Dictionary<int, List<int>> cardsByModel = [];

        for (int card = 0; card < capture.Owners.Length; card++)
        {
            int model = capture.Owners[card];

            if (!cardsByModel.TryGetValue(model, out List<int>? list))
            {
                list = [];
                cardsByModel[model] = list;
            }

            list.Add(card);
        }

        List<Placed> instances = [];
        List<MeshCard> pages = [];
        List<int> cardForPage = [];

        foreach (WorldPlacement placement in placements)
        {
            int model = modelOf(placement);

            if (model < 0 || !cardsByModel.TryGetValue(model, out List<int>? cards) || cards.Count == 0)
            {
                continue;
            }

            instances.Add(new Placed(model, placement.Position, placement.Orientation, pages.Count, cards.Count, placement));

            foreach (int card in cards)
            {
                // The same card, so the same page size. That equality is what lets a lighting texel
                // find its albedo at the same position within the card rather than needing a rescale.
                pages.Add(capture.Cards[card]);
                cardForPage.Add(card);
            }
        }

        if (pages.Count == 0)
        {
            log.Information(LogCategory, "No placement has cards; there is nothing to light.");
            return null;
        }

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(pages, maximumSize, log);

        log.Information(
            LogCategory,
            $"Surface cache lighting: {instances.Count} placement(s), {pages.Count} page(s) in a "
            + $"{layout.Width}x{layout.Height} atlas.");

        return new SurfaceCacheInstances(layout, [.. instances], [.. cardForPage]);
    }

    /// <summary>Where one placement stands, and which of its cards' pages are which.</summary>
    /// <param name="ModelIndex">Which captured model this placement uses.</param>
    /// <param name="Position">Where it stands, in world space.</param>
    /// <param name="Orientation">
    /// Its rotation, in the same form the renderer draws it with — so the lighting pass takes a card's
    /// model-space position and normal into the world exactly where the geometry is.
    /// </param>
    /// <param name="FirstPage">The first of this placement's lighting pages.</param>
    /// <param name="PageCount">How many pages follow it, one per card of the model.</param>
    /// <param name="Placement">
    /// The placement it was built from, so one that moves can be found again and its pages carried with it.
    /// </param>
    public readonly record struct Placed(
        int ModelIndex,
        Vector3d Position,
        Matrix3f Orientation,
        int FirstPage,
        int PageCount,
        WorldPlacement? Placement = null);
}
