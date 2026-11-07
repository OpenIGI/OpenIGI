namespace OpenIGI.Host;

/// <content>What a dedicated server asks of the host from outside the frame: the next map of its rotation.</content>
public sealed partial class GameHost
{
    // The mission asked for from outside, started at the next frame's boundary.
    private int? _missionAsked;

    // The mission a listen host's players voted for, gone into as its own missions are, behind the loading card.
    private int? _voteMission;

    /// <summary>Asks the host to go on to a mission at the next frame's boundary: a dedicated server's next map, or its operator's.</summary>
    /// <param name="id">The mission's id.</param>
    /// <returns><see langword="true"/> when it is a mission this game has.</returns>
    public bool GoToMission(int id)
    {
        if (_missions is null || !_missions.TryGet(id, out _))
        {
            return false;
        }

        _missionAsked = id;
        return true;
    }

    // A mission asked for from outside, at a frame's boundary where a level may be swapped.
    private void ApplyMissionAsked(FrameLoopState state)
    {
        if (_missionAsked is { } id)
        {
            _missionAsked = null;
            StartMission(id);
        }

        if (_voteMission is { } voted)
        {
            _voteMission = null;
            GoIntoMission(state, voted);
        }
    }
}
