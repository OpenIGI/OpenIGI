using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Presents Project I.G.I.'s world through its own renderers.</summary>
/// <remarks>
/// <para>
/// The adapter below carries no logic. It exists so that a pass written against
/// <see cref="ITitlePresentation"/> — today only <see cref="Rendering.ShadowCasterPass"/> — can draw
/// I.G.I. 1's characters without naming <see cref="SoldierRenderer"/>, which is what lets a second title
/// supply its own. The world needs no adapter: its renderer implements the interface and has moved to
/// <c>OpenIGI.Game.Igi1</c>.
/// </para>
/// <para>
/// The renderers are not owned here. The host builds and disposes them on its own schedule — the
/// soldier renderer is rebuilt per mission, the world presenter is rebuilt when the menu backdrop is
/// promoted into a mission — so this holds references and reads them per frame rather than caching.
/// </para>
/// </remarks>
/// <param name="world">The world presenter, or null before a level is loaded.</param>
/// <param name="characters">The soldier renderer, or null before a mission is loaded.</param>
internal sealed class Igi1Presentation(IWorldPresenter? world, SoldierRenderer? characters) : ITitlePresentation
{
    /// <inheritdoc/>
    /// <remarks>Passed straight through: the world renderer implements the interface itself now.</remarks>
    public IWorldPresenter? World { get; } = world;

    /// <inheritdoc/>
    public ICharacterPresenter? Characters { get; } = characters is null ? null : new SoldierAdapter(characters);

    private sealed class SoldierAdapter(SoldierRenderer renderer) : ICharacterPresenter
    {
        public void Draw(in Camera3D camera, in Matrix4f projection) => renderer.Draw(camera, projection);

        public void DrawPlayerBody(in Camera3D camera, in Matrix4f projection) =>
            renderer.DrawPlayerBody(camera, projection);
    }
}
