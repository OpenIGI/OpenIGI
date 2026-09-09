using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Titles;

/// <summary>
/// A unit of title-specific engine content: the task types level data spawns, the script natives and
/// constants its scripts call, and the tables both are configured from.
/// </summary>
/// <remarks>
/// <para>
/// Registration is two-phase because the original's start-up is. Every type and native is declared into
/// an empty namespace first (<see cref="Register"/>), and only then are the tables loaded — by
/// <em>running scripts that call those natives</em> (<see cref="LoadTables"/>). A module that loaded a
/// table during <see cref="Register"/> would be resolving a native a later module has not declared yet,
/// which is the failure the original avoids by ordering <c>WinMain</c> the way it does.
/// </para>
/// <para>
/// A host that only needs the tree's shape — the mission-preview loader, a headless test, the task
/// census — calls <see cref="Register"/> and stops.
/// </para>
/// </remarks>
public interface IContentModule
{
    /// <summary>Gets the name this module is reported and ordered by.</summary>
    string Name { get; }

    /// <summary>Declares this module's task types, script natives and script constants.</summary>
    /// <param name="context">The registries to declare into.</param>
    void Register(ContentRegistrationContext context);

    /// <summary>Loads the tables the declarations are configured from.</summary>
    /// <param name="context">The resources and registries the tables are read through.</param>
    void LoadTables(ContentLoadContext context);
}

/// <summary>What a content module declares into.</summary>
/// <param name="Types">The task-type registry level data spawns from.</param>
/// <param name="Natives">The script namespace natives and constants are bound in.</param>
/// <param name="Variables">The registry exported task variables are published into.</param>
/// <param name="Services">Where a module publishes a table it built.</param>
/// <param name="Logger">Where registration diagnostics go.</param>
public sealed record ContentRegistrationContext(
    TaskTypeRegistry Types,
    NativeRegistry Natives,
    ScriptVarRegistry Variables,
    IServiceCollector Services,
    IIgiLogger Logger);

/// <summary>What a content module loads its tables through.</summary>
/// <param name="FileSystem">The virtual file system scripts and archives are read from.</param>
/// <param name="Resources">The resource registry archives are opened into.</param>
/// <param name="Natives">The script namespace the table scripts call back into.</param>
/// <param name="Services">Where a module publishes a table it built.</param>
/// <param name="Logger">Where load diagnostics go.</param>
public sealed record ContentLoadContext(
    VirtualFileSystem FileSystem,
    ResourceRegistry Resources,
    NativeRegistry Natives,
    IServiceCollector Services,
    IIgiLogger Logger);
