using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What a title's own impact effects are drawn with.</summary>
/// <param name="Device">The device they draw with.</param>
/// <param name="Resources">The mission's resources, which hold the chip models.</param>
/// <param name="FileSystem">The game's files, which hold the effect sprites.</param>
/// <param name="Gameplay">The title's mission, whose effects are drawn.</param>
public readonly record struct MissionEffectsRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    VirtualFileSystem FileSystem,
    IMissionGameplay Gameplay);

/// <summary>A title's own impact effects, drawn over the lit scene: the dust and chips where rounds land, and the blood.</summary>
public interface IMissionImpactView : IDisposable
{
    /// <summary>Gets how many frames the blood mist's sprite has.</summary>
    int MistFrameCount { get; }

    /// <summary>Gets a value indicating whether the dust and blood sprites resolved.</summary>
    bool SpritesAvailable { get; }

    /// <summary>Loads what a mission's effects are drawn with.</summary>
    /// <param name="mission">The mission, whose packs hold the chip models.</param>
    /// <returns>How many chip models resolved.</returns>
    int Load(LoadedMission mission);

    /// <summary>Draws the effects.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">How far the frame is between the last two ticks.</param>
    void Draw(in Camera3D camera, in Matrix4f projection, float blend);
}
