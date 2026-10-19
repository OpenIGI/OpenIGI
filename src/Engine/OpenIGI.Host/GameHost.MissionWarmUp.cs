using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Frontend;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Rendering;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>The mission-name table, so the card titles a level the way the campaign screen did.</summary>
    private MenuStringTable? _menuStrings;

    /// <summary>The shell's mission list, for the number and the briefing the card sets under the title.</summary>
    private IReadOnlyList<MissionInfo>? _shellMissions;

    /// <summary>The 2D batch, held so a frame can be drawn from outside the render loop.</summary>
    private SpriteBatch? _batch;

    /// <summary>When the level may be shown: once its lighting exists, has converged and has settled.</summary>
    private MissionWarmUpGate _warmUp = new(0);

    /// <summary>The going-in presentation: the departure, the card over the held picture, the reveal.</summary>
    private readonly MissionLoadSequence _loadSequence = new();

    /// <summary>The load the sequence will run once the departure has played out.</summary>
    private Action? _pendingLoad;

    /// <summary>The mission that load brings up, for the load gate to be asked about.</summary>
    private int _pendingLoadMission;

    // The backdrop asked for while its mission's files were still arriving; see RequestBackdrop.
    private int? _backdropAwaitingFiles;

    // The mission -mission asked for while its files were still arriving; see StartMissionWhenReadable.
    private int? _startAwaitingFiles;

    /// <summary>What the load gate last said it was waiting for, so it is reported once rather than every frame.</summary>
    private string? _loadGateWaitingFor;

    /// <summary>The briefing, posted on the nearest surface at the start. See r_world_briefing.</summary>
    private WorldTitleRenderer? _briefingPost;
    private bool _briefingPosted;
    private int _lastBriefingMilliseconds;

    /// <summary>How far from the start a surface is looked for to post the briefing on, in metres.</summary>
    private const double BriefingPostReachMetres = 30.0;

    /// <summary>How far off the surface the briefing stands, in metres, so it never fights the paint.</summary>
    private const double BriefingPostStandOffMetres = 0.08;

    /// <summary>Above this a standing surface is a building rather than a fence, in metres over the eye.</summary>
    private const double FenceTopAboveEyeMetres = 2.2;

    /// <summary>How long the notice stays, once it has arrived, before it burns away, in seconds.</summary>
    private const float BriefingHoldSeconds = 5f;

    /// <summary>How long the notice takes to burn away, in seconds.</summary>
    private const float BriefingBurnSeconds = 1.6f;

    /// <summary>How long a notice nobody has looked at is kept before it burns anyway, in seconds.</summary>
    private const float BriefingUnseenPatienceSeconds = 90f;

    /// <summary>The mission being loaded, for the card.</summary>
    private MissionDescriptor? _loadingMission;
    private MissionInfo? _loadingInfo;

    /// <summary>How many of the blocking load's stages have finished, for the bar while it blocks.</summary>
    private int _loadStageIndex;

    private int _lastWarmUpMilliseconds;
    private int _lastLoadingPresentMilliseconds;
    private int _warmUpFramesDrawn;

    /// <summary>How many named stages <c>BringMissionUp</c> reports, so each moves the bar by one share.</summary>
    private const int LoadStageCount = 14;

    /// <summary>The fewest milliseconds between two loading frames presented from inside a blocking load.</summary>
    /// <remarks>
    /// With vertical sync on every present waits for a refresh, so presenting at every stage would add
    /// a refresh per stage to the load. Throttled, the card still animates a few times a second and the
    /// window never goes long enough without a present to be marked unresponsive.
    /// </remarks>
    private const int LoadingPresentIntervalMilliseconds = 40;

    private static readonly Bgra32 CardWhite = UiPalette.Text;
    private static readonly Bgra32 CardAccent = UiPalette.Accent;
    private static readonly Bgra32 CardMuted = UiPalette.TextSecondary;
    private static readonly Bgra32 CardDim = UiPalette.TextMuted;

    /// <summary>Whether the level is still being held and must not be revealed yet.</summary>
    private bool IsWarmingUp => _warmUp.IsHolding;

    /// <summary>
    /// Whether the presentation still owns the level -- it is withheld, the card is up, or the shot has
    /// the camera -- so nothing in it may run yet: a cut-scene that started under the card, or a patrol
    /// that walked off during the hold, would be one the player never saw begin.
    /// </summary>
    /// <remarks>
    /// Released as the reveal BEGINS rather than as it ends: the level's intro has to be on its first
    /// frame by the time the picture clears, and it cannot be until the level has ticked. Under the
    /// dissolve its first moments are seen arriving, which is what a dissolve is for; after it they would
    /// be a second of the player's own view before the cut.
    /// </remarks>
    private bool PresentationHoldsLevel =>
        IsWarmingUp
        || _loadSequence.Phase is MissionLoadPhase.Depart or MissionLoadPhase.Hold
        || _entry.IsCameraActive;

    /// <summary>How long the player has had the level, with no intro playing, in seconds.</summary>
    private float _controlSeconds;

    /// <summary>How long the level must have been the player's before the briefing is posted, in seconds.</summary>
    /// <remarks>
    /// An authored intro takes a tick or two to put its first frame up after the level is released, and
    /// a notice posted in that gap would stand in the player's view for a moment and then be cut away
    /// from. Waiting this long is what tells an intro that is about to start from no intro at all.
    /// </remarks>
    private const float BriefingControlSeconds = 1.5f;

    /// <summary>Whether a loading screen may be drawn at all this run.</summary>
    /// <remarks>
    /// Never during a capture, for the same reason the console hides its pinned overlays there: a
    /// reference shot is taken at a numbered frame, <c>-screenshot</c> implies <c>-frames 5</c>, and a
    /// screen over frame five would put a loading card into every reference image and straight through
    /// the vanilla pixel gate. The warm-up itself still runs -- it draws no extra frames and moves no
    /// counters, it only withholds -- so a capture sees exactly the scene it always saw.
    /// </remarks>
    private bool ShowsLoadingPanel =>
        _options.ScreenshotPath is null && !_options.IsBenchmark && !_options.HasFlight;

    /// <summary>Whether a load may be presented as the full sequence rather than run on the spot.</summary>
    private bool CinematicLoadAvailable => ShowsLoadingPanel && !_options.Lockstep && _batch is not null;

    /// <summary>
    /// Starts the going-in sequence and defers the load until its departure has played.
    /// </summary>
    /// <param name="missionId">The mission the load brings up.</param>
    /// <param name="load">What brings the mission up: a promotion of the backdrop, or a cold start.</param>
    /// <remarks>
    /// The order is the whole design. The clean picture -- the level as the player sees it now, with no
    /// interface over it -- is held on the very next frame, before the interface is drawn; the interface
    /// then leaves and the picture falls out of focus over a beat; and only then does the render thread
    /// block on the load. Loading first and presenting after was the old order, and it froze the menu,
    /// interface and all, for the length of the load.
    /// </remarks>
    private void BeginCinematicLoad(int missionId, Action load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _pendingLoad = load;
        _pendingLoadMission = missionId;
        _loadGateWaitingFor = null;
        _loadSequence.Begin(capturePicture: true);
    }

    /// <summary>Holds the clean picture if the sequence is waiting for one. Call after the scene, before the interface.</summary>
    /// <remarks>
    /// Once, for the still a departure defocuses; every frame while a live level is being held out of
    /// focus under its caption, so the backdrop pass is defocusing what the shot is looking at now.
    /// </remarks>
    private void CaptureLoadPictureIfWanted()
    {
        if (_loadSequence.WantsCapture)
        {
            _menuFlow.HoldFrame(_window.Width, _window.Height);
            _loadSequence.NotifyCaptured();
            return;
        }

        if (_loadSequence.WantsLivePicture)
        {
            _menuFlow.HoldFrame(_window.Width, _window.Height);
        }
    }

    /// <summary>The briefing's first paragraph, on one line, cut to what a caption can carry.</summary>
    private static string BriefingParagraph(string text)
    {
        string paragraph = text.Replace("\r", string.Empty).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(p => p.Trim().Length > 0)?.Replace('\n', ' ').Trim() ?? text;
        return paragraph.Length > 260 ? paragraph[..257].TrimEnd() + "…" : paragraph;
    }

    /// <summary>The briefing entire, as paragraphs on one line each.</summary>
    private static IEnumerable<string> BriefingParagraphs(string text) =>
        text.Replace("\r", string.Empty)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace('\n', ' ').Trim())
            .Where(p => p.Length > 0);

    /// <summary>Draws the briefing standing in the level, over everything but the console.</summary>
    /// <param name="batch">The 2D batch, not begun.</param>
    private void DrawWorldTitle(SpriteBatch batch)
    {
        if (!ShowsLoadingPanel)
        {
            return;
        }

        // How long the level has been the player's: reset by anything that takes it from them.
        bool playerHasLevel = !PresentationHoldsLevel && !_loadSequence.IsActive && _mission.Cutscenes?.Frame is null;
        _controlSeconds = playerHasLevel ? _controlSeconds + (1f / 60f) : 0f;

        // An intro that starts AFTER the notice went up takes it down again, to be posted fresh when the
        // intro lets go: a level's intro can begin a second or two into play, and a notice that ran its
        // few seconds behind the intro's camera was one nobody saw.
        if (_mission.Cutscenes?.Frame is not null && _briefingPost is { HasTitle: true } hidden)
        {
            hidden.Clear();
            _briefingPosted = false;
        }

        // The briefing posted at the start: it arrives once the player has the level, stays a few
        // seconds, and goes.
        if (RenderCVars.WorldBriefing.Value && _session.Mission is not null && HostedPlayer is not null
            && !PresentationHoldsLevel && !_loadSequence.IsActive)
        {
            PostBriefing();
            if (_briefingPost is { HasTitle: true } post)
            {
                int now = _loop.TimeSource.Milliseconds;
                float elapsed = Math.Clamp((now - _lastBriefingMilliseconds) / 1000f, 0f, 0.1f);
                _lastBriefingMilliseconds = now;
                post.Update(elapsed);

                // The hold counts from when the notice was first in the frame, so a player who turns to
                // it later still gets their five seconds -- up to a patience, after which it goes anyway.
                float burnStart = post.Seen
                    ? MathF.Max(post.ArrivalSeconds, post.SeenAtAge) + BriefingHoldSeconds
                    : post.ArrivalSeconds + BriefingUnseenPatienceSeconds;
                post.Dissolve = Math.Clamp((post.Age - burnStart) / BriefingBurnSeconds, 0f, 1f);
                if (post.Age > burnStart + BriefingBurnSeconds + 0.5f)
                {
                    post.Clear();
                }
                else
                {
                    _device.BeginStageTiming(RenderStage.UserInterface);
                    _device.Set2DProjection(_window.Width, _window.Height);
                    batch.Begin();
                    post.Draw(batch, _inspectorEye, _inspectorTangent, _inspectorAspect, _window.Width, _window.Height);
                    batch.End();
                    _device.EndStageTiming();
                }
            }
        }
    }

    /// <summary>Whether a mission's files can all be read now, as the load gate says.</summary>
    /// <param name="missionId">The mission.</param>
    /// <param name="waitingFor">What is still missing, when they cannot.</param>
    /// <returns>
    /// <see langword="true"/> when they can. A mission the catalogue does not know has no files to wait for;
    /// whatever then tries to load it reports it.
    /// </returns>
    private bool MissionFilesReady(int missionId, out string? waitingFor)
    {
        waitingFor = null;
        return _missions.Missions.FirstOrDefault(m => m.Id == missionId) is not { } mission
            || _loadGate.IsReady(mission, out waitingFor);
    }

    /// <summary>Asks for a menu backdrop, once its mission's files are there.</summary>
    /// <param name="loader">The backdrop builder, if the menu has one.</param>
    /// <param name="missionId">The mission to show.</param>
    /// <remarks>
    /// A backdrop is a mission loaded behind the menu, and reads the same files a played one does. Until
    /// the gate says they are there -- a browser reads a level's large files when it is first wanted --
    /// the request waits in <see cref="_backdropAwaitingFiles"/> and the menu asks again each frame; the
    /// newest request replaces one still waiting.
    /// </remarks>
    private void RequestBackdrop(MissionPreviewLoader? loader, int missionId)
    {
        if (loader is null)
        {
            return;
        }

        // Nothing could be played from it: a mission the catalogue does not offer cannot be promoted or
        // started, so it is not built behind the menu either.
        if (!_missions.TryGet(missionId, out _))
        {
            _logger.Warning("Mission", $"Asked for a backdrop of mission {missionId}, which is not offered.");
            return;
        }

        if (!MissionFilesReady(missionId, out _))
        {
            _backdropAwaitingFiles = missionId;
            return;
        }

        _backdropAwaitingFiles = null;
        loader.Request(missionId);
    }

    /// <summary>Starts a mission the command line asked for, now or once the gate lets its files be read.</summary>
    /// <param name="missionId">The mission.</param>
    /// <remarks>
    /// The desktop's gate always says yes, so this is the plain start there. A browser fetches a level's
    /// large files when the level is first wanted, and being asked is what starts the fetch: the start
    /// waits in <see cref="_startAwaitingFiles"/> and <see cref="RunCommandLineMissionIfDue"/> asks
    /// again each frame. Started regardless, the level came up with its terrain and models absent.
    /// </remarks>
    private void StartMissionWhenReadable(int missionId)
    {
        if (MissionFilesReady(missionId, out string? waitingFor))
        {
            StartMission(missionId);
            return;
        }

        _startAwaitingFiles = missionId;
        _logger.Information("Mission", $"Mission {missionId} waits for {waitingFor ?? "its files"}.");
    }

    /// <summary>Starts the command line's mission once its files are there. Call between frames.</summary>
    private void RunCommandLineMissionIfDue()
    {
        if (_startAwaitingFiles is not { } missionId || !MissionFilesReady(missionId, out _))
        {
            return;
        }

        _startAwaitingFiles = null;
        StartMission(missionId);
    }

    /// <summary>Runs the deferred load once the departure has played. Call between frames.</summary>
    private void RunPendingLoadIfDue()
    {
        if (!_loadSequence.WantsLoad || _pendingLoad is not { } load)
        {
            return;
        }

        // Not until the mission's files can be read. The sequence holds its picture meanwhile, and the
        // load runs on the frame the gate says yes -- on the desktop, always the first.
        if (!MissionFilesReady(_pendingLoadMission, out string? waitingFor))
        {
            if (!string.Equals(waitingFor, _loadGateWaitingFor, StringComparison.Ordinal))
            {
                _loadGateWaitingFor = waitingFor;
                _logger.Information("Mission", $"Mission {_pendingLoadMission} waits for {waitingFor ?? "its files"}.");
            }

            return;
        }

        _pendingLoad = null;
        load();

        if (!_loadSequence.Loaded)
        {
            // Nothing came up -- a promotion with no backdrop left to promote. The sequence has nothing
            // to hold for; let it go rather than hold a card over whatever the view is.
            _loadSequence.Cancel();
            return;
        }

        // The level is up. The card stays over the held picture until the lighting is ready, and then
        // dissolves straight into the level -- its intro, or the player's own eyes.
    }

    /// <summary>
    /// Posts the briefing on the nearest vertical surface at the player's start, once per level: the
    /// fence beside the start, a wall, the side of a crate.
    /// </summary>
    /// <remarks>
    /// Rays are cast level from the eye, a full turn, against the level's collision; the nearest hit on a
    /// surface that stands up (a fence or a wall, not a floor or a roof) is the post, with a fence
    /// preferred to a wall and a surface ahead preferred to one beside. The notice stands a few
    /// centimetres off it along its normal and faces out, so the player reads it from the open side.
    /// With nothing in reach it stands in the air ahead, where it can at least be read.
    /// </remarks>
    private void PostBriefing()
    {
        if (_briefingPosted || HostedPlayer is not { } standing || _shellHeadingFont is not { } face)
        {
            return;
        }

        // Not before the player has had the level for a moment: the notice arrives once the picture has
        // cleared and the authored intro, if there is one, has let go -- so its own arrival is what they
        // first see, and it is never cut away from.
        if (_controlSeconds < BriefingControlSeconds || !_playerMode)
        {
            return;
        }

        _briefingPosted = true;
        _briefingPost ??= new WorldTitleRenderer(face);

        Camera3D eye = standing.Camera;
        float yaw = standing.Body.Yaw;
        Vector3d anchor;
        string where;

        if (TryFindBriefingSurface(standing.EyeUnits, yaw, out Vector3d surfaceMetres, out Vector3f normal, out string surfaceLabel))
        {
            anchor = new Vector3d(
                surfaceMetres.X + (normal.X * BriefingPostStandOffMetres),
                surfaceMetres.Y + (normal.Y * BriefingPostStandOffMetres),
                eye.Position.Z + 0.35);

            // A reader stands on the open side and looks INTO the surface, so the notice faces along the
            // normal and is read by someone whose heading is the normal reversed.
            yaw = MathF.Atan2(-normal.X, -normal.Y);
            where = FormattableString.Invariant($"on '{surfaceLabel}', {Vector3d.Distance(eye.Position, surfaceMetres):F1} m from the start");
        }
        else
        {
            var forward = new Vector3d(Math.Sin(yaw), Math.Cos(yaw), 0.0);
            anchor = new Vector3d(
                eye.Position.X + (forward.X * 6.0),
                eye.Position.Y + (forward.Y * 6.0),
                eye.Position.Z - 0.1);
            where = "in the air six metres ahead, with no surface in reach";
        }

        // The notice: the mission's number and name, then the briefing entire, wrapped to a notice's width.
        const float bodyHeight = 0.3f;
        const float noticeWidthMetres = 7.5f;
        float metresPerPixel = bodyHeight / face.BasePixelHeight;

        var lines = new List<WorldTitleRenderer.Line>();
        if (_loadingInfo is { } info)
        {
            lines.Add(new WorldTitleRenderer.Line(FormattableString.Invariant($"MISSION {info.Number:00}"), 0.34f, CardAccent, 0f));
            lines.Add(new WorldTitleRenderer.Line(info.Name.ToUpperInvariant(), 1.05f, CardWhite, 0.08f));

            bool first = true;
            int count = 0;
            foreach (string paragraph in BriefingParagraphs(info.Description))
            {
                string wrapped = face.WrapText(paragraph, face.BasePixelHeight, noticeWidthMetres / metresPerPixel);
                bool firstLine = true;
                foreach (string line in wrapped.Split('\n'))
                {
                    lines.Add(new WorldTitleRenderer.Line(
                        line.ToUpperInvariant(), bodyHeight, CardMuted, first ? 0.4f : firstLine ? 0.22f : 0.08f));
                    first = false;
                    firstLine = false;
                    if (++count == 10)
                    {
                        break;
                    }
                }

                if (count >= 10)
                {
                    break;
                }
            }
        }
        else
        {
            lines.Add(new WorldTitleRenderer.Line("PROJECT I.G.I.", 1.05f, CardWhite, 0f));
        }

        _briefingPost.Lay(lines, anchor, yaw, upright: true);
        _lastBriefingMilliseconds = _loop.TimeSource.Milliseconds;
        _briefingPost.ReliefLayers = 3;
        _briefingPost.ReliefStepMetres = 0.02f;

        _logger.Information(
            "Mission",
            FormattableString.Invariant(
                $"Posted the briefing {where}, at ({anchor.X:F1}, {anchor.Y:F1}, {anchor.Z:F1}) m facing heading {yaw:F2}; r_world_briefing 0 hides it."));
    }

    /// <summary>Finds the nearest standing surface around the start to post on.</summary>
    /// <param name="eyeUnits">The player's eye, in engine units, which is what the collision index takes.</param>
    /// <param name="heading">The way the player faces, so a surface in front is preferred.</param>
    /// <param name="surfaceMetres">Receives where the surface was hit, in metres.</param>
    /// <param name="normal">Receives the surface's outward normal, pointing back towards the start.</param>
    /// <param name="label">Receives what was hit and why it was chosen, for the log.</param>
    private bool TryFindBriefingSurface(
        Vector3d eyeUnits, float heading, out Vector3d surfaceMetres, out Vector3f normal, out string label)
    {
        surfaceMetres = default;
        normal = default;
        label = string.Empty;
        if (_mission.Solids is not { } solids)
        {
            return false;
        }

        // What the player is facing when they get the level: a notice behind them is one they never see.
        var facing = new Vector3d(Math.Sin(heading), Math.Cos(heading), 0.0);

        const int rays = 36;
        const double unitsPerMetre = WorldScene.UnitsPerMetre;
        double reach = BriefingPostReachMetres * unitsPerMetre;

        // A fence is preferred over a wall, and a surface in front of the player over one beside or
        // behind them; within a tier the nearest wins. A fence is known by its name when the level gave
        // it one, and otherwise by its height: a standing surface that is not there any more a couple
        // of metres above the eye is a fence, a crate or a low wall, not a building.
        int bestTier = -1;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < rays; i++)
        {
            double angle = i * (2.0 * Math.PI / rays);
            var end = new Vector3d(
                eyeUnits.X + (Math.Sin(angle) * reach),
                eyeUnits.Y + (Math.Cos(angle) * reach),
                eyeUnits.Z);

            if (!solids.TryIdentify(eyeUnits, end, out string hitLabel, out WorldRayHit hit))
            {
                continue;
            }

            // Standing surfaces only, and not so close the notice would be in the player's face.
            if (MathF.Abs(hit.Normal.Z) > 0.45f || hit.Distance < 1.2 * unitsPerMetre)
            {
                continue;
            }

            // The normal must face the start, or the notice would be posted on the far side.
            Vector3f n = hit.Normal;
            float towards = (float)((eyeUnits.X - hit.Position.X) * n.X + (eyeUnits.Y - hit.Position.Y) * n.Y);
            if (towards < 0f)
            {
                n = new Vector3f(-n.X, -n.Y, -n.Z);
            }

            bool namedFence = hitLabel.Contains("fence", StringComparison.OrdinalIgnoreCase)
                || hitLabel.Contains("wire", StringComparison.OrdinalIgnoreCase)
                || hitLabel.Contains("gate", StringComparison.OrdinalIgnoreCase)
                || hitLabel.Contains("barrier", StringComparison.OrdinalIgnoreCase)
                || hitLabel.Contains("rail", StringComparison.OrdinalIgnoreCase);

            // The same ray, a couple of metres higher: a fence is not there, a building still is.
            var highOrigin = new Vector3d(eyeUnits.X, eyeUnits.Y, eyeUnits.Z + (FenceTopAboveEyeMetres * unitsPerMetre));
            var highEnd = new Vector3d(end.X, end.Y, highOrigin.Z);
            bool lowSurface = !solids.TryCast(highOrigin, highEnd, out WorldRayHit above)
                || above.Distance > hit.Distance + (1.5 * unitsPerMetre);

            // In front: within sixty degrees of where the player faces.
            double along = (Math.Sin(angle) * facing.X) + (Math.Cos(angle) * facing.Y);
            bool ahead = along > 0.5;

            bool fenceLike = namedFence || lowSurface;
            int tier = (fenceLike ? 2 : 0) + (ahead ? 1 : 0);
            if (tier < bestTier || (tier == bestTier && hit.Distance >= bestDistance))
            {
                continue;
            }

            bestTier = tier;
            bestDistance = hit.Distance;
            surfaceMetres = new Vector3d(hit.Position.X / unitsPerMetre, hit.Position.Y / unitsPerMetre, hit.Position.Z / unitsPerMetre);
            normal = n;
            label = FormattableString.Invariant(
                $"{(hitLabel.Length > 0 ? hitLabel : "an unnamed surface")}{(fenceLike ? ", fence-like" : ", wall-like")}{(ahead ? ", ahead" : ", to the side")}");
        }

        return bestTier >= 0;
    }

    /// <summary>
    /// Starts holding a level back, from before its load begins until the picture it will be shown with is
    /// final.
    /// </summary>
    /// <param name="mission">The mission being brought up.</param>
    /// <remarks>
    /// <para>
    /// <b>Why a load ending is not the same as a level being ready.</b> Most of this renderer's lighting
    /// is temporal: the screen probe gather accumulates its estimate per pixel across frames, the world
    /// radiance cache fills its probes over several, the temporal resolve needs a history before it has
    /// anything to reconstruct from, and the auto-exposure adapts towards the scene rather than jumping
    /// to it. None of that can be computed in the load, because all of it is defined in terms of FRAMES
    /// -- there is no way to have a history without having drawn some. And under the real-time model the
    /// structures it lights from are built rather than switched on: a distance field that may still be
    /// baking, a surface cache that converges over dispatches.
    /// </para>
    /// <para>
    /// So the level is held by <see cref="MissionWarmUpGate"/> until those structures exist and have
    /// converged, and then for a few frames more -- drawn as normal, with every temporal system
    /// accumulating exactly as it would in play, but with the finished picture kept off the screen. That
    /// is what every shipping game does behind a loading screen, and it is the only honest way to promise
    /// that what appears is final: the alternative is showing the player the renderer converging, which
    /// reads as the game breaking and then repairing itself.
    /// </para>
    /// <para>
    /// Every route in gets <see cref="MissionLoadSequence"/>: the held picture, the card, the shot over
    /// the start, the reveal. The card covers the blocking part; the shot's hold covers the lighting.
    /// </para>
    /// </remarks>
    private void BeginMissionWarmUp(MissionDescriptor mission)
    {
        _loadingMission = mission;
        _loadingInfo = _shellMissions?.FirstOrDefault(m => m.Id == mission.Id);
        _loadStageIndex = 0;
        _warmUpFramesDrawn = 0;
        _lastWarmUpMilliseconds = _loop.TimeSource.Milliseconds;

        _warmUp = new MissionWarmUpGate(RenderCVars.MissionWarmUp.Value);
        _warmUp.Begin();

        // The last run's notice goes with the last run: a restart within its few seconds must not carry
        // it through the load, nor post it again on the moment the level was last held for.
        _briefingPosted = false;
        _briefingPost?.Clear();
        _controlSeconds = 0f;

        // A level started with nothing on screen -- the command line's -mission -- has no picture to
        // hold and no interface to leave, so the sequence opens straight onto the card over its own
        // surface. Never under the entry shot, which is the cover for that route.
        if (!_loadSequence.IsActive && !_entry.IsRunning && ShowsLoadingPanel)
        {
            _loadSequence.Begin(capturePicture: false);
        }

        _logger.Information(
            "Mission",
            $"Holding the level until its lighting is built and settled ({RenderCVars.MissionWarmUp.Value} "
            + "frame(s) once it is), so what appears is what it will look like.");
    }

    /// <summary>Tells the sequence the level has been built; the hold now only waits on its lighting.</summary>
    private void NotifyMissionBuilt() => _loadSequence.NotifyLoaded();

    /// <summary>Abandons the warm-up, so the level is shown on the next frame.</summary>
    /// <remarks>
    /// For anything that takes the player out of the level before it was ever shown — abandoning to
    /// the campaign, a mission that fails during its own warm-up — where holding on would keep a card
    /// over a menu.
    /// </remarks>
    private void CancelMissionWarmUp()
    {
        _warmUp.Cancel();
        _loadSequence.Cancel();
        _pendingLoad = null;
        _briefingPost?.Clear();
        _controlSeconds = 0f;
    }

    /// <summary>Reports one more stage of the blocking load finished, and shows it.</summary>
    /// <remarks>
    /// Called from <c>EndStage</c>, so the fourteen named stages of bringing a mission up each move the
    /// bar by one share while the render thread is otherwise not presenting at all.
    /// </remarks>
    private void ReportLoadStage()
    {
        if (!_warmUp.IsActive)
        {
            return;
        }

        _loadStageIndex++;
        _warmUp.ReportLoad(_loadStageIndex / (float)LoadStageCount);
        PresentLoadingFrame();
    }

    /// <summary>
    /// Advances the hold by one drawn frame and draws the loading screen over the withheld picture.
    /// </summary>
    /// <param name="batch">The 2D batch, between its own begin and end.</param>
    /// <returns><see langword="true"/> when the screen was drawn.</returns>
    /// <remarks>
    /// Drawn over a frame that was fully rendered rather than instead of one. That is the whole point:
    /// the scene has to be drawn for its temporal passes to advance, so what this does is withhold it,
    /// not skip it. Once the gate opens the sequence reveals: the card leaves and the held picture comes
    /// back into focus over the live, settled level.
    /// </remarks>
    private bool DrawMissionWarmUp(SpriteBatch batch)
    {
        int now = _loop.TimeSource.Milliseconds;
        float elapsed = Math.Clamp((now - _lastWarmUpMilliseconds) / 1000f, 0f, 0.1f);
        _lastWarmUpMilliseconds = now;

        if (_warmUp.IsActive)
        {
            _warmUpFramesDrawn++;
            bool fieldPending = _gi.FieldBake is not null;
            bool lightingReady = !_gi.Pending;

            _warmUp.Update(lightingReady, fieldPending, _gi.ConvergeProgress, elapsed);

            if (!_warmUp.IsActive)
            {
                // The level is up. A promoted backdrop still carries the decoded pictures its arming had
                // not reached; the mission decodes those as it meets them, like a level loaded cold does,
                // rather than holding the lot for as long as it is played.
                _memory.ReleaseLoadLeftovers(_world, _detailMaps);

                if (_warmUp.GaveUp)
                {
                    _logger.Warning(
                        "Mission",
                        $"Showing the level after {MissionWarmUpGate.PatienceSeconds:0} s without its lighting "
                        + "finished; it will cross over when it lands.");
                }
                else
                {
                    _logger.Information(
                        "Mission",
                        $"Revealed the level after {_warmUpFramesDrawn} withheld frame(s).");
                }
            }
        }

        _loadSequence.Update(elapsed, ready: _loadSequence.Loaded && !_warmUp.IsActive);
        _switch.AdvanceHold(revealed: _loadSequence.Loaded && !_warmUp.IsActive, loadActive: _loadSequence.IsActive);

        if (!_loadSequence.IsActive || !ShowsLoadingPanel)
        {
            return false;
        }

        PaintLoadingScreen(batch);
        return true;
    }

    /// <summary>
    /// Draws and presents a single loading frame, from outside the render loop, during a blocking load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bringing a mission up is a few hundred milliseconds of work on the render thread -- uploading meshes
    /// and textures, reading nav graphs, capturing the surface cache -- and none of it presents a frame.
    /// So whatever was last on screen stayed there, frozen, and a window that stops presenting for long
    /// enough is dimmed and marked as not responding by the compositor. The loop cannot help with that:
    /// it is precisely what is blocked.
    /// </para>
    /// <para>
    /// So frames are drawn and swapped by hand as each stage of the load finishes, with the same sequence
    /// the loop draws before and after -- the picture already held, the card already up -- so the load is
    /// invisible in the presentation: the bar moves and the light keeps drifting.
    /// </para>
    /// </remarks>
    private void PresentLoadingFrame()
    {
        if (_batch is not { } batch || !ShowsLoadingPanel || !_loadSequence.IsActive)
        {
            return;
        }

        int now = _loop.TimeSource.Milliseconds;
        if (now - _lastLoadingPresentMilliseconds < LoadingPresentIntervalMilliseconds)
        {
            return;
        }

        float elapsed = Math.Clamp((now - _lastWarmUpMilliseconds) / 1000f, 0f, 0.1f);
        _lastWarmUpMilliseconds = now;
        _lastLoadingPresentMilliseconds = now;
        _loadSequence.Update(elapsed, ready: false);

        _device.Clear(Bgra32.FromRgb(8, 10, 13));
        PaintLoadingScreen(batch);
        _scheduler.Flush();
    }

    /// <summary>Paints the loading screen: the backdrop pass, then the card over it.</summary>
    /// <param name="batch">The 2D batch, not begun.</param>
    private void PaintLoadingScreen(SpriteBatch batch)
    {
        // After a switch of renderer the other renderer's picture stands in for the whole screen.
        if (_switch.PaintResumeFrame(_window.Width, _window.Height))
        {
            return;
        }

        float width = _window.Width;
        float height = _window.Height;
        float seconds = _loop.TimeSource.Milliseconds / 1000f;

        var backdrop = new LoadingBackdropSettings(
            TimeSeconds: seconds,
            Blur: _loadSequence.Blur,
            Desaturation: _loadSequence.Desaturation,
            Vignette: _loadSequence.Vignette,
            Zoom: _loadSequence.Zoom,
            Grain: 0.035f,
            Scanlines: 0.05f,
            Opacity: _loadSequence.Opacity,
            Letterbox: MissionLoadSequence.LetterboxFraction * _loadSequence.Letterbox,
            HasPicture: _loadSequence.HasPicture && _menuFlow.HeldFrame.IsValid,
            AspectRatio: height > 0f ? width / height : 1.777f,
            Sweep: _loadSequence.Sweep);

        _device.BeginStageTiming(RenderStage.UserInterface);
        _device.DrawLoadingBackdrop(_menuFlow.HeldFrame, backdrop);

        float card = _loadSequence.Card;
        if (card > 0.002f)
        {
            _device.Set2DProjection(_window.Width, _window.Height);
            batch.Begin();
            var painter = UiPainter.Create(batch, new LayoutContext(_window.Width, _window.Height));
            painter.PushOpacity(card * _loadSequence.Opacity);
            PaintLoadingCard(painter, width, height, seconds);
            painter.PopOpacity();
            batch.End();
        }

        _device.EndStageTiming();
    }

    /// <summary>Paints the card: the mission's number, name and briefing, and what the load is doing.</summary>
    /// <remarks>
    /// Set the way the campaign page sets its mission: a monospaced eyebrow and a rule over the name in the
    /// title face, the briefing under it, and the design's thin amber bar for the progress.
    /// </remarks>
    private void PaintLoadingCard(UiPainter painter, float width, float height, float seconds)
    {
        if (_shellType is not { } type)
        {
            return;
        }

        UiFont body = type.Body;
        float scale = UiScale.Resolve(_options.UiScale, _window.Width, _window.Height);
        float slide = _loadSequence.CardSlide * scale;
        float left = width * 0.085f;
        float bars = height * MissionLoadSequence.LetterboxFraction;

        // The title block sits in the lower third, above the bar, the way a title does over a place: a
        // caption, not a dialog.
        float titleSize = 112f * scale;
        float kickerSize = 13f * scale;
        float bodySize = 20f * scale;
        float baseline = height * 0.60f + slide;
        float titleTop = baseline - (titleSize * 0.85f);

        // "MISSION 08 —— DEPLOYING", letterspaced, in the accent -- on the card, where the card carries the name.
        if (!_loadSequence.LiveScene)
        {
            string kicker = _loadingInfo is { } info
                ? FormattableString.Invariant($"MISSION {info.Number:00}")
                : "MISSION";
            float kickerTop = titleTop - (28f * scale) - type.Mono.LineHeight(kickerSize);
            UiVec2 pen = painter.DrawLabel(type.Mono, kicker, new UiVec2(left, kickerTop), kickerSize, CardAccent, 0.28f);
            float ruleX = pen.X + (16f * scale);
            painter.FillRect(new UiRect(ruleX, kickerTop + (type.Mono.LineHeight(kickerSize) * 0.5f), 48f * scale, MathF.Max(1f, scale)), UiPalette.AccentAt(0.6f));
            painter.DrawLabel(type.Mono, "DEPLOYING", new UiVec2(ruleX + (64f * scale), kickerTop), kickerSize, CardMuted, 0.28f);
        }

        // The name and the briefing are on the card only until the level is up; from then on they stand
        // in the level itself, under the shot -- see WorldTitleRenderer.
        if (!_loadSequence.LiveScene)
        {
            string title = (_loadingInfo?.Name
                ?? (_loadingMission is { } mission ? _menuStrings?.Get(mission.Name) ?? mission.Name : string.Empty))
                .ToUpperInvariant();
            float fit = MathF.Min(1f, (width - (left * 2f)) / MathF.Max(1f, UiPainter.MeasureLabel(type.Display, title, titleSize, 0.01f)));
            float size = titleSize * fit;
            var titleBox = new UiRect(left, titleTop, width, titleSize * 0.85f);
            painter.DrawLabel(type.Display, title, new UiVec2(left, UiRowStyle.CapsCenterY(type.Display, size, titleBox)), size, CardWhite, 0.01f);

            // A hairline that draws itself under the name as the card comes in.
            float ruleWidth = MathF.Min(width * 0.18f, 300f * scale) * _loadSequence.Card;
            float ruleTop = baseline + (18f * scale);
            painter.FillRect(new UiRect(left, ruleTop, ruleWidth, 2f * scale), CardAccent);

            // The briefing, a few lines of it, under the rule.
            if (_loadingInfo is { Description.Length: > 0 } briefed)
            {
                float lineHeight = body.LineHeight(bodySize) * 1.15f;
                float y = ruleTop + (24f * scale);
                foreach (string line in WrapLines(body, briefed.Description, bodySize, width * 0.36f, maxLines: 3))
                {
                    painter.DrawText(body, line, new UiVec2(left, y), bodySize, UiPalette.TextBody);
                    y += lineHeight;
                }
            }
        }

        // Bottom left: what is happening, with a slow pulse of dots.
        float footer = height - bars - (52f * scale);
        int dots = (int)(seconds * 2.5f) % 4;
        float captionSize = 12f * scale;
        painter.FillDiamond(new UiVec2(left + (3f * scale), footer + (type.Mono.LineHeight(captionSize) * 0.5f)), 6f * scale, CardAccent);
        painter.DrawLabel(type.Mono, LoadingCaptions[dots], new UiVec2(left + (16f * scale), footer), captionSize, CardMuted, 0.28f);

        // Bottom right: the stage, the percentage and the bar.
        float barWidth = MathF.Min(width * 0.26f, 460f * scale);
        float barRight = width - left;
        float barLeft = barRight - barWidth;
        float barHeight = 4f * scale;
        float barTop = footer + (14f * scale);
        float progress = Math.Clamp(_warmUp.Progress, 0f, 1f);

        string stage = _warmUp.IsActive ? _warmUp.Stage : "Preparing";
        float stageSize = 11f * scale;
        painter.DrawLabel(type.Mono, stage.ToUpperInvariant(), new UiVec2(barLeft, footer - (8f * scale)), stageSize, CardDim, 0.22f);

        string percent = FormattableString.Invariant($"{(int)(progress * 100f)}%");
        float percentSize = 14f * scale;
        float percentWidth = type.Mono.MeasureWidth(percent, percentSize);
        painter.DrawText(type.Mono, percent, new UiVec2(barRight - percentWidth, footer - (10f * scale)), percentSize, CardWhite);

        painter.FillRect(new UiRect(barLeft, barTop, barWidth, barHeight), UiPalette.White(0.1f));
        painter.FillRect(new UiRect(barLeft, barTop, barWidth * progress, barHeight), CardAccent);

        // A highlight that sweeps the bar, so the card reads as alive even while a stage is long.
        float sweep = (seconds * 0.45f) % 1.35f;
        float glowWidth = barWidth * 0.14f;
        float glowLeft = barLeft + (barWidth * (sweep - 0.14f));
        float glowRight = MathF.Min(glowLeft + glowWidth, barLeft + barWidth);
        glowLeft = MathF.Max(glowLeft, barLeft);
        if (glowRight > glowLeft)
        {
            Span<UI.Drawing.UiGradientStop> glow =
            [
                new(0f, UiPalette.White(0f)),
                new(0.5f, UiPalette.White(0.55f)),
                new(1f, UiPalette.White(0f)),
            ];
            painter.FillGradient(new UiRect(glowLeft, barTop, glowRight - glowLeft, barHeight), glow, UI.Drawing.UiGradientDirection.Horizontal);
        }
    }

    /// <summary>The loading caption with its pulse of dots, made once rather than every frame.</summary>
    private static readonly string[] LoadingCaptions = ["LOADING", "LOADING.", "LOADING..", "LOADING..."];

    /// <summary>Breaks a paragraph into lines that fit a width, greedily, to at most a few lines.</summary>
    private static List<string> WrapLines(UiFont font, string text, float size, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureWidth(candidate, size) <= maxWidth || current.Length == 0)
            {
                current.Clear().Append(candidate);
                continue;
            }

            lines.Add(current.ToString());
            current.Clear().Append(word);
            if (lines.Count == maxLines - 1)
            {
                break;
            }
        }

        if (current.Length > 0 && lines.Count < maxLines)
        {
            string last = current.ToString();
            if (lines.Count == maxLines - 1 && font.MeasureWidth(last + "…", size) > maxWidth)
            {
                while (last.Length > 1 && font.MeasureWidth(last + "…", size) > maxWidth)
                {
                    last = last[..^1];
                }

                last = last.TrimEnd() + "…";
            }

            lines.Add(last);
        }

        return lines;
    }
}
