using OpenIGI.Game.Config;

using OpenIGI.Engine.Titles;

namespace OpenIGI.Game.World;

/// <summary>Retail campaign-unlock semantics from Config_UpdateActiveMissionID (IGI1 0x406340).</summary>
public static class MissionProgression
{
    /// <summary>Advances the profile's progress in a game to <paramref name="nextMissionId"/> only when it lies ahead of its frontier.</summary>
    /// <param name="profile">The player's profile.</param>
    /// <param name="titleId">The game played, whose progress it is.</param>
    /// <param name="missions">The game's missions.</param>
    /// <param name="nextMissionId">The mission the completed one leads to.</param>
    /// <returns>Whether the frontier moved.</returns>
    public static bool UnlockNext(PlayerProfile profile, string titleId, IMissionCatalogue<MissionDescriptor> missions, int nextMissionId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(missions);
        if (nextMissionId == MissionDescriptor.NextMissionUndefined)
        {
            return false;
        }

        // The next mission must itself be on the campaign, after the active one. Unlocking on merely passing
        // the active mission unlocked a mission the catalogue does not offer (the browser build's
        // six-mission limit let the sixth unlock the seventh). A game not yet played stands at its first mission.
        List<MissionDescriptor> campaign = [.. missions.InProgressionOrder()];
        int frontier = campaign.FindIndex(mission => mission.Id == profile.MissionOf(titleId));
        int next = campaign.FindIndex(mission => mission.Id == nextMissionId);
        if (next <= Math.Max(frontier, 0))
        {
            return false;
        }

        profile.SetMission(titleId, nextMissionId);
        return true;
    }

    /// <summary>Gets a mission's campaign status when ActiveMission means the furthest unlocked mission.</summary>
    public static MissionCampaignStatus Status(IMissionCatalogue<MissionDescriptor> missions, int activeMissionId, int missionId)
    {
        int frontier = -1;
        int wanted = -1;
        int index = 0;
        foreach (MissionDescriptor mission in missions.InProgressionOrder())
        {
            if (mission.Id == activeMissionId)
            {
                frontier = index;
            }
            if (mission.Id == missionId)
            {
                wanted = index;
            }
            index++;
        }

        if (frontier < 0)
        {
            frontier = 0;
        }
        if (wanted < 0)
        {
            return MissionCampaignStatus.Locked;
        }
        return wanted < frontier ? MissionCampaignStatus.Completed
            : wanted == frontier ? MissionCampaignStatus.Available
            : MissionCampaignStatus.Locked;
    }
}

/// <summary>A mission's state relative to the profile's furthest unlocked mission.</summary>
public enum MissionCampaignStatus
{
    /// <summary>The profile has not reached this mission.</summary>
    Locked,
    /// <summary>This is the profile's current furthest unlocked mission.</summary>
    Available,
    /// <summary>This mission precedes the profile's current frontier.</summary>
    Completed,
}
