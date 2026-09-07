using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Draws the world into the sun's shadow cascades.
/// </summary>
/// <remarks>
/// <para>
/// It draws through the <em>same</em> renderers the main pass uses, with a camera placed in the light
/// instead of in the player's head. That is deliberate and is what the pass structure on
/// <see cref="IGraphicsDevice"/> exists for: a renderer asks for a mesh with a texture, and the device
/// decides from the current pass that this one wants depth only. Nothing in
/// <see cref="WorldRenderer"/> or <see cref="SoldierRenderer"/> knows that shadows exist.
/// </para>
/// <para>
/// <b>The light camera sits at the player's position, not at the light.</b> A directional light has no
/// position, and the cascades are fitted in a frame whose origin is the camera — so putting the light
/// camera anywhere else would mean every cascade matrix carrying a translation into single precision
/// at level coordinates that run into the thousands. This is the same reason
/// <c>Camera3D.CreateModelView</c> folds the camera offset into each object's transform.
/// </para>
/// <para>
/// <b>Terrain does not cast here.</b> Its level of detail is chosen from the camera's position, so
/// drawing it from the light would pick a tessellation for a viewpoint kilometres away — and its own
/// shadowing is already in the baked lightmaps. Objects and soldiers are what throw the shadows a player
/// reads. The ray-traced sun does see it, through <see cref="TerrainShadowCaster"/>.
/// </para>
/// </remarks>
public sealed class ShadowCasterPass
{
    private readonly IGraphicsDevice _device;

    /// <summary>Initialises a new instance of the <see cref="ShadowCasterPass"/> class.</summary>
    /// <param name="device">The device to draw through.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public ShadowCasterPass(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    /// <summary>Gets how many cascades the last call rendered.</summary>
    public int LastCascadeCount { get; private set; }

    /// <summary>Fills every cascade with the geometry that casts into it.</summary>
    /// <param name="cascades">The fitted cascades.</param>
    /// <param name="count">How many of them are live.</param>
    /// <param name="eye">The camera the cascades were fitted around.</param>
    /// <param name="scene">The world's placed objects, or <see langword="null"/> for none.</param>
    /// <param name="world">The renderer that draws them.</param>
    /// <param name="soldiers">The soldier renderer, or <see langword="null"/> for none.</param>
    public void Render(
        ReadOnlySpan<ShadowCascade> cascades,
        int count,
        in Camera3D eye,
        WorldScene? scene,
        WorldRenderer? world,
        SoldierRenderer? soldiers)
    {
        LastCascadeCount = 0;

        for (int i = 0; i < count && i < cascades.Length; i++)
        {
            if (!_device.BeginShadowCascade(i))
            {
                continue;
            }

            try
            {
                Camera3D lightCamera = new(eye.Position, cascades[i].LightRotation);

                if (scene is not null && world is not null)
                {
                    // An object smaller than a handful of texels of this cascade cannot cast a shadow
                    // anyone reads, and the cascades redraw the whole scene once each — so each one
                    // skips casters below 1/256th of its own radius. The near cascade's threshold is
                    // centimetres, which skips nothing a player could stand next to.
                    world.Draw(scene, lightCamera, cascades[i].Projection, cascades[i].Radius * (1f / 256f));
                }

                soldiers?.Draw(lightCamera, cascades[i].Projection);
            }
            finally
            {
                // Always closed, even if a renderer throws: leaving a cascade open strands the
                // framebuffer binding and the next frame draws the world into the shadow map.
                _device.EndShadowCascade();
            }

            LastCascadeCount = i + 1;
        }
    }
}
