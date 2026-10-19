using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Project I.G.I.'s own impact effects, drawn by the retail impact renderer from the mission's runtime.</summary>
/// <param name="renderer">The renderer.</param>
/// <param name="gameplay">The mission, whose effects are drawn.</param>
internal sealed class Igi1ImpactView(RetailImpactRenderer renderer, Igi1Gameplay gameplay) : IMissionImpactView
{
    /// <inheritdoc/>
    public int MistFrameCount => renderer.MistFrameCount;

    /// <inheritdoc/>
    public bool SpritesAvailable => renderer.SpritesAvailable;

    /// <inheritdoc/>
    public int Load(LoadedMission mission) => renderer.Load(mission);

    /// <inheritdoc/>
    public void Draw(in Camera3D camera, in Matrix4f projection, float blend) =>
        renderer.Draw(gameplay.Impacts.Runtime, camera, projection, blend);

    /// <inheritdoc/>
    public void Dispose() => renderer.Dispose();
}
