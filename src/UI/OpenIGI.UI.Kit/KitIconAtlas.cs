using OpenIGI.UI.Drawing;

namespace OpenIGI.UI.Kit;

/// <summary>
/// The interface kit's own icons -- refresh, the eye, the tick, the shield and the rest the pages draw -- baked from
/// <c>assets/ui-icons</c> by <c>tools/OpenIGI.Tools.IconAtlas</c> and embedded here, named as <see cref="KitIcon"/> names them
/// (with <c>ShieldCheck</c> and <c>Flag</c> besides). A host makes them on its renderer's images and hands them to its theme.
/// </summary>
public static class KitIconAtlas
{
    private const string Resource = "OpenIGI.UI.Kit.ui-icons.bin";

    /// <summary>Makes the kit's icons on a renderer's images.</summary>
    /// <param name="images">The renderer's images.</param>
    /// <returns>The icons, or null where the baked atlas is missing; the caller owns and disposes them.</returns>
    public static IconAtlas? Load(IUiImageFactory images)
    {
        ArgumentNullException.ThrowIfNull(images);
        using Stream? stream = typeof(KitIconAtlas).Assembly.GetManifestResourceStream(Resource);
        return stream is null ? null : IconAtlas.Read(stream, images);
    }
}
