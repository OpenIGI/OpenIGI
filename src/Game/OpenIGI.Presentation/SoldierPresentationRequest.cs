using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What the level's soldiers are drawn with.</summary>
/// <param name="Device">The graphics device to upload through and draw with.</param>
/// <param name="Resources">The level's resources, which hold the models, skeletons and textures.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <param name="Magic">The magic-object table the held weapons resolve through, or <see langword="null"/> for none.</param>
/// <param name="DetailMaps">The material detail maps the bodies are shaded with, or <see langword="null"/> for none.</param>
/// <param name="Sun">The level's fixed-function light as it stands, which the bodies and their guns are lit by; or none.</param>
/// <param name="Lod">The level's detail settings as they stand, which a distant body gives up detail by; or none to draw it whole.</param>
public readonly record struct SoldierPresentationRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    IOpenIgiLogger Logger,
    MagicObjectRegistry? Magic = null,
    MaterialDetailPack? DetailMaps = null,
    Func<FixedFunctionSun?>? Sun = null,
    Func<Rendering.ModelLodPolicy?>? Lod = null);
