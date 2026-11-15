using OpenIGI.Core.Simulation;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Hosting;

namespace OpenIGI.Host;

/// <summary>
/// The level held in memory at a moment and put back exactly, for the editor's rewinds: the cutscene preview's seeks and the
/// play-test's replay go back by restoring one rather than loading the level again (<see cref="StateCheckpoint"/>, over the
/// <see cref="SimulationBoundary"/>).
/// </summary>
public sealed partial class GameHost
{
    /// <summary>Gets whether the level up can be held: a level is loaded, and the platform compiles code.</summary>
    internal bool CanCheckpointLevel => StateCheckpoint.IsSupported && _session.LevelRoot is not null;

    /// <summary>Records the level as it stands.</summary>
    /// <param name="tick">What the caller counts the moment as (a scene's tick, a recording's).</param>
    /// <param name="previous">An earlier checkpoint of the same level, whose unchanged data is shared.</param>
    /// <returns>The checkpoint, or null when there is no level.</returns>
    internal LevelCheckpoint? CaptureLevel(int tick, LevelCheckpoint? previous = null)
    {
        if (!CanCheckpointLevel || _session.LevelRoot is not { } level)
        {
            return null;
        }

        List<object> roots = [level, _loop.Tasks, _loop.Variables, _hosted, _mission];
        if (TitlePlayer is { } player)
        {
            roots.Add(player);
        }

        StateCheckpoint state = StateCheckpoint.Capture(
            roots,
            SimulationBoundary.Level,
            previous is { } earlier && ReferenceEquals(earlier.Level, level) ? earlier.State : null);
        return new LevelCheckpoint(level, tick, state);
    }

    /// <summary>
    /// Puts the level back as a checkpoint holds it, with the editor's changes made since laid on it again, and silences what was
    /// sounding since.
    /// </summary>
    /// <param name="checkpoint">The checkpoint.</param>
    /// <returns>Whether it was put back: false when another level is up.</returns>
    internal bool RestoreLevel(LevelCheckpoint checkpoint)
    {
        if (!ReferenceEquals(checkpoint.Level, _session.LevelRoot))
        {
            return false;
        }

        checkpoint.State.Restore();

        // What claims a drawn placement may have changed with it: what the renderer cached of that is asked again.
        _loop.Tasks.PlacementsChanged();
        ReapplyLiveEdits();
        _cutsceneSounds.StopAll();
        _vehicleSounds?.StopAll();
        _hosted.Audio.ResetForMission();
        return true;
    }
}

/// <summary>A level held at a moment.</summary>
/// <param name="Level">The level it was taken of: void once another is loaded.</param>
/// <param name="Tick">The moment, as the one that took it counts.</param>
/// <param name="State">What it holds.</param>
internal sealed record LevelCheckpoint(LevelTask Level, int Tick, StateCheckpoint State);
