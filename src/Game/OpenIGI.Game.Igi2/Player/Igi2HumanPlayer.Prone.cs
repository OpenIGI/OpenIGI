using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Lying flat and crawling.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The ground tolerance while lying flat or crawling.</summary>
    public const float ProneGroundTolerance = 2048f;

    /// <summary>The water depth that stands a crawl up whatever else it was doing.</summary>
    public const float CrawlWaterDepthLimit = 819.20001f;

    private const float CrawlSpeed = 136.53334f;
    private const float CrawlStep = 27.306667f;

    /// <summary>
    /// The crawl's target speeds by held keys, indexed <c>left + 2·right + 4·(back + 2·forward)</c> as
    /// <c>HumanPlayer_RunStateFlatCrawl</c> (<c>igi2 0x4B2FD0</c>) builds its table: a key and its opposite
    /// together cancel the whole move, not just their own axis.
    /// </summary>
    private static readonly (float X, float Y)[] CrawlSpeeds =
    [
        (0f, 0f), (-CrawlSpeed, 0f), (CrawlSpeed, 0f), (0f, 0f),
        (0f, -CrawlSpeed), (-CrawlSpeed, -CrawlSpeed), (CrawlSpeed, -CrawlSpeed), (0f, 0f),
        (0f, CrawlSpeed), (-CrawlSpeed, CrawlSpeed), (CrawlSpeed, CrawlSpeed), (0f, 0f),
        (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f),
    ];

    private IIgi2SoundInstance? _crawlSound;

    /// <summary>Gets the orientation the player lay down with, <c>tStateContext.tFlatContext</c>.</summary>
    public Matrix3f FlatOrientation { get; private set; }

    // State 75 -- InitStateFlat 0x4B2D30, RunStateFlat 0x4B2E30.
    private void EnterFlat()
    {
        if (Pose != Igi2HumanPose.LyingFlat)
        {
            Weapon.LeaveZoomAlternateFireMode();
        }

        Pose = Igi2HumanPose.LyingFlat;
        Animation.PlayState(Igi2PlayerState.Flat, 0, StateBlendSeconds);
        CurrentUpperBody = 0;
        RequestedUpperBody = 0;
        Animation.ClearUpperBody(0.4f);
        AimPrecision(Igi2PrecisionType.LyingFlat, static p => p.LyingFlat);
        FlatOrientation = Orientation;
        Animation.ClearUpperBody(0f);
        GroundTolerance = ProneGroundTolerance;
        Locomotion.Current = (_, _) => RunFlat();
    }

    private void RunFlat()
    {
        // Lying down stops dead: the step is a full run's speed.
        MoveOnGround(0f, RunForwardSpeed, 0f, RunForwardSpeed);

        int next;
        if (!Has(Igi2PlayerSignal.Grounded))
        {
            next = Igi2PlayerState.Fall;
        }
        else if (!Allows(Igi2AllowedStance.Lie))
        {
            next = Allows(Igi2AllowedStance.Crouch) ? ToggledCrouch() : Igi2PlayerState.Stand;
        }
        else if (Has(Igi2PlayerSignal.StandUp) && Allows(Igi2AllowedStance.Stand))
        {
            next = Igi2PlayerState.Stand;
        }
        else if (Has(Igi2PlayerSignal.RaiseStance) && Allows(Igi2AllowedStance.Crouch))
        {
            next = ToggledCrouch();
        }
        else if (Has(Igi2PlayerSignal.Dead))
        {
            next = Igi2PlayerState.Die;
        }
        else if ((Signal & CrawlKeys) != 0 && ActionState == Igi2PlayerActionState.Idle)
        {
            next = Igi2PlayerState.FlatCrawl;
        }
        else
        {
            return;
        }

        GroundTolerance = LandingGroundTolerance;
        Go(next);
    }

    // State 80 -- InitStateFlatCrawl 0x4B2F10, RunStateFlatCrawl 0x4B2FD0, ExitStateFlatCrawl 0x4B33C0.
    private void EnterFlatCrawl()
    {
        Pose = Igi2HumanPose.LyingFlat;
        Animation.PlayState(Igi2PlayerState.FlatCrawl, 0, StateBlendSeconds);
        AimPrecision(Igi2PrecisionType.LyingFlat, static p => p.LyingFlat);

        // The reference is forgotten, not stopped: a crawl entered afresh starts its own loop.
        _crawlSound = null;
        GroundTolerance = ProneGroundTolerance;
        Locomotion.Current = (_, _) => RunFlatCrawl();
    }

    private void RunFlatCrawl()
    {
        bool soundAlive = _crawlSound is { IsAlive: true };
        if (soundAlive)
        {
            _crawlSound!.MoveToHuman();
        }

        if ((Signal & MoveKeys) != 0)
        {
            AimPrecision(Igi2PrecisionType.Crawling, static p => p.Crawling);
        }
        else
        {
            AimPrecision(Igi2PrecisionType.LyingFlat, static p => p.LyingFlat);
        }

        int next = Stay;
        if (Has(Igi2PlayerSignal.Dead))
        {
            next = Igi2PlayerState.Die;
        }
        else if (!Has(Igi2PlayerSignal.Grounded))
        {
            next = Igi2PlayerState.Fall;
        }

        // The original tests "lying not allowed" here as bit 4 of a 0-or-1 flag, so the test never passes
        // and a crawl is never pushed out of lying the way lying flat is.
        else if (Has(Igi2PlayerSignal.RaiseStance) && Allows(Igi2AllowedStance.Crouch))
        {
            next = ToggledCrouch();
        }
        else if (Has(Igi2PlayerSignal.StandUp) && Allows(Igi2AllowedStance.Stand))
        {
            next = Igi2PlayerState.Stand;
        }
        else if (Has(Igi2PlayerSignal.Fire | Igi2PlayerSignal.Reload))
        {
            next = Igi2PlayerState.Flat;
        }
        else if ((Signal & CrawlKeys) != 0)
        {
            Crawl(soundAlive);
        }
        else
        {
            next = Igi2PlayerState.Flat;
        }

        if (WaterDepth >= CrawlWaterDepthLimit)
        {
            Go(Igi2PlayerState.Stand);
        }
        else if (next != Stay)
        {
            Go(next);
        }
    }

    private void ExitFlatCrawl()
    {
        GroundTolerance = LandingGroundTolerance;
        StopCrawlSound();
    }

    /// <summary>One tick of crawling: the loop sound follows the keys, and the legs steer to the table's speed.</summary>
    private void Crawl(bool soundAlive)
    {
        int side = (Has(Igi2PlayerSignal.Left) ? 1 : 0) + (Has(Igi2PlayerSignal.Right) ? 2 : 0);
        int along = (Has(Igi2PlayerSignal.Backward) ? 1 : 0) + (Has(Igi2PlayerSignal.Forward) ? 2 : 0);

        if (side != 0 || along != 0)
        {
            if (!soundAlive)
            {
                _crawlSound = Surroundings.StartSound("crawl_loop");
            }
        }
        else if (soundAlive)
        {
            StopCrawlSound();
        }

        (float x, float y) = CrawlSpeeds[side + (4 * along)];
        MoveOnGround(y * MovementSpeedScale, CrawlStep, x * MovementSpeedScale, CrawlStep);
    }

    private void StopCrawlSound()
    {
        _crawlSound?.Delete();
        _crawlSound = null;
    }

    /// <summary>A crouch reached from lying down is a toggled one, not a held one.</summary>
    private int ToggledCrouch()
    {
        CrouchIsHeld = false;
        return Igi2PlayerState.Crouch;
    }

    private const Igi2PlayerSignal MoveKeys =
        Igi2PlayerSignal.Left | Igi2PlayerSignal.Right | Igi2PlayerSignal.Forward | Igi2PlayerSignal.Backward;

    private const Igi2PlayerSignal CrawlKeys = MoveKeys | Igi2PlayerSignal.Turning;
}
