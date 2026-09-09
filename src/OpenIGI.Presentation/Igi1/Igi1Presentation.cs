using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Presents Project I.G.I.'s world through its own renderers.</summary>
/// <remarks>
/// <para>
/// The adapters below carry no logic. They exist so that a pass written against
/// <see cref="ITitlePresentation"/> — today only <see cref="Rendering.ShadowCasterPass"/> — can draw
/// I.G.I. 1's world without naming <see cref="WorldRenderer"/> or <see cref="SoldierRenderer"/>, which
/// is what lets a second title supply its own.
/// </para>
/// <para>
/// The renderers are not owned here. The host builds and disposes them on its own schedule — the
/// soldier renderer is rebuilt per mission, the world renderer is rebuilt when the menu backdrop is
/// promoted into a mission — so this holds references and reads them per frame rather than caching.
/// </para>
/// <para>
/// TODO(phase-1): moves to <c>OpenIGI.Game.Igi1</c> with the rest of this folder.
/// </para>
/// </remarks>
/// <param name="world">The world renderer, or null before a level is loaded.</param>
/// <param name="characters">The soldier renderer, or null before a mission is loaded.</param>
internal sealed class Igi1Presentation(WorldRenderer? world, SoldierRenderer? characters) : ITitlePresentation
{
    /// <inheritdoc/>
    public IWorldPresenter? World { get; } = world is null ? null : new WorldAdapter(world);

    /// <inheritdoc/>
    public ICharacterPresenter? Characters { get; } = characters is null ? null : new SoldierAdapter(characters);

    private sealed class WorldAdapter(WorldRenderer renderer) : IWorldPresenter
    {
        public void Draw(WorldScene scene, in Camera3D camera, in Matrix4f projection, float minimumRadius) =>
            renderer.Draw(scene, camera, projection, minimumRadius);
    }

    private sealed class SoldierAdapter(SoldierRenderer renderer) : ICharacterPresenter
    {
        public void Draw(in Camera3D camera, in Matrix4f projection) => renderer.Draw(camera, projection);

        public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection) =>
            renderer.DrawPlayerBody(camera, projection);
    }
}
