namespace OpenIGI.Input;

/// <summary>A finger on the screen.</summary>
/// <param name="Id">What tells it from the other fingers for as long as it stays down.</param>
/// <param name="X">Where it is now, in window pixels.</param>
/// <param name="Y">Where it is now, in window pixels.</param>
/// <param name="StartX">Where it came down, in window pixels.</param>
/// <param name="StartY">Where it came down, in window pixels.</param>
/// <param name="DeltaX">How far it has moved this frame, in window pixels.</param>
/// <param name="DeltaY">How far it has moved this frame, in window pixels.</param>
/// <param name="Began">Whether it came down this frame.</param>
/// <param name="Ended">Whether it was lifted this frame; it is gone the next.</param>
public readonly record struct TouchPoint(
    int Id, float X, float Y, float StartX, float StartY, float DeltaX, float DeltaY, bool Began, bool Ended);

/// <summary>The fingers on a touch screen this frame, as the window reported them.</summary>
/// <remarks>
/// The window reports each finger coming down, moving and lifting; a finger that comes down and lifts within one frame is
/// still seen for that frame, with <see cref="TouchPoint.Began"/> and <see cref="TouchPoint.Ended"/> both set, so a quick tap is
/// never lost between two frames.
/// </remarks>
public sealed class TouchState
{
    private readonly List<TouchPoint> _points = [];

    /// <summary>Gets the fingers down this frame, and those lifted this frame.</summary>
    public IReadOnlyList<TouchPoint> Points => _points;

    /// <summary>Gets whether the screen has been touched since the window opened: whether there is a touch screen to show controls on.</summary>
    public bool InUse { get; private set; }

    /// <summary>Gets whether any finger is down.</summary>
    public bool AnyDown
    {
        get
        {
            foreach (TouchPoint point in _points)
            {
                if (!point.Ended)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Starts a frame: fingers lifted last frame are let go, and this frame's movement starts from nought.</summary>
    public void BeginFrame()
    {
        _points.RemoveAll(static point => point.Ended);
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] = _points[i] with { DeltaX = 0f, DeltaY = 0f, Began = false };
        }
    }

    /// <summary>A finger came down.</summary>
    public void Down(int id, float x, float y)
    {
        InUse = true;
        int at = IndexOf(id);
        TouchPoint point = new(id, x, y, x, y, 0f, 0f, Began: true, Ended: false);
        if (at < 0)
        {
            _points.Add(point);
        }
        else
        {
            _points[at] = point;
        }
    }

    /// <summary>A finger moved.</summary>
    public void Move(int id, float x, float y)
    {
        int at = IndexOf(id);
        if (at < 0 || _points[at].Ended)
        {
            return;
        }

        TouchPoint point = _points[at];
        _points[at] = point with { X = x, Y = y, DeltaX = point.DeltaX + (x - point.X), DeltaY = point.DeltaY + (y - point.Y) };
    }

    /// <summary>A finger was lifted, or the system took it away.</summary>
    public void Up(int id, float x, float y)
    {
        Move(id, x, y);
        int at = IndexOf(id);
        if (at >= 0)
        {
            _points[at] = _points[at] with { Ended = true };
        }
    }

    /// <summary>Lets go of every finger, as the window loses the screen.</summary>
    public void Release()
    {
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] = _points[i] with { Ended = true };
        }
    }

    private int IndexOf(int id)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            if (_points[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
