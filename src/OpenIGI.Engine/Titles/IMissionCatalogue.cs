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

    /// <summary>Walks every registered mission in the order a browser should list them.</summary>
    /// <returns>The missions, campaign first.</returns>
    /// <remarks>
    /// <para>
    /// <b>A listing order, not a progression.</b> The two titles derive it completely differently and
    /// only one of them can state a progression at all: Project I.G.I.'s <c>DefineMission</c> names the
    /// mission that follows each one, so its campaign is a chain, and I.G.I. 2's names nothing of the
    /// kind — the argument that would carry it holds a four-character game mode instead. So this is what
    /// a menu wants, and where the real progression comes from is a separate question for the title that
    /// has one.
    /// </para>
    /// <para>
    /// Every registered mission appears, including any the campaign does not reach. A mod's extra level
    /// or an installation with a broken descriptor still exists and should still be listed rather than
    /// silently vanishing.
    /// </para>
    /// </remarks>
    IEnumerable<TDescriptor> InCampaignOrder();

    /// <summary>Walks the campaign the way the game plays it.</summary>
    /// <returns>The campaign's missions, in the order they are played.</returns>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="InCampaignOrder"/>, which lists everything for a browser. This is the
    /// progression a mission-select menu offers up to whatever the player has reached, so a mission the
    /// campaign never reaches must <b>not</b> appear in it.
    /// </para>
    /// <para>
    /// Only one of the two titles states a progression. Project I.G.I. names the mission that follows
    /// each one and its campaign is that chain; I.G.I. 2 names nothing of the kind, so the best it can
    /// answer is its campaign's own grouping — which is a real answer, but not one its data asserts.
    /// </para>
    /// </remarks>
    IEnumerable<TDescriptor> InProgressionOrder();

    /// <summary>Looks up a mission by id.</summary>
    /// <param name="id">The mission id.</param>
    /// <param name="descriptor">Receives the descriptor when one is registered.</param>
    /// <returns><see langword="false"/> when no mission has that id.</returns>
    bool TryGet(int id, [NotNullWhen(true)] out TDescriptor? descriptor);
}
