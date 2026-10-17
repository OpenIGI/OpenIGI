using OpenIGI.Engine.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1;

/// <summary>Project I.G.I.'s content, as the modules a session registers.</summary>
/// <remarks>
/// <para>
/// These were imperative blocks in the composition root, and the same block was maintained by hand a
/// second time in the mission-preview loader — with a comment saying so, and one entry already missing
/// from the copy. Naming them makes the list something a host walks rather than something two hosts
/// remember.
/// </para>
/// </remarks>
public static class Igi1ContentModules
{
    /// <summary>Gets every module, in the order a session registers them.</summary>
    /// <remarks>
    /// Order is behaviour: a module whose natives another module's tables call has to declare first, and
    /// the original's own start-up order is the specification for it.
    /// </remarks>
    public static IReadOnlyList<IContentModule> All { get; } =
    [
        new PlayerContentModule(),
        new AiContentModule(),
        new WorldContentModule(),
    ];
}

/// <summary>The player's task types.</summary>
/// <remarks>
/// Without these the level's own <c>HumanPlayer</c> task is never built and the spawn the mission
/// declares cannot be found — the tree loads perfectly well and simply has no player.
/// </remarks>
public sealed class PlayerContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "Player";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerTaskTypes.RegisterAll(context.Types);
    }

    /// <inheritdoc/>
    public void LoadTables(ContentLoadContext context)
    {
    }
}

/// <summary>The soldier brain's task types and its combat settings.</summary>
/// <remarks>
/// Without the task types, <c>HumanAI</c>, <c>PatrolPath</c> and <c>PatrolPathCommand</c> load as
/// untyped placeholders: the brain cannot be found, so no soldier gets its own <c>MISSION:AI/&lt;id&gt;</c>
/// script, so nothing names a patrol path and every guard falls back to its archetype and stands still.
/// </remarks>
public sealed class AiContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "AI";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AiTaskTypes.RegisterAll(context.Types);
        HumanAiConfigRegistry.RegisterTypes(context.Types);

        context.Services.GetOrProvide(() => new HumanAiConfigRegistry(context.Logger));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every soldier's combat numbers — 29 AI types by three difficulties out of
    /// <c>common/ai/settings.qsc</c>. Loaded before any level, as <c>0x406DF0</c> is: the table has to
    /// exist before a <c>HumanAI</c> is configured.
    /// </remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.Require<HumanAiConfigRegistry>().Load(context.Resources, context.Natives, context.Tasks);
    }
}

/// <summary>The world's task types and the tables a level's own scripts never mention.</summary>
public sealed class WorldContentModule : IContentModule
{
    /// <inheritdoc/>
    public string Name => "World";

    /// <inheritdoc/>
    public void Register(ContentRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A level's CutScene tasks export isRun/isFinished/nTick, which the mission AI scripts read in
        // their idle branch. Without the type the symbols never bind and guard.qsc faults every idle
        // tick, cutting its patrol handling short.
        context.Types.Register<CutSceneTask>(CutSceneTask.TypeName);

        // A level parks its intro and outro actors inside ConditionalContainer tasks gated on state that
        // is false during play. Without the type each is built as a plain container and its children are
        // rendered, standing in the level at mission start where the original never registers them.
        context.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        // GetOrProvide rather than Provide: a session running beside another -- the mission preview --
        // must bind its own loop's natives to the tables the playing session already loaded, not to a
        // second empty set. The natives are per-loop either way; only the table is shared.
        context.Services.GetOrProvide(() => new MagicObjectRegistry(context.Logger)).Register(context.Natives);
        context.Services.GetOrProvide(() => new AnimTriggerRegistry(context.Logger)).Register(context.Natives);
        context.Services.GetOrProvide(() => new VehiclePhysicsRegistry(context.Logger));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The magic-object table is what turns a level's few hundred scripted objects into its full
    /// contents: every model's attachments name an entry in it, and a level's own scripts never mention
    /// it. Loading it is a start-up step, like the mission scan.
    /// </remarks>
    public void LoadTables(ContentLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Services.Require<MagicObjectRegistry>().Load(context.Resources, context.Natives);
        context.Services.Require<VehiclePhysicsRegistry>().Load(context.Resources);
        context.Services.Require<AnimTriggerRegistry>().Load(context.Resources, context.Natives);
    }
}
