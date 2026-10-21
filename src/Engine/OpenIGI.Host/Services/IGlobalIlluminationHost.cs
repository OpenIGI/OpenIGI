using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>What the real-time lighting model reads of the host: the level up now, its sun, and the caches it keys by.</summary>
internal interface IGlobalIlluminationHost
{
    /// <summary>Gets the level's placed objects, once walked.</summary>
    WorldScene? Scene { get; }

    /// <summary>Gets the world's renderer, which captures the surface cache.</summary>
    IWorldPresenter? World { get; }

    /// <summary>Gets the level's authored sun.</summary>
    DirectionalLightSettings? Sun { get; }

    /// <summary>Gets the sun this frame lights with.</summary>
    DirectionalLightSettings? EffectiveSun { get; }

    /// <summary>Gets whether a level is up, played or behind the menu.</summary>
    bool HasLevel { get; }

    /// <summary>Gets the mission whose level is up, for keying its caches.</summary>
    int? CurrentMissionId { get; }

    /// <summary>Gets the rendered frame's number.</summary>
    int FrameCount { get; }

    /// <summary>Gets the cache the level's distance fields are kept in.</summary>
    DistanceFieldCache FieldCache { get; }

    /// <summary>The sun the frame lights with, as the clock and the level's night set it.</summary>
    /// <returns>The sun.</returns>
    DirectionalLightSettings CycleSun();

    /// <summary>The sky's fill under this frame's sun.</summary>
    /// <returns>The colour.</returns>
    Vector3f SkyFillColour();

    /// <summary>Applies the lite backdrop's overrides, which decide whether either structure is wanted at all.</summary>
    void UpdateBackdropProfile();

    /// <summary>Brings the terrain's height raster up to date for the frame's traces.</summary>
    void UpdateTerrainRaster();

    /// <summary>Describes the distance field the frame's traces march, around an eye.</summary>
    /// <param name="eye">The eye.</param>
    /// <returns>The binding.</returns>
    DistanceFieldBinding DescribeDistanceField(Vector3d eye);

    /// <summary>Gets the level's baked probe field, which the surface cache's radiance is gathered against, or null before it lands.</summary>
    IrradianceVolume? ProbeField { get; }
}
