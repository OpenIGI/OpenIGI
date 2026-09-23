using OpenIGI.Game.World;

namespace OpenIGI.Host.Infrastructure;

/// <summary>Says whether a mission's files can be read yet, before a load that reads them all at once.</summary>
/// <remarks>
/// <para>
/// A desktop reads a level from disk as it loads it, and its files are always there. A browser holds an
/// installation in memory, read from what the person picked, and fetches a mission's folder ahead of the
/// mission -- so a load asked for before its folder has arrived has nothing to read, and must wait.
/// </para>
/// <para>
/// Asked each frame while a load is due. The menu's going-in sequence holds its picture while the answer
/// is no, and runs the load on the frame it becomes yes. Being asked is also the gate's cue to fetch
/// whatever the mission still lacks.
/// </para>
/// </remarks>
public interface ILoadGate
{
    /// <summary>Whether a mission's files are all readable now.</summary>
    /// <param name="mission">The mission about to load; its directory is where its files are.</param>
    /// <param name="waitingFor">What is still missing, in words, when it is not ready; null when it is.</param>
    /// <returns><see langword="true"/> when the load may run.</returns>
    bool IsReady(MissionDescriptor mission, out string? waitingFor);
}

/// <summary>The gate of a host whose files are always there: every mission is ready.</summary>
public sealed class AlwaysReadyLoadGate : ILoadGate
{
    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static AlwaysReadyLoadGate Instance { get; } = new();

    /// <inheritdoc/>
    public bool IsReady(MissionDescriptor mission, out string? waitingFor)
    {
        waitingFor = null;
        return true;
    }
}
