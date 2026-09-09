using System.Diagnostics.CodeAnalysis;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Titles;

/// <summary>A title's missions, as its own data publishes them.</summary>
/// <typeparam name="TDescriptor">What one mission's descriptor looks like for this title.</typeparam>
/// <remarks>
/// <para>
/// The <em>walk</em> is shared — every title publishes its missions by running one script per mission
/// directory, which is <see cref="ScriptTableLoader.WalkAndRun"/>. The <em>reader</em> is not:
/// I.G.I. 1's <c>DefineMission</c> takes eleven arguments in a fixed order, and there is no reason to
/// expect a second title's to agree on either the count or the meaning.
/// </para>
/// <para>
/// Generic in the descriptor rather than over a shared base, because the fields a title's missions carry
/// are exactly the thing that differs. A common base would either be empty or would be I.G.I. 1's
/// descriptor with a different name.
/// </para>
/// </remarks>
public interface IMissionCatalogue<TDescriptor>
    where TDescriptor : class
{
    /// <summary>Gets every mission, in the order the scan found them.</summary>
    IReadOnlyList<TDescriptor> Missions { get; }

    /// <summary>Gets how many missions are registered.</summary>
    int Count { get; }

    /// <summary>Declares the natives the discovery scripts call.</summary>
    /// <param name="natives">The script namespace to declare into.</param>
    void Register(NativeRegistry natives);

    /// <summary>Discovers every mission, running the scripts the title publishes them from.</summary>
    /// <param name="fileSystem">The file system the mission tree is walked on.</param>
    /// <param name="resources">The registry the scripts are loaded through.</param>
    /// <param name="natives">The script namespace the scripts call back into.</param>
    /// <returns>How many missions were registered.</returns>
    int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives);

    /// <summary>Looks up a mission by id.</summary>
    /// <param name="id">The mission id.</param>
    /// <param name="descriptor">Receives the descriptor when one is registered.</param>
    /// <returns><see langword="false"/> when no mission has that id.</returns>
    bool TryGet(int id, [NotNullWhen(true)] out TDescriptor? descriptor);
}
