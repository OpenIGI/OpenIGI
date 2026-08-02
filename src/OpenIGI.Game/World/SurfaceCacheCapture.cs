using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Renders every model into its cards' pages of the surface cache atlas, once, at load.
/// </summary>
/// <remarks>
/// <para>
/// The point of the whole surface cache: after this runs, a ray that hits a model anywhere in the level
/// can read what that surface looks like without the model being on screen, in the frustum, or drawn.
/// Each card is one orthographic view, and the six of them together describe a model from any
/// direction well enough to bounce light off.
/// </para>
/// <para>
/// <b>Runs on the render thread and cannot move.</b> Unlike the distance field bake, which is
/// arithmetic, this is a render pass — the atlas is a framebuffer and the geometry is already uploaded,
/// both of which belong to the context that made them.
/// </para>
/// <para>
/// <b>Captured per model, not per placement.</b> A level draws the same crate two hundred times and the
/// crate looks the same every time, so its cards are captured once and every instance reads them. What
/// an instance contributes is its transform, which the tracing side applies when it looks a card up.
/// </para>
/// </remarks>
public static class SurfaceCacheCapture
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>What a capture produced.</summary>
    /// <param name="Layout">Where every card's page sits.</param>
    /// <param name="Cards">The cards themselves, in the same order as the layout's pages.</param>
    /// <param name="Owners">Which model each card belongs to, as an index into the models given.</param>
    public readonly record struct Result(
        SurfaceCacheLayout Layout, MeshCard[] Cards, int[] Owners);

    /// <summary>One model to capture: its geometry, and how to reach the texture of each subset.</summary>
    /// <param name="Geometry">The drawable mesh.</param>
    /// <param name="Cards">The cards baked for it.</param>
    /// <param name="TextureForSubset">
    /// Supplies the diffuse texture a subset draws with, and the material it draws under.
    /// </param>
    public readonly record struct Subject(
        MeshGeometry Geometry,
        IReadOnlyList<MeshCard> Cards,
        Func<MeshSubset, (TextureHandle Diffuse, TextureHandle Lightmap, SurfaceMaterial Material)> TextureForSubset);

    /// <summary>Captures every model's cards into one atlas.</summary>
    /// <param name="device">The device to render through.</param>
    /// <param name="subjects">The models to capture, each with its cards and its textures.</param>
    /// <param name="meshes">The uploaded mesh for each subject, in the same order.</param>
    /// <param name="maximumSize">The largest atlas edge the device will allow.</param>
    /// <param name="logger">Where to report what was captured.</param>
    /// <returns>The layout and cards, or <see langword="null"/> when there was nothing to capture.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Result? Capture(
        IGraphicsDevice device,
        IReadOnlyList<Subject> subjects,
        IReadOnlyList<MeshHandle> meshes,
        int maximumSize = 8192,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(meshes);

        IIgiLogger log = logger ?? NullLogger.Instance;

        List<MeshCard> cards = [];
        List<int> owners = [];

        for (int subject = 0; subject < subjects.Count; subject++)
        {
            foreach (MeshCard card in subjects[subject].Cards)
            {
                cards.Add(card);
                owners.Add(subject);
            }
        }

        if (cards.Count == 0)
        {
            log.Information(LogCategory, "No cards to capture; the surface cache is empty.");
            return null;
        }

        SurfaceCacheLayout layout = SurfaceCacheLayout.Pack(cards, maximumSize, log);

        if (!device.BeginCardCapture(layout.Width, layout.Height))
        {
            log.Warning(LogCategory, "The card atlas could not be opened; the surface cache is empty.");
            return null;
        }

        int captured = 0;

        try
        {
            for (int index = 0; index < cards.Count; index++)
            {
                SurfaceCacheLayout.Page page = layout.Pages[index];

                if (!page.IsAllocated)
                {
                    continue;
                }

                int subject = owners[index];

                if (subject >= meshes.Count || !meshes[subject].IsValid)
                {
                    continue;
                }

                device.SetCardPage(page.X, page.Y, page.Width, page.Height);
                DrawCard(device, subjects[subject], meshes[subject], cards[index]);
                captured++;
            }
        }
        finally
        {
            device.EndCardCapture();
        }

        log.Information(
            LogCategory,
            $"Captured {captured} of {cards.Count} card(s) from {subjects.Count} model(s) into a "
            + $"{layout.Width}x{layout.Height} atlas.");

        return new Result(layout, [.. cards], [.. owners]);
    }

    /// <summary>Renders one model through one card's orthographic view.</summary>
    private static void DrawCard(
        IGraphicsDevice device, in Subject subject, MeshHandle mesh, MeshCard card)
    {
        // The card's own frame, as a rotation whose rows are its axes. Written as rows because that is
        // the convention Camera3D uses for a view matrix, and the backend applies a 3x3 the same way
        // wherever it came from -- a card that built its rotation the other way round would capture the
        // model mirrored, which looks like a plausible surface and is not.
        Matrix3f view = Matrix3f.FromRows(card.Right, card.Up, card.Direction);

        // The camera sits on the card's own plane looking along its facing, so the model is behind it.
        // Negated because the rotation takes world into the card's frame and the translation has to be
        // in that frame too, exactly as the world-to-view transform does it.
        Vector3f eye = card.Centre;
        Vector3f offset = view.Transform(new Vector3f(-eye.X, -eye.Y, -eye.Z));

        Matrix4f modelView = Matrix4f.CreateRigid(view, offset);

        // The box the card covers: its rectangle across, and its depth back. A near plane of zero is
        // what puts the card's own plane at the front of the capture, which is where the surface it
        // describes begins.
        Matrix4f projection = Matrix4f.CreateOrthographic(
            card.Width, card.Height, 0f, MathF.Max(card.Depth, 0.01f));

        device.SetProjection(projection);

        // Back faces are the inside of the model as seen from this card, and capturing them would put
        // the far wall of a crate into the page describing its near one.
        device.SetCullMode(CullMode.CounterClockwise);

        foreach (MeshSubset piece in subject.Geometry.Subsets)
        {
            if (piece.IndexCount == 0)
            {
                continue;
            }

            (TextureHandle diffuse, TextureHandle lightmap, SurfaceMaterial material) =
                subject.TextureForSubset(piece);

            device.SetMaterialOverride(material);
            device.DrawMesh(
                mesh, piece.IndexOffset, piece.IndexCount, modelView, diffuse, lightmap, BlendMode.Opaque);
        }
    }
}
