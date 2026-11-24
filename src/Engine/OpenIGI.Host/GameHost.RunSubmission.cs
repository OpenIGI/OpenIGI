using System.Net;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Replays;
using OpenIGI.Game.Scoring;
using OpenIGI.Releases;
using OpenIGI.Releases.Accounts;
using OpenIGI.Releases.Maps;

namespace OpenIGI.Host;

/// <content>
/// A completed run offered to the boards as its mission ends: its inputs (its ghost left out) sent signed by the player's key, the
/// checker's ruling followed, and what became of it told at the foot of the mission's rank screen -- checking, on the boards and
/// where, not ranked and why, kept on the player's machine and why, or that signing in with Discord would rank it.
/// </content>
public sealed partial class GameHost
{
    // How often a run waiting to be checked is asked after, and for how long.
    private static readonly TimeSpan RunAskEvery = TimeSpan.FromSeconds(5);
    private const int RunAsks = 120;

    private RunsService? _runsService;

    // What became of the last run offered, for the rank screen of its mission: the words, and whether it went well (null for
    // neither, as while it is checked).
    private volatile RunOffer? _runOffer;

    private sealed record RunOffer(int Mission, string Text, bool? Good);

    private RunsService RunsService => _runsService ??= new RunsService(AccountHttp, MapService.Master);

    /// <summary>Gets what became of the last run offered to the boards, while its mission is the one up; null for none.</summary>
    private RunOffer? ShownRunOffer => _runOffer is { } offer && _session.Mission?.Id == offer.Mission ? offer : null;

    /// <summary>Offers a run just finished to the boards, where it is one they rank and this game may send it.</summary>
    /// <param name="replay">The run.</param>
    /// <param name="sheet">The mission's score sheet as it ended, whose requirements weigh its rank.</param>
    private void OfferRunToBoards(ReplayFile replay, MissionScoreSheet? sheet)
    {
        _runOffer = null;
        if (replay.Result is not { Outcome: ReplayOutcome.Completed, Score: { } score }
            || !_options.SavesUserFiles || _options.SimulationOnly || !ReplayCVars.Submit.Value)
        {
            return;
        }

        int mission = replay.Header.Mission;
        if (replay.Result.Unranked.Length > 0 || Ruleset.FromId(replay.Header.Ruleset) == Ruleset.Unranked)
        {
            Offer(mission, $"Not ranked: {(replay.Result.Unranked.Length > 0 ? replay.Result.Unranked : "not played by the Classic or Modern rules")}", false);
            return;
        }

        if (_account is not { IsSignedIn: true, Account: { } account } || PlayerSigner() is not { } signer)
        {
            Offer(mission, "Sign in with Discord to put your runs on the boards", null);
            return;
        }

        byte[] bytes = (replay with { Ghost = null }).ToBytes();
        if (bytes.Length > RunsService.MaxReplayBytes)
        {
            Offer(mission, "Too long a run for the boards", false);
            return;
        }

        RunClaims claims = new(
            replay.Header.Title,
            mission,
            replay.Header.Difficulty,
            replay.Header.Ruleset,
            (int)Math.Min(score.Ticks, int.MaxValue),
            sheet is null ? -1 : sheet.Requirements.GetRank(score));
        Offer(mission, "Checking your run for the boards", null);
        _ = SubmitRunAsync(signer, claims, bytes, account.Id);
    }

    // The run sent, and asked after until the checker has ruled on it.
    private async Task SubmitRunAsync(IRequestSigner signer, RunClaims claims, byte[] bytes, string account)
    {
        try
        {
            string id = await RunsService.SubmitAsync(signer, claims, bytes).ConfigureAwait(false);
            for (int asked = 0; asked < RunAsks; asked++)
            {
                await Task.Delay(RunAskEvery).ConfigureAwait(false);
                RunStatus run = await RunsService.RunAsync(id).ConfigureAwait(false);
                if (run.IsPending)
                {
                    continue;
                }

                if (!run.IsVerified)
                {
                    Offer(claims.Mission, $"Not ranked: {run.Reason}", false);
                    return;
                }

                RunBoard board = await RunsService.BoardAsync(claims.Title, claims.Mission, claims.Difficulty, claims.Ruleset, "time", account).ConfigureAwait(false);
                Offer(claims.Mission, board.You is { } you ? $"On the boards: #{you.Place} fastest" : "On the boards", true);
                return;
            }

            Offer(claims.Mission, "Still being checked: see the boards later", null);
        }
        catch (AccountRefusedException refused)
        {
            // Not worth the room (no better than a best, would not place, the boards full) stays on the machine; anything else
            // the master refused is not ranked.
            bool kept = refused.Status is HttpStatusCode.Conflict or HttpStatusCode.InsufficientStorage or HttpStatusCode.TooManyRequests;
            Offer(claims.Mission, kept ? refused.Message : $"Not ranked: {refused.Message}", kept ? null : false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.Warning("Replays", $"The boards could not be reached for the run of mission {claims.Mission}: {ex.Message}");
            Offer(claims.Mission, "The boards could not be reached: the run stays on your machine", false);
        }
    }

    private void Offer(int mission, string text, bool? good) => _runOffer = new RunOffer(mission, text.TrimEnd('.'), good);
}
