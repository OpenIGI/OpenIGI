namespace OpenIGI.Rendering;

/// <summary>The offscreen scene capture: opening it, the passes the frame's draws belong to, and the targets a game-layer pass may read.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IScenePassDevice
{
    /// <summary>Gets the part of the frame draws currently belong to.</summary>
    /// <remarks><see cref="ScenePass.None"/> outside a scene capture, which is the original's own path.</remarks>
    ScenePass CurrentPass { get; }

    /// <summary>Starts rendering the scene into an offscreen target instead of the back buffer.</summary>
    /// <param name="description">The target to render into.</param>
    /// <returns>
    /// <see langword="true"/> when the capture began. <see langword="false"/> means the backend cannot
    /// offer one — the caller then draws exactly as the original does, which is a supported outcome
    /// rather than an error.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the one addition the whole remaster track rests on. The original throws each frame away
    /// as it presents it; shadows, ambient occlusion, reflections, volumetric fog and temporal
    /// anti-aliasing all need the frame and its depth to still be there afterwards.
    /// </para>
    /// <para>
    /// Pair with <see cref="EndSceneCapture"/> and then <see cref="IGraphicsDevice.ResolveScene"/>. Two-dimensional
    /// drawing — the HUD, the menus, the console — belongs <em>after</em> the resolve, on the back
    /// buffer, so that it is never tone mapped.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A capture is already in progress.</exception>
    bool BeginSceneCapture(in SceneTargetDescription description);

    /// <summary>Gets the current scene target's depth buffer, or <see cref="TextureHandle.None"/>.</summary>
    /// <remarks>
    /// The bridge the screen probe gather crosses: a pass in the game layer needs the frame's per-pixel
    /// depth to place probes and reconstruct where each pixel is in the world. Non-linear device depth,
    /// turned into a view distance with the same near and far the deferred lighting uses.
    /// </remarks>
    TextureHandle SceneDepth { get; }

    /// <summary>Gets the current scene target's normal buffer, or <see cref="TextureHandle.None"/>.</summary>
    /// <remarks>
    /// View-space normals, encoded to the zero-to-one range the way the G-buffer writes them. A consumer
    /// in world space has to lift them through the view-to-world rotation itself.
    /// </remarks>
    TextureHandle SceneNormal { get; }

    /// <summary>Gets the current scene target's lit colour, or <see cref="TextureHandle.None"/>.</summary>
    /// <remarks>
    /// What the deferred lighting wrote, before tone mapping. The screen probe gather reads it to add its
    /// indirect term onto the frame, compositing the surface cache's bounce over the direct light.
    /// </remarks>
    TextureHandle SceneColour { get; }

    /// <summary>
    /// What the resolve is about to multiply the lit buffer by — the exposure a display-referred colour
    /// must be divided by to survive it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A forward draw that declares itself emissive writes straight into the SCENE-REFERRED lit buffer,
    /// which the resolve then exposes and tone maps. A colour authored in display terms and written raw
    /// is at the mercy of the eye adaptation: in a dark scene the exposure is high, and a saturated red
    /// sprite comes back pink because the red channel clips long before the other two stop rising.
    /// Dividing by this lands it at the colour it was asked for whatever the scene is doing — the same
    /// correction the first-person rig's lighting and the selection glow already take.
    /// </para>
    /// <para>
    /// One on a backend or a path that draws straight to a display-encoded target, where a colour
    /// already means what it says. Never zero.
    /// </para>
    /// </remarks>
    float SceneExposure { get; }

    /// <summary>Declares which part of the frame the following draws belong to.</summary>
    /// <param name="pass">The part of the frame.</param>
    /// <remarks>
    /// <para>
    /// The renderers themselves never call this; the shell does, around the groups of draws it already
    /// makes. That is what lets the backend shade an opaque surface into a G-buffer and a translucent
    /// one directly, without any renderer in the game learning that there are two ways to be drawn.
    /// </para>
    /// <para>
    /// The glazing's front layer is the one pass that may begin while another is open: it begins with the
    /// geometry pass still open and closes it, without the shading that ending the geometry pass would
    /// run -- the front layer has to be drawn between the two.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">
    /// No capture is in progress, or a pass is already open other than the geometry pass the front layer
    /// closes.
    /// </exception>
    void BeginPass(ScenePass pass);

    /// <summary>Ends the current pass.</summary>
    /// <exception cref="GraphicsDeviceException">No pass is open.</exception>
    void EndPass();

    /// <summary>Stops rendering into the offscreen target.</summary>
    /// <exception cref="GraphicsDeviceException">No capture is in progress, or a pass is still open.</exception>
    void EndSceneCapture();
}
