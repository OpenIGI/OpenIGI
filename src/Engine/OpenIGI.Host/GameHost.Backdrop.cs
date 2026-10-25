using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Ai;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Services;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The level behind the menu: the preview, its soldiers, the lite profile and the frosted glass.
    private readonly MenuBackdrop _menuBackdrop;

    // Where the menu stands: up or not and over what, the next backdrop, and the dissolve between them.
    private readonly MenuFlow _menuFlow;

    /// <summary>Gets whether the level on screen is the menu's backdrop, drawn lite.</summary>
    private bool BackdropLite => _menuBackdrop.IsLite(missionActive: _session.Mission is not null);

    /// <summary>Applies or clears the backdrop's lite profile to match <see cref="BackdropLite"/>. Cheap; call freely.</summary>
    private void UpdateBackdropProfile() => _menuBackdrop.UpdateProfile(missionActive: _session.Mission is not null);

    /// <summary>Arms, cycles and swaps the level behind the menu.</summary>
    private void UpdateMenuBackdrop(FrameLoopState state)
    {
        // The showcase's first backdrop, asked for a couple of menu frames in so the shell is already on
        // screen. It is built on a worker like every other one; until it arrives the menu sits over its
        // dark backdrop rather than over a blank blue clear.
        if (_menuFlow.OverShowcase && _menuFlow.ShowcasePending && _loop.Clock.FrameCount >= 2)
        {
            _menuFlow.ShowcasePending = false;

            // -entrycam's mission stands in for the default showcase, so the backdrop it presses Play
            // over is built through exactly the path a player's selection would have built it through.
            int firstShowcase = _options.EntryCamMission > 0 ? _options.EntryCamMission : ShowcaseMissionId;
            _menuFlow.CurrentShowcaseMissionId = firstShowcase;
            RequestBackdrop(state.PreviewLoader, firstShowcase);
        }

        // The backdrop is made playable here, on the frames where nothing is happening but a menu.
        // A couple of milliseconds is invisible at the uncapped rate the menu runs at, and the work
        // is finished long before anybody has finished reading a briefing.
        if (_menuFlow.OverShowcase && _arming is { IsArmed: false })
        {
            _arming.Step(IdleArmingBudgetMilliseconds);

            // The level is resident: the decoded pictures nobody took are dropped, and the staging
            // the uploads went through is given back rather than held for the next level.
            if (_arming.IsArmed)
            {
                _memory.ReleaseLoadLeftovers(_world, _detailMaps);
            }
        }

        // -entrycam presses Play the moment the backdrop it was pointed at is armed, which is the
        // state a player's press would find it in after a second of reading. Waiting for the arming
        // rather than only for the backdrop is what keeps the measurement honest.
        if (state.EntryCamPending && _menuFlow.OverShowcase && _menuBackdrop.Preview is { } entryBackdrop
            && entryBackdrop.MissionId == _options.EntryCamMission
            && _arming is { IsArmed: true })
        {
            // -entrytime: the time chosen first, as the dropdown chooses it, and Play a second later
            // over the backdrop that choice re-lit.
            if (_options.EntryTime != MissionTimeOfDay.Default && state.EntryTimeChosenAt < 0)
            {
                state.EntryTimeChosenAt = _loop.Clock.FrameCount;
                state.ShellHostForEntryCam?.SetMissionConditions(_conditions with { TimeOfDay = _options.EntryTime });
            }

            bool settled = state.EntryTimeChosenAt < 0
                || _loop.Clock.FrameCount - state.EntryTimeChosenAt >= EntryTimeSettleFrames;

            if (settled)
            {
                state.EntryCamPending = false;
                _logger.Information("Menu", $"-entrycam is pressing Play on mission {entryBackdrop.MissionId}.");
                state.ShellHostForEntryCam?.StartMission(
                    entryBackdrop.MissionId,
                    _configuration.Active?.Difficulty ?? (int)AiDifficulty.Normal);
            }
        }

        // -backdropcycle walks the campaign's rows on a fixed cadence, taking the same path a player
        // moving down the mission list takes: it asks the preview loader for the next mission and
        // lets the ordinary handover below adopt it. Latched on the frame count so each row is asked
        // for once, and only while the menu is actually up over a backdrop.
        if (_options.BackdropCycleFrames > 0 && _menuFlow.OverShowcase && state.PreviewLoader is not null)
        {
            long row = _loop.Clock.FrameCount / _options.BackdropCycleFrames;
            if (row != _menuFlow.BackdropCycleRow)
            {
                _menuFlow.BackdropCycleRow = row;
                int[] order = [.. _missions.InCampaignOrder().Select(m => m.Id)];
                if (order.Length > 0)
                {
                    int next = order[(int)(row % order.Length)];
                    if (next != _menuFlow.CurrentShowcaseMissionId)
                    {
                        _logger.Information("Menu", $"-backdropcycle: selecting mission {next}.");
                        _menuFlow.PendingPreviewMissionId = null;
                        _menuFlow.CurrentShowcaseMissionId = next;
                        RequestBackdrop(state.PreviewLoader, next);
                    }
                }
            }
        }

        // -presetcycle applies the next graphics preset on a fixed cadence, the way the settings page does.
        if (_options.PresetCycleFrames > 0)
        {
            long step = _loop.Clock.FrameCount / _options.PresetCycleFrames;
            if (step != _menuFlow.PresetCycleStep)
            {
                _menuFlow.PresetCycleStep = step;
                GraphicsPreset[] presets = [GraphicsPreset.Ultra, GraphicsPreset.Vanilla, GraphicsPreset.High, GraphicsPreset.Enhanced];
                GraphicsPreset preset = presets[(int)(step % presets.Length)];
                if (GraphicsPresetRegistry.Default.IsAllowed(preset))
                {
                    _logger.Information("Menu", $"-presetcycle: applying {preset}.");
                    GraphicsPresetRegistry.Default.Apply(preset);
                }
            }
        }

        // The build request, once the campaign selection has held still for a moment. The debounce is
        // only there to stop a run down the list queueing fourteen builds; it costs the render thread
        // nothing either way, because the build happens on a worker.
        if (_menuFlow.MenuMode
            && !_menuFlow.PauseMenu
            && _menuFlow.PendingPreviewMissionId is { } previewMissionId
            && _loop.TimeSource.Milliseconds - _menuFlow.PreviewRequestedAtMs > PreviewSettleMs)
        {
            _menuFlow.PendingPreviewMissionId = null;
            RequestBackdrop(state.PreviewLoader, previewMissionId);
        }

        // A backdrop whose mission's files were still arriving, asked for again until they are there.
        if (_menuFlow.OverShowcase && _backdropAwaitingFiles is { } awaitingFiles)
        {
            RequestBackdrop(state.PreviewLoader, awaitingFiles);
        }

        // The handover. Everything a backdrop is made of was built off the render thread; what is left
        // is the part that cannot be — a GL texture belongs to the context that made it — so the
        // renderers are constructed here, on the frame after the outgoing picture was held. Same shape
        // as the probe and distance-field bakes above.
        if (_menuFlow.OverShowcase && _menuFlow.HeldFrameReady)
        {
            bool changed = false;
            if (state.PreviewLoader is { } loader
                && loader.TryCollect(out MissionPreview? readyPreview) && readyPreview is not null)
            {
                _menuFlow.CurrentShowcaseMissionId = readyPreview.MissionId;
                AdoptPreview(readyPreview);
                changed = true;

                // Under the held frame, where the pause is not seen: the outgoing level and the
                // incoming one's decoding have just been through the heap.
                _memory.TrimAfterLoad();

                // A soak reports where memory stands every tenth swap, so growth shows as a trend.
                if (_options.BackdropCycleFrames > 0 && ++_menuFlow.BackdropSwaps % 10 == 0)
                {
                    _memory.Report($"After {_menuFlow.BackdropSwaps} backdrop swap(s)");
                }

                // Once the first backdrop is up, every other mission's files are read through in the
                // background, so whichever the player settles on next builds from memory.
                if (!_menuFlow.MissionsSwept)
                {
                    _menuFlow.MissionsSwept = true;
                    state.MissionWarmer?.WarmAll(_missions.InCampaignOrder().Select(mission => mission.Id));
                }
            }
            else if (_showcaseCamera is { IsCutDue: true } showcase)
            {
                // No new level, just the next place in this one. The camera does not take the cut
                // itself, so that it happens here — after the outgoing frame has been held.
                showcase.CommitCut();
                changed = true;
            }
            else if (_menuFlow.BakedFieldForBackdrop is { } landedField)
            {
                // The backdrop's field, baked behind the menu, adopted under the held frame so the
                // level's cross-over to its real-time lighting is dissolved to rather than watched.
                _menuFlow.BakedFieldForBackdrop = null;
                _gi.AdoptDistanceField(landedField.Scene, landedField.Atlas, fromCache: false);
                changed = true;
            }

            _menuFlow.HeldFrameReady = false;

            // The held frame is now the whole picture; it dissolves off to reveal what is behind it,
            // once the lighting behind it has converged.
            if (changed)
            {
                _menuFlow.Dissolve.SnapTo(_menuFlow.HeldFrameIsPicture ? 1f : 0f);
                _menuFlow.DissolveRevealPending = _menuFlow.HeldFrameIsPicture;
            }
        }
    }
}
