using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The traced copy of the world: whether it is ready, how fast it fills, and the geometry offered to it without being drawn.</summary>
/// <remarks>A device that traces nothing answers every member with its default. Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface ITracedWorldDevice
{
    /// <summary>Gets whether a ray traced against the world would meet the level's own geometry.</summary>
    /// <remarks>
    /// The world's acceleration structure is only built once the level has been drawn and its geometry
    /// gathered, and until then a tracing pass binds a stand-in that every ray misses: the answer an empty
    /// world gives. Anything that lights the level ONCE and keeps the result -- the surface cache's
    /// load-time convergence -- has to wait for this, or it lights every surface as if nothing stood
    /// between it and any light. Always true on a device that traces nothing, where the question does not
    /// arise.
    /// </remarks>
    bool WorldTraceReady => true;

    /// <summary>Gets whether the world's acceleration structure has stopped growing.</summary>
    /// <remarks>
    /// The structure is filled from what the frames draw and offer, a few meshes a frame, so it is ready
    /// long before it is whole. Something that lights the level once and keeps the result waits for this,
    /// and only falls back to <see cref="WorldTraceReady"/> when the level never settles. Always true on a
    /// device that traces nothing.
    /// </remarks>
    bool WorldTraceSettled => true;

    /// <summary>Lifts, or restores, the per-frame budget on geometry newly admitted to the traced world.</summary>
    /// <param name="unlimited">Whether everything offered is admitted in the frame it is offered.</param>
    /// <remarks>
    /// For the loading screen: the budget keeps structure builds out of frames the player sees, and behind
    /// a load there are none, so lifting it lets the whole level arrive in a few frames rather than a few
    /// seconds -- and lets what is lit once at load be lit against all of it.
    /// </remarks>
    void SetTraceAdmission(bool unlimited)
    {
        _ = unlimited;
    }

    /// <summary>Stops, or resumes, building the traced world from the frames' draws.</summary>
    /// <param name="paused">Whether draws are kept out of the traced world.</param>
    /// <remarks>
    /// For a picture nothing traces -- the menu's lite backdrop. Every mesh admitted there is an
    /// acceleration structure built, and then forgotten at the next swap, for a level nobody will play.
    /// Resumed, the world fills from the next frame's draws as it does at any load.
    /// </remarks>
    void SetTracePaused(bool paused)
    {
        _ = paused;
    }

    /// <summary>Drops everything a backend has accumulated about the level being left.</summary>
    /// <remarks>
    /// <para>
    /// Some of what a backend builds is discovered from the frames it draws rather than handed to it:
    /// the traceable copy of the world is assembled from geometry-pass draws over a level's first
    /// seconds, and it has no other way of learning that the level has gone. Destroying the meshes tells
    /// it that each one has gone, which is not the same thing -- the material records a structure's hits
    /// are looked up in are append-only by design, so a session that entered six missions carried six
    /// missions' worth of them and the seventh would have overflowed the buffer they live in.
    /// </para>
    /// <para>
    /// Called when a mission is released, before its meshes and textures are destroyed. It is not a
    /// device reset: nothing the front end is still drawing with is touched, and a backend with nothing
    /// accumulated does nothing.
    /// </para>
    /// </remarks>
    void ReleaseWorld();

    /// <summary>Puts part of a mesh into the traced world without drawing it.</summary>
    /// <param name="mesh">The mesh to admit.</param>
    /// <param name="indexOffset">The first index of the run.</param>
    /// <param name="indexCount">How many indices the run covers; a multiple of three.</param>
    /// <param name="modelView">The model-to-view transform, as <see cref="IMeshDrawDevice.DrawMesh"/> takes it.</param>
    /// <param name="diffuse">The diffuse texture, which is what names the run's material to a ray.</param>
    /// <remarks>
    /// <para>
    /// For geometry that must be THERE for a ray and absent for the camera. The player's own body in
    /// first person is the case this exists for: it has to be in the acceleration structure or no
    /// reflected ray can strike it and no window can show the player standing at it, and it must not be
    /// in the G-buffer or the camera is inside its head.
    /// </para>
    /// <para>
    /// Only meaningful once a pass can shade a hit from the hit's own material. Before that, geometry the
    /// camera could not see had no colour available to it and admitting it only produced a silhouette of
    /// nothing.
    /// </para>
    /// <para>
    /// A no-op by default: a backend that traces nothing has nowhere to put this, and the callers are
    /// written so that skipping it costs only the reflection.
    /// </para>
    /// </remarks>
    /// <param name="firstPersonOnly">
    /// Whether only reflections may find it, as the first-person rig is: the player's own body, which the
    /// camera never sees. Every other ray -- the GI's, the shadows' -- passes through it, or an invisible
    /// occluder stands wherever the camera does and darkens whatever the player faces.
    /// </param>
    /// <param name="material">
    /// What the run is made of, as it would be drawn -- whether it glows, and whether it occludes. Nothing
    /// is drawn here, so there is no current material to take it from: reading the device's own left this
    /// run with whatever the last DRAW set, which recorded a culled monitor with a ceiling lamp's glow and
    /// could leave a culled wall out of the traced world because the last thing drawn was the sky.
    /// <see langword="null"/> is an ordinary opaque surface.
    /// </param>
    void SubmitTracedMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        bool firstPersonOnly = false,
        SurfaceMaterial? material = null)
    {
    }

    /// <summary>Offers a mesh to the ray-traced world for this frame only, as a shadow caster that is not drawn.</summary>
    /// <param name="mesh">A mesh from <see cref="IMeshDevice.CreateMeshStorage"/> whose first <paramref name="indexCount"/> indices are the caster.</param>
    /// <param name="modelView">The transform the mesh would be drawn with.</param>
    /// <param name="indexCount">How many indices, from the start of the mesh, make up the caster.</param>
    /// <remarks>
    /// <para>
    /// For geometry whose traceable shape is not what the rasteriser is handed: the terrain. Its pages
    /// hold every chunk ever streamed into them at every level of detail, at positions the vertex stage
    /// then slides towards the next level as the camera moves — so a structure built over a page casts
    /// from a surface that is not the one on screen, and the ground shadows itself in patches. The
    /// terrain instead writes the chunks it drew, at the positions it drew them, into a mesh of its own
    /// and offers that here each frame; the placement lasts one frame, and the structure is rebuilt
    /// whenever the mesh's contents change.
    /// </para>
    /// <para>
    /// A backend without a traced world ignores the call, which is the default implementation.
    /// </para>
    /// </remarks>
    void SubmitShadowCaster(MeshHandle mesh, in Matrix4f modelView, int indexCount);

    /// <summary>Gets whether <see cref="SubmitShadowCaster"/> would keep anything this frame.</summary>
    /// <remarks>
    /// Asked before building a caster at all, because building one is the expensive half: the terrain walks
    /// its whole octree from every direction to write it. False where nothing is traced -- a backend
    /// without a traced world, or the menu's lite backdrop, which pauses it -- and a profile of the menu
    /// found that walk taking a couple of seconds of the render thread for a caster the device then
    /// dropped.
    /// </remarks>
    bool AcceptsShadowCasters => false;
}
