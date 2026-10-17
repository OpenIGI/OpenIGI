using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Time;
using OpenIGI.Frontend;
using OpenIGI.Frontend.Saves;
using OpenIGI.Game.World;
using OpenIGI.Host.Lighting;
using OpenIGI.Host.Menu;
using OpenIGI.Platform;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.UI.Kit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Host;

/// <summary>What <see cref="GameHost"/>'s frame loop keeps from one frame to the next.</summary>
/// <remarks>
/// The loop's own state rather than the host's: the shell and its menus, the backdrop and its dissolve, the capture
/// switches, the pacing clocks, the screen GI's history. It lives as long as one run of the loop, and it is what the
/// loop's passes are handed.
/// </remarks>
internal sealed class FrameLoopState
{
    public SpriteBatch Batch { get; set; } = default!;

    public bool Running { get; set; }

    public ManualTimeSource? LockstepClock { get; set; }

    public bool ReportedWorld { get; set; }

    public bool ReportedShot { get; set; }

    public InteractionCamera InteractionCamera { get; set; } = default!;

    public bool ClimbActive { get; set; }

    public bool WorldInteractionActive { get; set; }

    public int TemporalFrame { get; set; }

    public string? CutsceneCaptureDirectory { get; set; }

    public int CutsceneCaptureScene { get; set; }

    public bool CaptureCutscene { get; set; }

    public int CutsceneCaptureTick { get; set; }

    public int CutsceneCaptureEvery { get; set; }

    public int CutsceneCaptureCount { get; set; }

    public int CutsceneCaptureStartTick { get; set; }

    public CutSceneTask? PreviousCapturedScene { get; set; }

    public int CutsceneCaptureLimit { get; set; }

    public bool FlightStarted { get; set; }

    public Vector3d FlightStartPos { get; set; } = default!;

    public float FlightStartYaw { get; set; }

    public float FlightStartPitch { get; set; }

    public int FlightFrame { get; set; }

    public long ProbeArrivedAt { get; set; }

    public bool CardLightingDumped { get; set; }

    public bool ScreenGiDumped { get; set; }

    public ScreenGiHistory ScreenGi { get; set; } = default!;

    public bool ClimbSlideArmed { get; set; }

    public int ReportedHumanHits { get; set; }

    public FogPresets FogPresets { get; set; } = default!;

    public bool ShowDebug { get; set; }

    public Shell? Shell { get; set; }

    public MissionWarmer? MissionWarmer { get; set; }

    public MissionPreviewLoader? PreviewLoader { get; set; }

    public IgiTypography? ShellTypography { get; set; }

    public ShellHost? ShellHost { get; set; }

    public SaveSlotStore? Saves { get; set; }

    public bool MenuMode { get; set; }

    public bool PauseMenu { get; set; }

    public bool ShowcasePending { get; set; }

    public bool FirstFrameReported { get; set; }

    public bool EntryCamPending { get; set; }

    public long EntryTimeChosenAt { get; set; }

    public ShellHost? ShellHostForEntryCam { get; set; }

    public int LastEntryMilliseconds { get; set; }

    public int LastComputerMilliseconds { get; set; }

    public int CurrentShowcaseMissionId { get; set; }

    public int PreviewRequestedAtMs { get; set; }

    public bool HoldFrameForDissolve { get; set; }

    public bool HeldFrameReady { get; set; }

    public bool MissionsSwept { get; set; }

    public int BackdropSwaps { get; set; }

    public bool HeldFrameIsPicture { get; set; }

    public bool DissolveRevealPending { get; set; }

    public DistanceFieldBake.Result? BakedFieldForBackdrop { get; set; }

    public IFramePacer MenuPacer { get; set; } = default!;

    public bool LastPassRendered { get; set; }

    public long PassStartedAt { get; set; }

    public FramePerformanceTracker FramePerformance { get; set; } = default!;

    public ShadowCascade[] CascadeStorage { get; set; } = default!;

    public int LastCameraMilliseconds { get; set; }

    public int LastConsoleMilliseconds { get; set; }

    public int LastShellMilliseconds { get; set; }

    public int LastMenuCameraMilliseconds { get; set; }

    public int LastProbeChaseMilliseconds { get; set; }

    public int ProbeChaseFrame { get; set; }

    public bool CaptureMode { get; set; }

    public FrameOutcome LastOutcome { get; set; } = default!;

    public int TicksSinceStep { get; set; }
}
