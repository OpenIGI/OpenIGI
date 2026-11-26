using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Game.Scoring;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The session overlays laid over play with made-up players, as <c>r_hud_overlay_preview</c> asks: a developer's way to look at
/// their design in either title without a session, one still each from a single lockstep capture.
/// </summary>
public sealed partial class GameHost
{
    // The made-up players: enough of them, on both sides, alive and down, with a host and this player among them.
    private static readonly ScoreboardLine[] PreviewLines =
    [
        new(0, "Jones", TeamRoster.Good, 0, 14, 3, true, 1, 0, Assists: 4),
        new(1, "Anya", TeamRoster.Good, 0, 9, 5, true, 2, 38, Assists: 7),
        new(2, "Priboi", TeamRoster.Bad, 0, 11, 6, false, 3, 92, Assists: 2),
        new(3, "Ekk", TeamRoster.Bad, 0, 6, 9, true, 4, 151, Assists: 5),
        new(4, "Phillips", TeamRoster.Good, 0, 3, 7, false, 5, 64, Assists: 1),
        new(5, "Lagatin", TeamRoster.Bad, 0, 2, 10, true, 6, 210, Assists: 0),
    ];

    // This player's own line among them.
    private const int PreviewOwnEntity = 2;

    // What the preview lays over the frame, after every real overlay.
    private void DrawOverlayPreview(FrameLoopState state, in Camera3D eye, float horizontalTangent, float aspectRatio)
    {
        string asked = HudCVars.OverlayPreview.Value.Trim().ToLowerInvariant();
        if (asked.Length == 0 || _remasteredHud is not { IsReady: true } hud)
        {
            return;
        }

        bool Wants(string name) => asked == name || asked == "all";
        int width = SafeWindowWidth, height = SafeWindowHeight;
        _device.Set2DProjection(width, height);
        state.Batch.Begin();

        if (Wants("scoreboard"))
        {
            hud.DrawScoreboard(state.Batch, new Scoreboard(0, 0, 0, [.. PreviewLines.Select(static l => l with { Team = TeamRoster.Good })]), PreviewOwnEntity, "K7Q2", "Mission 3", width, height);
        }
        else if (asked == "scoreboard_team")
        {
            hud.DrawScoreboard(state.Batch, new Scoreboard(4, 3, 2, PreviewLines), PreviewOwnEntity, null, "Mission 5", width, height, mode: MatchModes.TeamDeathmatch);
        }

        if (Wants("chat") || asked == "chat_open")
        {
            HudChatLine[] chat =
            [
                new("Server", "Anya joined the game", 1f, ChatKind.Server),
                new("Jones", "Guard tower to the north, two on it", 1f),
                new("Anya", "Covering the gate", 1f, ChatKind.Team),
                new("Priboi", "You'll never reach the base", 1f, ChatKind.All, Enemy: true, Dead: true),
            ];
            HudChatEntry? entry = asked == "chat_open" ? new HudChatEntry("Moving up on the left", 21, false, 0, true, false, 0f) : null;
            hud.DrawCoopTalk(state.Batch, chat, entry, "Poor connection to the host", ["Anya"], [], eye, horizontalTangent, aspectRatio, width, height);
        }

        if (Wants("revive"))
        {
            Vector3d ahead = eye.Position + (new Vector3d(eye.Orientation.M31, eye.Orientation.M32, eye.Orientation.M33) * 6.0);
            hud.DrawReviveMarker(state.Batch, new ReviveMarker(ahead, "Anya", 0.6f), eye, horizontalTangent, aspectRatio, width, height);
            hud.DrawReviveMarker(state.Batch, new ReviveMarker(eye.Position - (new Vector3d(eye.Orientation.M31, eye.Orientation.M32, eye.Orientation.M33) * 30.0), "Jones", 0f), eye, horizontalTangent, aspectRatio, width, height);
            hud.DrawDownedAlert(state.Batch, 0, "Jones", 30, 1f, width, height);
            hud.DrawNameTags(state.Batch, [new WorldNameTag(ahead + new Vector3d(1.5, 0.0, 0.0), "Phillips", 0.45f)], eye, horizontalTangent, aspectRatio, width, height);
            hud.DrawShield(state.Batch, 1.2f, 2f, width, height);
        }

        if (Wants("downed"))
        {
            hud.DrawDowned(state.Batch, 0.4f, width, height);
        }

        if (Wants("controls"))
        {
            hud.DrawControls(state.Batch, CurrentControlsHelp(state.Console.System.Binds, KeyLabel(ControlsHelpCVars.Key.Value) ?? "H"), width, height);
        }

        if (Wants("match") || asked is "teammenu" or "buymenu" or "mapvote")
        {
            DrawPreviewMatch(state, hud, asked, width, height);
        }

        // A race's timer and splits, a little behind the ghost, the newest split just reached; the ghost's mark 40 m ahead, and
        // another behind, pinned to the edge.
        if (Wants("race"))
        {
            Vector3d forward = new(eye.Orientation.M31, eye.Orientation.M32, eye.Orientation.M33);
            hud.DrawRaceGhostMarker(state.Batch, new Presentation.Hud.RaceGhostMarker(eye.Position + (forward * 40.0), 1f), eye, horizontalTangent, aspectRatio, width, height);
            hud.DrawRaceGhostMarker(state.Batch, new Presentation.Hud.RaceGhostMarker(eye.Position - (forward * 25.0), 1f), eye, horizontalTangent, aspectRatio, width, height);
            hud.DrawRace(
                state.Batch,
                new Presentation.Hud.RaceHud(
                    1.4,
                    [new("Objective 1", 41.3, -2.1), new("Objective 2", 95.0, 0.6), new("Objective 3", 188.2, 1.4)],
                    LatestShown: 0.8f),
                width,
                height);
        }

        // The replay viewer's bar, over a made-up run: playing at twice its speed, and at its end where it parted.
        if (Wants("replay") || asked == "replay_parted")
        {
            bool parted = asked == "replay_parted";
            (double, string)[] marks = [(41.0, "a guard sees you"), (95.5, "objective"), (188.0, "alarm"), (301.2, "objective")];
            hud.DrawReplayBar(
                state.Batch,
                new Presentation.Hud.ReplayBar(
                    "Jones · Trainyard · Hard · Classic", parted ? 512.0 : 214.6, 512.0, parted, parted ? 1f : 2f, "Behind", marks,
                    PartedAt: parted ? 233.4 : null, Ending: parted ? "Mission complete · Space plays it again" : null),
                width,
                height);
        }

        state.Batch.End();
    }

    // A completed mission's rank screen, as r_hud_overlay_preview missionscore asks -- one figure short of the next rank -- or
    // missionscore_coop, a squad of six scored as one, counting down.
    private static MissionScoreCard? PreviewScoreCard()
    {
        string asked = HudCVars.OverlayPreview.Value.Trim().ToLowerInvariant();
        if (asked is not ("missionscore" or "missionscore_coop"))
        {
            return null;
        }

        var card = new MissionScoreCard(
            "MISSION STATS",
            "Category",
            "Achieved",
            "Target",
            [
                new("Time To Complete Mission:", "0:14:52", true, "0:20:00"),
                new("Times Spotted:", "3", false, "1"),
                new("Health:", "76%", true, "50%"),
                new("Accuracy:", "41%", true, "40%"),
            ],
            "Rank:",
            "Covert Operative",
            1,
            3);
        return asked == "missionscore"
            ? card
            : card with
            {
                Lines =
                [
                    new("Time To Complete Mission:", "0:14:52", true, "0:20:00"),
                    new("Times Spotted:", "4", false, "2"),
                    new("Health:", "38%", false, "50%"),
                    new("Accuracy:", "44%", true, "30%"),
                ],
                RankName = "Field Officer",
                Rank = 0,
                Squad =
                [
                    new("Jones", 3, "76%", "41%", 14, IsOwn: true),
                    new("Anya", 0, "92%", "63%", 9, IsOwn: false),
                    new("Phillips", 1, "38%", "22%", 6, IsOwn: false),
                    new("Priboi", 0, "64%", "N/A", 0, IsOwn: false),
                    new("Ekk", 0, "81%", "35%", 11, IsOwn: false),
                    new("Lagatin", 0, "100%", "58%", 7, IsOwn: false),
                ],
            };
    }

    // The preview's foot: the host's, counting down in the co-op sample.
    private static Presentation.MissionScorePrompt PreviewScorePrompt() =>
        HudCVars.OverlayPreview.Value.Trim().Equals("missionscore_coop", StringComparison.OrdinalIgnoreCase)
            ? new Presentation.MissionScorePrompt("SPACE", SecondsLeft: 7)
            : new Presentation.MissionScorePrompt("SPACE", Board: "On the boards: #4 fastest", BoardGood: true);

    // A team match under way -- or decided, with the vote open -- and its menus, as the preview asks.
    private static void DrawPreviewMatch(FrameLoopState state, IRemasteredHudPresenter hud, string asked, int width, int height)
    {
        bool decided = asked == "mapvote";
        var match = new MatchHud
        {
            Mode = MatchModes.SearchAndDestroy,
            Title = MatchModes.Title(MatchModes.SearchAndDestroy),
            Phase = decided ? MatchPhase.Intermission : MatchPhase.Live,
            Round = 5,
            Target = 7,
            Clock = "1:24",
            Left = new TeamStrip(TeamRoster.Good, "I.G.I.", 3, [true, true, false], IsOwn: true),
            Right = new TeamStrip(TeamRoster.Bad, "Conspiracy", 2, [true, false, true], IsOwn: false),
            Role = "ATTACK",
            Money = 3200,
            BuyClock = "0:12",
            Hint = new ModeHint("HOLD", "TO PLANT THE BOMB AT A"),
            Result = decided ? new ResultCard("VICTORY", "I.G.I. WIN THE MATCH 7 - 4", 1, new MatchSummary(1, 6, 14, 3)) : null,
            MapVote = decided
                ? new NextMapVote([new MapChoice(3, MatchModes.TeamDeathmatch), new MapChoice(5, MatchModes.SearchAndDestroy, Again: true), new MapChoice(8, MatchModes.FreeForAll)], [2, 1, 0], 6, 0)
                : null,
        };
        hud.DrawMatchHud(state.Batch, asked is "teammenu" or "buymenu" ? match with { Hint = null } : match, null, "E", width, height);
        (float, float) pointer = (width * 0.5f, height * 0.5f);
        if (match.MapVote is { } vote)
        {
            hud.DrawMapVote(state.Batch, vote, match.Clock, pointer, width, height);
        }

        if (asked == "buymenu")
        {
            hud.DrawBuyMenu(
                state.Batch,
                [new ArmouryItem("ak", "AK-47", "ak47", 30, 2700), new ArmouryItem("m16", "M16A2", "m16", 30, 3100), new ArmouryItem("spas", "SPAS-12", "spas", 8, 3400), new ArmouryItem("nade", "Grenade", "grenade", 1, 300) { Full = true }],
                match.Money,
                match.BuyClock,
                pointer,
                width,
                height);
        }

        if (asked == "teammenu")
        {
            hud.DrawTeamMenu(state.Batch, new TeamPick(TeamRoster.Good, ["Jones", "Anya", "Phillips"], ["Priboi", "Ekk"]), match.Title, "M", pointer, width, height);
        }
    }
}
