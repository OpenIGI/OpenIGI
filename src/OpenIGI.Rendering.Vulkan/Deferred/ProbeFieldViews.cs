using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Deferred;

/// <summary>
/// The four volumes a probe field is sampled as, when they are not the baked field's own.
/// </summary>
/// <remarks>
/// The world-space radiance cache is offered to the lighting in exactly the baked field's shape — three
/// channels of harmonics and a distance — so the shading pass reads whichever it is given without
/// knowing which it has. All that differs is where they live and what layout they are in: the baked
/// field is uploaded and left shader-readable, while the cache is written by compute and stays in the
/// general layout for good.
/// </remarks>
/// <param name="Red">The red channel's harmonics.</param>
/// <param name="Green">The green channel's.</param>
/// <param name="Blue">The blue channel's.</param>
/// <param name="Distance">How far each probe saw.</param>
/// <param name="Layout">The layout all four are in.</param>
internal readonly record struct ProbeFieldViews(
    ImageView Red,
    ImageView Green,
    ImageView Blue,
    ImageView Distance,
    ImageLayout Layout)
{
    /// <summary>Gets a value indicating whether all four views are real.</summary>
    public bool Valid =>
        Red.Handle != 0 && Green.Handle != 0 && Blue.Handle != 0 && Distance.Handle != 0;

    /// <summary>Gets one channel's view, in the order the lighting binds them.</summary>
    /// <param name="channel">Zero to three: red, green, blue, distance.</param>
    /// <returns>That channel's view.</returns>
    public ImageView View(int channel) => channel switch
    {
        0 => Red,
        1 => Green,
        2 => Blue,
        _ => Distance,
    };
}
