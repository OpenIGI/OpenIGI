using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2;

/// <summary>I.G.I. 2: Covert Strike's content, as the modules a session registers.</summary>
/// <remarks>
/// The list grows one module per gameplay milestone. Everything not covered by a module still loads:
/// a type level data declares with no class behind it becomes a <c>DeclaredTask</c> placeholder with its
/// parameters intact, so a level is always structurally complete and only ever behaviourally partial.
/// </remarks>
public static class Igi2ContentModules
{
    /// <summary>Gets every module, in the order a session registers them.</summary>
    /// <remarks>
    /// Order is behaviour: a module whose natives another module's tables call has to declare first, and
    /// the original's own start-up order is the specification for it.
    /// </remarks>
    public static IReadOnlyList<IContentModule> All { get; } =
    [
        new Igi2ScriptVocabularyModule(),
        new Igi2TerrainContentModule(),
        new Igi2ForestContentModule(),
        new Igi2WaterContentModule(),
        new Igi2SkyContentModule(),
    ];
}

/// <summary>The script identifiers every I.G.I. 2 level depends on.</summary>
/// <remarks>
/// <para>
/// <b>There are only five, and they were counted rather than discovered.</b> Reading the symbol tables
/// of all 24 shipped <c>objects.qvm</c> together gives the complete vocabulary a level rests on: the two
/// task commands and <c>TRUE</c>/<c>FALSE</c>, which the engine already provides, plus these. Finding
/// them one failing level at a time would have taken as many passes as there are levels and still left
/// no evidence the list was complete.
/// </para>
/// <para>
/// The values come from <c>igi2.exe</c> — <c>AISquad_RegisterScriptValues</c> (<c>0x49A880</c>) for the
/// AI types and <c>OverlayHolder_Open</c> (<c>0x533BC0</c>) for the overlays. Reading them mattered more
/// than it looks: a wrong value does not fail. It lands as a parameter on a task, the tree still builds,
/// and it resurfaces much later looking like a gameplay bug with no trace back to here.
/// </para>
/// </remarks>
public sealed class Igi2ScriptVocabularyModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Script vocabulary";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Natives.RegisterConstant("AIType_Defensive", 0);
        context.Natives.RegisterConstant("AIType_Offensive", 1);
        context.Natives.RegisterConstant("AIType_OnVehicle", 2);

        context.Natives.RegisterConstant("OVERLAY_BINOCULAR", 0);
        context.Natives.RegisterConstant("OVERLAY_SNIPER", 2);
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The terrain's task types: the tile grid, its placement and its material table.</summary>
/// <remarks>
/// Without these the three types load as placeholders — the tree is right and the parameter values are
/// there, but nothing knows a tile has an id, so no <c>.thm</c>, <c>.tmm</c> or <c>.tlm</c> is ever
/// asked for and the level renders with no ground.
/// </remarks>
public sealed class Igi2TerrainContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Terrain";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<TerrainTask>(TerrainTask.TypeName);
        context.Types.Register<TerrainMapTask>(TerrainMapTask.TypeName);
        context.Types.Register<TerrainMaterialTask>(TerrainMaterialTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to load. A terrain's tables are its own child tasks and its tiles' map files, both of
    /// which arrive with the level rather than before it.
    /// </remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The forest's task types: a stand of trees and the wind that moves it.</summary>
/// <remarks>
/// Without these the two types load as placeholders, so nothing knows a stand has a level id, and
/// without the id nothing can name its <c>forest_%d.dat</c> — the placement file is named by the task
/// rather than by a parameter, so an unrecognised task means an unreachable file.
/// </remarks>
public sealed class Igi2ForestContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Forest";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<ForestTask>(ForestTask.TypeName);
        context.Types.Register<WindTask>(WindTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to load: a stand's placement file arrives with the level, not before it.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The water's task types: a sheet and the wave trains summed over it.</summary>
/// <remarks>
/// Without these the two types load as placeholders, so nothing knows a sheet has a level id or a
/// resource list, and its baked environment maps -- named from both -- are unreachable.
/// </remarks>
public sealed class Igi2WaterContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Water";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<WaterTask>(WaterTask.TypeName);
        context.Types.Register<WaterLayerTask>(WaterLayerTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to load: a sheet's environment maps arrive with the level, not before it.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The sky's task types: the dome, its scrolling layers, and its colour keyframes.</summary>
/// <remarks>
/// Without these the three load as placeholders, and a sky layer's texture stays the string the level
/// authored rather than the one the engine actually opens — <c>FlatSkyLayer_FixFilename</c> rebases
/// every name onto <c>MISSION:textures/</c>, and only the registered class knows to do that.
/// </remarks>
public sealed class Igi2SkyContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Sky";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Types.Register<FlatSkyTask>(FlatSkyTask.TypeName);
        context.Types.Register<FlatSkyLayerTask>(FlatSkyLayerTask.TypeName);
        context.Types.Register<FlatSkyKeyframeTask>(FlatSkyKeyframeTask.TypeName);
    }

    /// <inheritdoc/>
    /// <remarks>Nothing to load: a sky's textures arrive with the level, not before it.</remarks>
    public void LoadTables(ContentLoadContext context)
    {
    }
}
