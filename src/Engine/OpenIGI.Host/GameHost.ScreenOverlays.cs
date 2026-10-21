using OpenIGI.Audio;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Frontend.Saves;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Host.Audio;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Diagnostics;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Host.Titles;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Presentation;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using OpenIGI.UI;
using OpenIGI.Host.Services;

using System.Diagnostics;

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
        bool remasterHud = RemasteredHudPresentation.UsesRemasteredHud(RenderCVars.RemasteredHud.Value);
        bool remasteredOptics = RemasteredHudPresentation.UsesRemasteredOptics(
            TacticalComputer is not null,
            _remasteredHud is { IsReady: true });
        bool tacticalMessages = RemasteredHudPresentation.UsesTacticalMessages(
            remasterHud,
            RenderCVars.TacticalMessages.Value);
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
            RenderCVars.InteractionPrompt.Value);
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
            ? RenderCVars.BinocularRange.Value
                + ((RenderCVars.BinocularRangeZoomed.Value - RenderCVars.BinocularRange.Value) * binocularZoomIn)
            : float.PositiveInfinity;
        IReadOnlyList<EnemyOverlayBox> binocularTargets =
            showFirstPersonHud
            && Viewpoint?.IsBinocularsOpen == true
            && _soldiers is { } binocularSoldiers
                ? binocularSoldiers.BinocularTargets(
                    // Use the same presented eye that rendered the world. The detector owns
                    // authoritative slot lifetime, but its screen rectangle must be projected
                    // through the frame's smoothed camera or moving/zooming binoculars make the
                    // box visibly lag behind the soldier it is meant to surround.
                    eye,
                    sight.TanX,
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
            _remasteredHud);
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
            titleHud.Draw(state.Batch, titleHudPlayer, _window.Width, _window.Height);
            state.Batch.End();
        }

        MissionHudFrame hudFrame = HudFrame(state, remasterHud, remasteredOptics, showFirstPersonHud, showPlayerHud, tacticalMessages, worldInteractionPrompt, binocularTargets, binocularRangeMetres);
        _hudOverlay?.DrawWeaponHud(state.Batch, hudFrame);

        _hudOverlay?.DrawRetailStatus(state.Batch, hudFrame);

        if (_mission.CutsceneFrameShown is { } cinematicFraming)
        {
            CutsceneFramingRenderer.Draw(
                _device, state.Batch, cinematicFraming, _window.Width, _window.Height, _hud, _loop.Clock.TickCount);
        }

        // ComputerMap renders the terrain through its own top-down camera and turns only that
        // captured view into green phosphor. The SIMOTECH casing is a separate retail panel that
        // is drawn after the resolve, in its authored colours, before the HUD fills its aperture.
        //
        // The tactical interface has no casing at all — that is the point of it — so this whole
        // block is the retail path's alone.
        if (_computerOpen
            && TacticalComputer is null
            && _playerMode
            && Viewpoint?.IsDeathViewActive != true
            && Viewpoint?.WalkingEye(smoothPresentation, tickBlend) is { } computerEye)
        {

            Matrix4f computerViewModelProjection = ViewModelProjection.Create(
                sight.ViewModelTanX,
                FarPlane);

            // 0x46BB10 gives the computer task the mapcomputer model only while the level grants the
            // device; without it the pages are drawn with nothing held.
            if (_mapComputerDevice is { IsReady: true } deviceModel && _mapComputer is { MapAvailable: true })
            {
                Viewport deviceViewport = _mapComputer!.DeviceViewport(_window.Width, _window.Height);
                float horizontalHalfTangent = sight.ViewModelTanX;
                // The mapcomputer mesh is a close foreground prop. Its housing extends past the
                // view just as it does in retail; 2.50 is the retail asset's aperture-to-frame
                // calibration against the map task's 91% screen viewport. The casing is intentionally
                // cropped at the display edges: leaving its outside black behind the four corners
                // makes the computer look set back from the camera rather than held in the foreground.
                float horizontalCoverage = deviceViewport.Width / (float)Math.Max(1, _window.Width) * 2.50f;
                deviceModel.Draw(computerEye, computerViewModelProjection, horizontalHalfTangent, horizontalCoverage);
            }

            _device.SetDepthState(DepthState.Disabled);
        }

        if (_computerOpen
            && _mission.Objectives is { } objectiveRuntime
            && Viewpoint?.BodyPositionUnits is { } computerBody)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            if (TacticalComputer is { } tacticalChrome)
            {
                tacticalChrome.Draw(
                    state.Batch,
                    objectiveRuntime,
                    computerBody,
                    _soldiers?.MapContacts() ?? [],
                    _computerHighlights?.Visible() ?? [],
                    _window.Width,
                    _window.Height,
                    _computerFlight.ChromeOpacity);
            }
            else if (_mapComputer is { IsReady: true } computer)
            {
                computer.Draw(
                    state.Batch,
                    objectiveRuntime,
                    computerBody,
                    _soldiers?.MapContacts() ?? [],
                    _computerHighlights?.Visible() ?? [],
                    _window.Width,
                    _window.Height);
            }

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

        // The HUD is in the condition, not asserted inside it. Every presenter is nullable so a
        // title under construction draws the passes it has and skips the rest -- and I.G.I. 2 has
        // no HUD at all yet, so the null-forgiving read of it here took the process down as soon
        // as one of its missions had an objective to post.
        if (tacticalMessages
            && showFirstPersonHud
            && _remasteredHud is { } messageHud
            && _mission.Objectives is { } modernMessages
            && _messages is { IsReady: true } modernCards)
        {
            _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
            state.Batch.Begin();
            float topOffset = new LayoutContext(_window.Width, _window.Height)
                .Px(messageHud.MessageTopInset);
            modernCards.Draw(
                state.Batch,
                modernMessages.DisplayedMessages.Where(static message => !message.IsCutscene),
                _window.Width,
                _window.Height,
                topOffset);
            state.Batch.End();
        }

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

        _device.EndStageTiming();

        _debugOverlays.Draw(state, eye, cameraIndoors);
    }
}
