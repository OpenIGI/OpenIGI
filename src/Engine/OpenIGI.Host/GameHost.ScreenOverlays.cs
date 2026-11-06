using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Hud;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The retail HUD and the remaster's, built with the player's loadout.
    private IHudPresenter? _hud;
    private IRemasteredHudPresenter? _remasteredHud;

    // The title's mission HUD, made from the title's presentation the first time a frame draws it.
    private IMissionHudOverlay? _hudOverlay;

    // The interaction marker's own wall clock: which offer is on screen and how far it has faded up.
    // Kept on the host for the same reason as the binocular optics -- a fade measured in real seconds cannot live on a
    // renderer that is only asked to draw.
    private readonly InteractionPromptAnimation _interactionPrompt = new();
    private int _lastInteractionPromptMilliseconds;

    /// <summary>Settles what the HUD shows over the frame, and scans the binocular detector once.</summary>
    private void ResolveFrameHud(ref FrameView view)
    {
        SightZoomFrame sight = view.Sight;
        bool smoothPresentation = view.SmoothPresentation;
        float tickBlend = view.TickBlend;
        Camera3D eye = view.Eye;

        // The remastered-optics decisions and the binocular detector are resolved here, before
        // the soldiers are drawn, for two reasons: the detector is stateful and must be scanned
        // exactly once per frame, and the through-wall glow pass below has to mark the very
        // contacts it finds while the scene is still being captured. The HUD after the resolve
        // reuses this same target list rather than advancing the detector a second time.
        bool remasterHud = RemasteredHudPresentation.UsesRemasteredHud(HudCVars.RemasteredHud.Value);
        bool remasteredOptics = RemasteredOpticsUp();
        bool tacticalMessages = RemasteredHudPresentation.UsesTacticalMessages(
            remasterHud,
            HudCVars.TacticalMessages.Value);
        bool showPlayerHud = _playerMode && !_computerOpen && !_entry.IsCameraActive
            && _mission.Cutscenes?.IsPlaying != true
            && Viewpoint?.IsDeathViewActive != true
            && Viewpoint?.HasPlayerHud == true;

        // On a stand retail draws no HUD at all -- captured in mission 4, the stance figure shown
        // beside the gun is gone the moment its sight takes the view -- hence the last condition.
        bool showFirstPersonHud = showPlayerHud
            && Viewpoint?.HasFirstPersonHud == true;

        // The arms and the held weapon hang off the eye, so the moment the camera leaves it they
        // are either filling the frame or behind it. When they stand down, and when they come
        // back, is the camera's own call -- see ThirdPersonCamera.ShowsViewModel.
        bool showViewModel = showFirstPersonHud && Viewpoint?.ShowsViewModel != false;

        // What the interaction resolver is offering, advanced on its own wall clock so the
        // marker fades at the same rate whatever the frame rate. Gated on showPlayerHud rather
        // than showFirstPersonHud on purpose: a timed interaction hides the hands and the
        // weapon, and that is exactly when the ring filling on the object is what the player
        // is reading.
        bool worldInteractionPrompt = InteractionPromptPresentation.UsesWorldPrompt(
            remasterHud,
            HudCVars.InteractionPrompt.Value);
        int interactionPromptNow = _loop.TimeSource.Milliseconds;
        float interactionPromptElapsed =
            Math.Clamp((interactionPromptNow - _lastInteractionPromptMilliseconds) / 1000f, 0f, 0.1f);
        _lastInteractionPromptMilliseconds = interactionPromptNow;
        _interactionPrompt.Update(
            worldInteractionPrompt && showPlayerHud
                ? Viewpoint?.InteractionPrompt(smoothPresentation ? tickBlend : 1f)
                : null,
            interactionPromptElapsed);

        // The remastered detector's range grows with the zoom: from the base range at the
        // default field of view up to the zoomed range at the tightest magnification, so leaning
        // into the optics resolves a distant figure the wide view does not. The vanilla optics
        // stay retail-uncapped (positive infinity).
        float binocularZoomIn = Viewpoint?.BinocularZoomIn(sight.TanX) ?? 0f;
        float binocularDetectionRange = remasteredOptics
            ? HudCVars.BinocularRange.Value
                + ((HudCVars.BinocularRangeZoomed.Value - HudCVars.BinocularRange.Value) * binocularZoomIn)
            : float.PositiveInfinity;
        IReadOnlyList<EnemyOverlayBox> binocularTargets =
            ((showFirstPersonHud && Viewpoint?.IsBinocularsOpen == true) || TitleBinocularsOpen)
            && _soldiers is { } binocularSoldiers
                ? binocularSoldiers.BinocularTargets(
                    // Use the same presented eye that rendered the world. The detector owns
                    // authoritative slot lifetime, but its screen rectangle must be projected
                    // through the frame's smoothed camera or moving/zooming binoculars make the
                    // box visibly lag behind the soldier it is meant to surround.
                    eye,
                    view.HorizontalTangent,
                    _window.Width,
                    _window.Height,
                    // FUN_00471F60/FUN_00471B60 use Sys_GetTickCount (the retail
                    // millisecond counter), not the 30 Hz simulation index. Passing
                    // TickCount here stretched the native 250 ms entry pulse and
                    // 3000 ms retention window by a factor of thirty-three.
                    _loop.Clock.Now,
                    binocularDetectionRange,
                    _loop.Clock.TickCount)
                : [];

        view.RemasterHud = remasterHud;
        view.RemasteredOptics = remasteredOptics;
        view.TacticalMessages = tacticalMessages;
        view.ShowPlayerHud = showPlayerHud;
        view.ShowFirstPersonHud = showFirstPersonHud;
        view.ShowViewModel = showViewModel;
        view.WorldInteractionPrompt = worldInteractionPrompt;
        view.BinocularTargets = binocularTargets;
    }

    /// <summary>What the mission's HUD draws from this frame.</summary>
    private MissionHudFrame HudFrame(
        FrameLoopState state,
        bool remasterHud,
        bool remasteredOptics,
        bool showFirstPersonHud,
        bool showPlayerHud,
        bool tacticalMessages,
        bool worldInteractionPrompt,
        IReadOnlyList<EnemyOverlayBox> binocularTargets,
        float? binocularRangeMetres)
    {
        _hudOverlay ??= _presentation.Presenters.Create<MissionPresentationRequest, IMissionHudOverlay>(
            new MissionPresentationRequest(_device, _hosted, _logger));
        return new MissionHudFrame(
            _window.Width,
            _window.Height,
            SafeWindowWidth,
            SafeWindowHeight,
            UiScale.Resolve(_options.UiScale, _window.Width, _window.Height),
            _loop.Clock.TickCount,
            _binocularOptics.Seconds,
            remasterHud,
            remasteredOptics,
            showFirstPersonHud,
            showPlayerHud,
            tacticalMessages,
            worldInteractionPrompt,
            binocularTargets,
            binocularRangeMetres,
            _hud,
            _remasteredHud,
            // The original HUD's own size: hud_retail_scale, or a -uiscale given on the command line over it.
            UiScale.Resolve(
                _options.UiScale != UiScale.Automatic ? _options.UiScale : HudCVars.RetailHudScale.Value,
                _window.Width,
                _window.Height));
    }

    /// <summary>The mission's status messages, for a title's player standing in it in the mission's own player's stead.</summary>
    /// <returns>The messages, or none where the mission has its own player or no messages.</returns>
    /// <remarks>
    /// Another game's player standing in the mission is not the mission's player, so the mission's HUD stands down for it; its
    /// radio and status lines -- Anya's, in the first mission -- are the mission's to say all the same, and the player's own
    /// display shows them in its own look.
    /// </remarks>
    private List<StatusMessageDisplay> GuestMissionMessages()
    {
        _guestMessages.Clear();
        if (HostedPlayer is null && _mission.Objectives is { } runtime)
        {
            _guestMessages.AddRange(runtime.DisplayedMessages);
        }

        return _guestMessages;
    }

    private readonly List<StatusMessageDisplay> _guestMessages = [];

    /// <summary>Draws what the mods lay over the player's view, after the game's HUD and status and before a cut-scene's framing.</summary>
    /// <param name="state">The frame loop's state, whose sprite batch they draw into.</param>
    /// <param name="view">The frame's view, whose eye and projection place their markers.</param>
    private void DrawModHudOverlays(FrameLoopState state, in FrameView view)
    {
        if (_hudOverlays.Count == 0 || !_playerMode || _computerOpen || _mission.Cutscenes?.IsPlaying == true
            || _shellType?.Body is not { } font)
        {
            return;
        }

        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        UiPainter painter = UiPainter.Create(state.Batch, new LayoutContext(_window.Width, _window.Height));
        HudOverlayFrame frame = new(painter, font, _window.Width, _window.Height, view.Eye, view.Projection, _loop.Clock.TickCount);
        foreach (IHudOverlay overlay in _hudOverlays)
        {
            overlay.Draw(frame);
        }

        state.Batch.End();
    }

    /// <summary>Resolves the frame and draws everything 2D over it: the HUD, the messages, the computer, the damage wash and the flash.</summary>
    private void DrawScreenOverlays(
        FrameLoopState state, bool captured, in FrameView view, float? binocularRangeMetres, bool cameraIndoors)
    {
        PostProcessSettings postProcess = view.PostProcess;
        bool remasterHud = view.RemasterHud;
        bool remasteredOptics = view.RemasteredOptics;
        bool showFirstPersonHud = view.ShowFirstPersonHud;
        IReadOnlyList<EnemyOverlayBox> binocularTargets = view.BinocularTargets;
        bool tacticalMessages = view.TacticalMessages;
        bool showPlayerHud = view.ShowPlayerHud;
        bool smoothPresentation = view.SmoothPresentation;
        float tickBlend = view.TickBlend;
        SightZoomFrame sight = view.Sight;
        bool worldInteractionPrompt = view.WorldInteractionPrompt;
        Camera3D eye = view.Eye;
        float horizontalTangent = view.HorizontalTangent;
        float aspectRatio = view.AspectRatio;

        // Everything 2D belongs AFTER the resolve, on the back buffer: the HUD, the damage wash,
        // the menus and the console are authored in display colours and must never be tone mapped.
        // Unconditional, and outside the player-mode block on purpose -- a free-camera frame draws
        // no view model, and leaving the capture open would strand it until the next frame threw.
        if (!captured)
        {
            _device.EndStageTiming();
        }

        if (captured)
        {
            _device.EndPass();
            _device.EndSceneCapture();
            UpdateModPostPasses();
            _device.ResolveScene(postProcess);
            captured = false;
            EndStageIfSlow("the resolve (tone map, bloom, depth of field, temporal)");
        }

        _device.BeginStageTiming(RenderStage.UserInterface);

        // StatusScreen is a display-space layer. Keep every retail HUD component out of the
        // captured scene so camera motion blur, depth of field, bloom and tone mapping can only
        // affect the world and first-person rig. The damage and flash washes intentionally remain
        // later in the draw order and still cover the HUD exactly as they did before this separation.
        if (TitlePlayer is { } titleHudPlayer && _titleHud is { } titleHud
            && _playerMode && !_computerOpen && !_entry.IsCameraActive && _mission.Cutscenes?.IsPlaying != true
            && Viewpoint?.ShowsViewModel != false)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            titleHud.ShowMissionMessages(GuestMissionMessages());
            titleHud.Draw(state.Batch, titleHudPlayer, _window.Width, _window.Height, _options.UiScale);
            state.Batch.End();
        }

        MissionHudFrame hudFrame = HudFrame(state, remasterHud, remasteredOptics, showFirstPersonHud, showPlayerHud, tacticalMessages, worldInteractionPrompt, binocularTargets, binocularRangeMetres);
        _hudOverlay?.DrawWeaponHud(state.Batch, hudFrame);
        DrawRemasteredBinoculars(state, view, binocularRangeMetres);

        _hudOverlay?.DrawRetailStatus(state.Batch, hudFrame);
        DrawModHudOverlays(state, view);

        if (CutsceneFrameShown is { } cinematicFraming)
        {
            CutsceneFramingRenderer.Draw(
                _device, state.Batch, cinematicFraming, _window.Width, _window.Height, _hud, _loop.Clock.TickCount);
        }

        // The original's computer: its screen's far layers, the device held before the eye, then the controls and the pointer
        // nearer than the casing. The tactical interface has no casing; it draws all of itself at once.
        if (_computerOpen && TacticalComputer is null && _mapComputer is { IsReady: true } retailComputer)
        {
            retailComputer.Scene = _scene;
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            retailComputer.DrawBehindDevice(state.Batch, _window.Width, _window.Height);
            state.Batch.End();

            // 0x46BB10 gives the computer task the mapcomputer model only while the level grants the map.
            if (retailComputer.ShowsDevice
                && _mapComputerDevice is { IsReady: true } deviceModel
                && _playerMode
                && Viewpoint?.IsDeathViewActive != true
                && Viewpoint?.WalkingEye(smoothPresentation, tickBlend) is { } computerEye)
            {
                // Drawn through the 4:3 lens spread across the window, as retail draws it at every shape: the
                // screens' pages are laid out across the whole window, so the casing has to frame the whole window
                // too. Fitting it Hor+ like the rig would leave the pages spilling past its sides.
                deviceModel.DrawHeld(
                    computerEye,
                    ViewModelProjection.Create(HumanView.DefaultTanX, FarPlane, WidescreenLens.AuthoredAspect),
                    _world?.Shading.Sun);
                _device.SetDepthState(DepthState.Disabled);
            }

            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            retailComputer.DrawInFrontOfDevice(state.Batch, _window.Width, _window.Height);
            state.Batch.End();
        }
        else if (_computerOpen
            && TacticalComputer is { } tacticalChrome
            && ComputerObjectives is { } computerObjectives
            && ComputerBodyUnits is { } computerBody)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            tacticalChrome.Draw(
                state.Batch,
                computerObjectives,
                computerBody,
                _soldiers?.MapContacts() ?? [],
                _computerHighlights?.Visible() ?? [],
                _window.Width,
                _window.Height,
                _computerFlight.ChromeOpacity);
            state.Batch.End();
        }

        _hudOverlay?.DrawWashes(state.Batch, hudFrame);

        _hudOverlay?.DrawRemasteredHud(state.Batch, hudFrame);

        // The marker itself, over the finished frame and over the HUD. It is a surface standing
        // in the level rather than an overlay on the picture, so it is handed the same presented
        // eye and the same lens that drew the thing it belongs to, and it projects itself.
        if (worldInteractionPrompt
            && _interactionPrompt.Visible is { } promptTarget
            && _remasteredHud is { IsReady: true } promptHud)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            promptHud.DrawInteractionPrompt(
                state.Batch,
                new InteractionPromptView(
                    promptTarget.AnchorUnits / WorldScene.UnitsPerMetre,
                    _interactionPrompt.Opacity,
                    _interactionPrompt.Pop,
                    InteractionPromptPresentation.BadgeLabel(
                        PlayerProfileInputBindings.BoundKeyName(
                            _configuration.Active,
                            PlayerInputBindings.ConfigName(PlayerAction.Activate))),
                    InteractionPromptPresentation.Title(promptTarget),
                    InteractionPromptPresentation.Detail(promptTarget),
                    InteractionPromptPresentation.RingProgress(promptTarget)),
                eye,
                horizontalTangent,
                aspectRatio,
                SafeWindowWidth,
                SafeWindowHeight);
            state.Batch.End();
        }

        // A session's scoreboard, while its key is held: over everything but the console.
        if (TickObserver is ISessionScoreboard session
            && session.CurrentScoreboard is { } board
            && _remasteredHud is { IsReady: true } boardHud
            && _scoreboardUp)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            boardHud.DrawScoreboard(
                state.Batch, board, session.LocalEntity, session.RoomCode, session.Place, SafeWindowWidth, SafeWindowHeight, ScoreboardHand(board), session.Relayed, session.Mode);
            state.Batch.End();
        }

        // The fallen teammates, each marked where it lies with the same prompt anything usable gets: its ring is the revival.
        if (_remasteredHud is { IsReady: true } reviveHud && _soldiers?.ReviveMarkers() is { Count: > 0 } fallen)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            foreach (Game.Ai.ReviveMarker mark in fallen)
            {
                reviveHud.DrawInteractionPrompt(
                    state.Batch,
                    new InteractionPromptView(
                        mark.AnchorMetres,
                        1f,
                        1f,
                        "+",
                        $"Revive {mark.Name}",
                        mark.Progress > 0f
                            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Reviving {mark.Progress * 100f:0}%, stay close")
                            : "Stand beside them for 5 seconds",
                        mark.Progress),
                    eye,
                    horizontalTangent,
                    aspectRatio,
                    SafeWindowWidth,
                    SafeWindowHeight);
            }

            state.Batch.End();
        }

        // This player fallen in co-op: down, not out, and how far a teammate has got with bringing it back.
        if (TickObserver is ISessionScoreboard { OwnRevive: { } revival } && _remasteredHud is { IsReady: true } downedHud)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            downedHud.DrawDowned(state.Batch, revival, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // A match's HUD -- the score strip, the bomb, a plant under way, a decided round's card -- and, out of the map's bounds,
        // the word to get back. A session with no HUD of its own shows the plain banner.
        if (TickObserver is ISessionScoreboard scored && _remasteredHud is { IsReady: true } matchHud
            && (CurrentMatchHud() is { } modeHud ? (object)modeHud : scored.Banner) is { } shown)
        {
            string? warning = TickObserver is Multiplayer.ISessionMode { Layout: { Bounds.Length: > 0 } layout }
                && TitlePlayer is { } own && !layout.InBounds(own.Position)
                ? "RETURN TO THE MATCH"
                : null;
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            if (shown is Game.Multiplayer.Modes.MatchHud hud)
            {
                string useKey = PlayerProfileInputBindings.BoundKeyName(_configuration.Active, PlayerInputBindings.ConfigName(PlayerAction.Activate)) ?? "E";
                matchHud.DrawMatchHud(state.Batch, hud, warning, useKey, SafeWindowWidth, SafeWindowHeight, BuyKeyName());
                if (_buyMenuUp && hud is { BuyClock: { } buyClock, Money: { } money })
                {
                    matchHud.DrawBuyMenu(
                        state.Batch, BuyItems, money, buyClock, (_window.Input.Mouse.X, _window.Input.Mouse.Y), SafeWindowWidth, SafeWindowHeight);
                }
            }
            else if (shown is Game.Multiplayer.Modes.MatchBanner banner)
            {
                matchHud.DrawMatchBanner(state.Batch, banner, warning, SafeWindowWidth, SafeWindowHeight);
            }

            state.Batch.End();
        }

        // Dead in a match, watching a living player.
        if (Spectated() is { } watched && _remasteredHud is { IsReady: true } spectateHud)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            spectateHud.DrawSpectating(state.Batch, watched.Name, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // The connection, as net_graph asks.
        if (Game.Multiplayer.NetCVars.NetGraph.Value && TickObserver is ISessionScoreboard { NetGraph: { Count: > 0 } graph } && _remasteredHud is { IsReady: true } graphHud)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            graphHud.DrawNetGraph(state.Batch, graph, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // The local player's fight: the hit marker and where shots that struck it came from.
        if (TickObserver is ISessionScoreboard fighting && TitlePlayer is { } eyes && _remasteredHud is { IsReady: true } combatHud
            && fighting.Combat(eyes.EyePosition, eyes.ViewOrientation) is { } combat)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            combatHud.DrawCombat(state.Batch, combat, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // The kills of late, down the top right corner.
        if (TickObserver is ISessionScoreboard { KillFeed: { Count: > 0 } kills } && _remasteredHud is { IsReady: true } feedHud)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            feedHud.DrawKillFeed(state.Batch, kills, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // Co-op talk: the lines said and being typed, and the places marked.
        string? linkWarning = TickObserver is ISessionScoreboard { PoorConnection: true } ? "Poor connection to the host" : null;
        Game.Multiplayer.ScoreboardLine[] talkLines = (TickObserver as ISessionScoreboard)?.CurrentScoreboard?.Lines.ToArray() ?? [];
        string[] talking = [.. Talking().Select(e => e < 0 ? "You" : talkLines.FirstOrDefault(l => l.Entity == e).Name ?? "Teammate")];
        if (_remasteredHud is { IsReady: true } talkHud && Talk is not null && CoopTalkShown() is var (chat, talkMarks)
            && ModePointMarks(talkMarks) is var marks
            && (chat.Count > 0 || marks.Count > 0 || _chatEntry is not null || linkWarning is not null || talking.Length > 0))
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            talkHud.DrawCoopTalk(state.Batch, chat, _chatEntry, linkWarning, talking, marks, eye, horizontalTangent, aspectRatio, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // The other players' names over their heads, on the same eye and lens as the marker.
        if (_remasteredHud is { IsReady: true } tagHud && _soldiers?.NameTags() is { Count: > 0 } tags)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            tagHud.DrawNameTags(state.Batch, tags, eye, horizontalTangent, aspectRatio, SafeWindowWidth, SafeWindowHeight);
            state.Batch.End();
        }

        // In play the mission's messages are the remastered display's, each title's in its own look (Igi1HudOverlay and the
        // title's own display); the cards below stand in only for a cutscene's subtitles where the original's face is missing.

        // Cutscene StatusMessages are subtitles, not HUD. They remain visible while the authored
        // camera owns the frame even though the arms, weapon and ordinary mission UI are hidden.
        if (_mission.Cutscenes?.IsPlaying == true && _mission.Objectives is { } subtitleRuntime)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            // Retail cutscenes always use StatusMessageScreen's shipped bitmap face and its
            // left-aligned 620/640-width line. TacticalMessages only replaces the in-game feed;
            // letting it replace dialogue produced a centred modern subtitle unlike the original.
            if (_hud is { IsReady: true } subtitleHud)
            {
                subtitleHud.DrawStatusMessages(
                    state.Batch,
                    subtitleRuntime.DisplayedMessages,
                    _window.Width,
                    _window.Height,
                    // Retail scales this full-width normalized subtitle face with framebuffer
                    // height. Keeping the HUD's integer auto scale here made a 938px capture use
                    // the unscaled 480p glyphs, less than half the size of the original.
                    // The retail text objects use the shipped face at their 0.9 presentation
                    // scale; retain that calibration while growing it with framebuffer height.
                    Math.Max(1f, (_window.Height / (float)UiScale.AuthoredHeight) * 0.87f));
            }
            else if (_messages is { IsReady: true } subtitleCards)
            {
                subtitleCards.Draw(state.Batch, subtitleRuntime.DisplayedMessages, _window.Width, _window.Height);
            }
            state.Batch.End();
        }

        DrawTouchControls(state);
        DrawCutsceneSkip(state);

        _device.EndStageTiming();

        _debugOverlays.Draw(state, eye, cameraIndoors, horizontalTangent);
    }

    // The places a mode plays for -- search and destroy's bomb sites -- marked as a player's marks are, always up, so they are
    // found from anywhere: held on the screen's edge when out of view, with their distance.
    private IReadOnlyList<HudMark> ModePointMarks(IReadOnlyList<HudMark> marks)
    {
        if (TickObserver is not Multiplayer.ISessionMode { Mode: Game.Multiplayer.Modes.GameModes.SearchAndDestroy, Layout: { } layout })
        {
            return marks;
        }

        List<HudMark> all = [.. marks];
        Game.Multiplayer.Modes.ModeReport? report = (TickObserver as ISessionScoreboard)?.ModeReport;
        if (report?.BombAt is { } bomb)
        {
            // Planted: the bomb is all that matters.
            all.Add(new HudMark((bomb / WorldScene.UnitsPerMetre) + new Vector3d(0, 0, 1.0), "BOMB", 1f));
            return all;
        }

        foreach (Game.Multiplayer.Modes.MpPoint point in layout.Points)
        {
            if (point.Kind == Game.Multiplayer.Modes.MpPointKind.BombSite)
            {
                Vector3d metres = new Vector3d(point.Position[0], point.Position[1], point.Position[2]) / WorldScene.UnitsPerMetre;
                all.Add(new HudMark(metres + new Vector3d(0, 0, 2.5), $"SITE {point.Id}", 1f));
            }
        }

        return all;
    }
}
