using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Whether a culler may act on its occlusion verdicts: the view has held still long enough for them to describe it.</summary>
/// <remarks>
/// A verdict is read back once the slot's fence comes round, so the culler skips a page a frame or two after the depth that
/// judged it was written. Standing still that is exactly right. Turning is a statement about where the camera was looking,
/// not where it is: a page that has just come out from behind a hill still carries the verdict it earned while hidden, so
/// it is skipped -- the hole -- until its retest lands and it is drawn again.
/// </remarks>
/// <param name="windowed">
/// Whether sustained travel is also judged over a window of frames (see <see cref="WindowFrames"/>), as well as each frame's
/// own step.
/// </param>
public sealed class OcclusionSettle(bool windowed)
{
    /// <summary>How many frames after the last movement the verdicts stay unusable.</summary>
    /// <remarks>Long enough to cover the deepest frame queue the backends run.</remarks>
    public const int SettleFrames = 3;

    /// <summary>How far the camera may drift per frame and still count as holding still, in metres.</summary>
    public const double SettlePosition = 0.05;

    /// <summary>How far any orientation element may move per frame and still count as holding still.</summary>
    /// <remarks>About a fifth of a degree: below what a hand on a mouse holds, above sensor noise.</remarks>
    public const float SettleOrientation = 0.003f;

    /// <summary>How many frames the cumulative-movement window spans.</summary>
    /// <remarks>
    /// The per-frame <see cref="SettlePosition"/> is fooled by a HIGH FRAME RATE and by a SLOW CINEMATIC PAN: sustained travel
    /// whose every step is under the per-frame threshold reads as "still" frame after frame, so occlusion re-engages while the
    /// camera is in fact moving, and terrain emerging from behind a hill is culled on the stale "occluded" verdict it earned
    /// while hidden -- the black flash on a mission switch and during fast flight. Judging the camera against where it was a
    /// whole window ago catches that travel however it is paced, because occlusion depends on camera POSITION (a hill either
    /// hides a point or it does not), never on where the camera looks -- so a windowed position test is both sufficient and
    /// exact.
    /// </remarks>
    public const int WindowFrames = 12;

    /// <summary>Cumulative metres over the window above which the camera counts as moving.</summary>
    public const double WindowDistance = 0.1;

    // A ring of the last WindowFrames camera positions, oldest at _windowNext once filled.
    private readonly Vector3d[]? _window = windowed ? new Vector3d[WindowFrames] : null;
    private int _windowNext;
    private bool _windowFilled;
    private Camera3D _previous;
    private bool _hasPrevious;
    private int _settle;

    /// <summary>Gets whether visibility verdicts may be acted on this frame.</summary>
    /// <param name="camera">This frame's camera.</param>
    /// <returns><see langword="true"/> once the view has held still for <see cref="SettleFrames"/> frames.</returns>
    /// <remarks>
    /// Not a test of this frame alone. A view that has just stopped still has verdicts in flight that were measured while it
    /// moved, so the count runs on for <see cref="SettleFrames"/> frames after the last movement and only then lets the culler
    /// skip anything again.
    /// </remarks>
    public bool VerdictsUsable(in Camera3D camera)
    {
        // The window is evaluated unconditionally -- it maintains the position ring -- so it must run even when the
        // per-frame test has already decided the camera moved.
        bool windowMoved = WindowMoved(camera.Position);
        if (ViewMoved(camera) || windowMoved)
        {
            _settle = SettleFrames;
        }
        else if (_settle > 0)
        {
            _settle--;
        }

        return _settle == 0;
    }

    /// <summary>Records the camera a pass was culled from, which the next frame's step is judged against.</summary>
    /// <param name="camera">The camera.</param>
    public void Observed(in Camera3D camera)
    {
        _previous = camera;
        _hasPrevious = true;
    }

    /// <summary>Forgets the last camera, so the next frame counts as a cut.</summary>
    public void Forget() => _hasPrevious = false;

    // Whether the camera has travelled more than the window allows over the last several frames.
    private bool WindowMoved(Vector3d position)
    {
        if (_window is null)
        {
            return false;
        }

        // The entry about to be overwritten is the oldest -- WindowFrames ago -- once the ring has filled. Before then there
        // is no window to compare against and the per-frame test carries the load.
        bool moved = _windowFilled && (position - _window[_windowNext]).Length > WindowDistance;
        _window[_windowNext] = position;
        _windowNext++;
        if (_windowNext >= _window.Length)
        {
            _windowNext = 0;
            _windowFilled = true;
        }

        return moved;
    }

    private bool ViewMoved(in Camera3D camera)
    {
        if (!_hasPrevious)
        {
            return true;
        }

        if ((camera.Position - _previous.Position).Length > SettlePosition)
        {
            return true;
        }

        Matrix3f a = camera.Orientation;
        Matrix3f b = _previous.Orientation;
        float largest = 0f;
        largest = Math.Max(largest, Math.Abs(a.M11 - b.M11));
        largest = Math.Max(largest, Math.Abs(a.M12 - b.M12));
        largest = Math.Max(largest, Math.Abs(a.M13 - b.M13));
        largest = Math.Max(largest, Math.Abs(a.M21 - b.M21));
        largest = Math.Max(largest, Math.Abs(a.M22 - b.M22));
        largest = Math.Max(largest, Math.Abs(a.M23 - b.M23));
        largest = Math.Max(largest, Math.Abs(a.M31 - b.M31));
        largest = Math.Max(largest, Math.Abs(a.M32 - b.M32));
        largest = Math.Max(largest, Math.Abs(a.M33 - b.M33));
        return largest > SettleOrientation;
    }
}
