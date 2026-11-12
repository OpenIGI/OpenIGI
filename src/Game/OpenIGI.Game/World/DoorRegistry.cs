using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One placed door — its state machine, where it stands and which placement it draws through.</summary>
/// <param name="Task">The exact <c>Door</c> task instance.</param>
/// <param name="Position">Its authored <c>Position start</c>, in world units.</param>
/// <param name="Orientation">Its closed orientation (<c>0x4B38E0</c> of its authored angles).</param>
/// <param name="StopX">Authored local X translation at fully open, in metres (<c>+264</c>).</param>
/// <param name="StopY">Authored local Y translation at fully open, in metres (<c>+268</c>).</param>
/// <param name="StopZ">Authored <c>Position slider</c> (<c>+312</c>): the slide fraction the door starts at.</param>
/// <param name="State">Its state machine.</param>
/// <param name="OpenSound">The definition played when the door reaches fully open.</param>
/// <param name="CloseSound">The definition played when it reaches fully closed.</param>
/// <param name="MoveSound">The definition played while it travels.</param>
/// <remarks>
/// The pose is the original's, in world units with its x87 arithmetic: the start (<c>+240</c>), the base orientation
/// (<c>+272</c>), the position (<c>+32</c>) and orientation (<c>+112</c>) they make with the slide and swing, and, for a
/// door inside another door, where it sits in that door's frame (<c>+744</c>, <c>+768</c>). A door moves the doors
/// inside it: a telescoping gate is a chain of them, each riding the one before.
/// </remarks>
public sealed record PlacedDoor(
    GameTask Task,
    Vector3d Position,
    Matrix3f Orientation,
    float StopX,
    float StopY,
    float StopZ,
    DoorState State,
    string OpenSound = "",
    string CloseSound = "",
    string MoveSound = "") : IAiDoor
{
    private static readonly float DegreesToRadians = BitConverter.Int32BitsToSingle(0x3C8EFA35);

    private readonly double[] _start = new double[3];
    private readonly float[] _base = new float[9];
    private readonly double[] _pose = new double[3];
    private readonly float[] _m = new float[9];
    private readonly double[] _previousPose = new double[3];
    private readonly float[] _previousM = new float[9];
    private readonly double[] _local = new double[3];
    private readonly float[] _localM = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    private readonly List<PlacedDoor> _children = [];
    private bool _placed;
    private bool _hasPrevious;
    private float _poseSlide;
    private float _poseAngle;

    /// <inheritdoc/>
    /// <remarks>Its authored start, which the walker's query measures from (<c>DoorRegistry.Nearest</c>).</remarks>
    Vector3d IAiDoor.DoorPosition => Position;

    /// <inheritdoc/>
    int IAiDoor.UseState => State.UseState;

    /// <inheritdoc/>
    /// <remarks>The walker's open (<c>sub_447DA0</c>'s command): the latch set.</remarks>
    void IAiDoor.OpenForSoldier() => State.CommandOpen();

    /// <summary>
    /// Gets or sets the door's expressions and lock, run by its own update between its children and its motion; <see langword="null"/>
    /// runs no expression and only keeps the open count.
    /// </summary>
    public Action? Conditions { get; set; }

    /// <summary>Gets or sets whether the door's travelling sound is running.</summary>
    /// <remarks>
    /// The original keeps the travelling sound's task handle in the door itself, at <c>+736</c>, which
    /// is what tells it the sound is already going. This is that slot, reduced to the one bit the port
    /// needs — the voice itself belongs to whoever took <c>DoorRegistry.SetSoundSink</c>.
    /// </remarks>
    public bool IsSounding { get; set; }

    /// <summary>A door is one object: two are equal only when they are the same one.</summary>
    /// <param name="other">The other door.</param>
    /// <returns>Whether they are the same door.</returns>
    /// <remarks>
    /// Value equality over a door whose pose and sound slot change every tick would move it in any dictionary it is
    /// a key of -- the host keeps each door's travelling voice that way.
    /// </remarks>
    public bool Equals(PlacedDoor? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    /// <summary>Gets the door this one sits inside (<c>0x4474E0</c>'s object ancestor, when it is a door).</summary>
    public PlacedDoor? Parent { get; private set; }

    /// <summary>Gets the door's current position, world units (<c>+32</c>).</summary>
    public Vector3d PoseUnits
    {
        get
        {
            Sync();
            return new(_pose[0], _pose[1], _pose[2]);
        }
    }

    /// <summary>Gets the door's current orientation (<c>+112</c>).</summary>
    public Matrix3f PoseOrientation
    {
        get
        {
            Sync();
            return RetailOrientation.ToMatrix(_m);
        }
    }

    /// <summary>Gets the door's closed position in metres — the centre of its opening.</summary>
    public Vector3d ClosedPosition => new(
        Position.X / WorldScene.UnitsPerMetre,
        Position.Y / WorldScene.UnitsPerMetre,
        Position.Z / WorldScene.UnitsPerMetre);

    /// <summary>Gets the two world-space endpoints of the doorway, spanning the opening.</summary>
    /// <remarks>
    /// The doorway spans across the door's local X axis (the width axis) at the closed position. A
    /// human standing anywhere on this segment is inside the opening and keeps a closing door from
    /// sealing — the mid-swing blocking ray the retail handler casts between its two child-task
    /// positions (0x446D10 at 0x447302).
    /// </remarks>
    public (Vector3d A, Vector3d B) DoorwaySegment
    {
        get
        {
            Vector3d across = new(Orientation.Column1.X, Orientation.Column1.Y, Orientation.Column1.Z);
            double length = across.Length;
            if (length > 0.0)
            {
                across /= length;
            }

            Vector3d centre = ClosedPosition;
            Vector3d half = across * DoorwayHalfWidth;
            return (centre - half, centre + half);
        }
    }

    /// <summary>Gets or sets the doorway's half width, in metres, spanning the opening.</summary>
    /// <remarks>Defaults to a one-metre half width — a body standing in the opening is within it.</remarks>
    public double DoorwayHalfWidth { get; set; } = 1.0;

    /// <summary>Gets the door's current drawable position in metres.</summary>
    public Vector3d DrawPosition
    {
        get
        {
            Sync();
            return Metres(_pose);
        }
    }

    /// <summary>Gets the door's current drawable orientation.</summary>
    public Matrix3f DrawOrientation
    {
        get
        {
            Sync();
            return RetailOrientation.ToMatrix(_m);
        }
    }

    /// <summary>Gets the door's pose as this frame presents it, between its last two ticks.</summary>
    /// <param name="tickFraction">How far the frame stands between the two simulation states.</param>
    /// <param name="smooth">Whether to interpolate at all; <see langword="false"/> takes the newer state.</param>
    /// <returns>The position in metres and the orientation.</returns>
    /// <remarks>
    /// The door moves on the thirty-hertz tick and is drawn far more often than that, so everything
    /// that has to sit on the door — its mesh, and any mark placed on it — has to ask for the same
    /// blended pose. Reading the tick state directly is what makes a thing on a moving door judder
    /// while the door itself glides. Each tick state is the original's exactly; only the frames between blend.
    /// </remarks>
    public (Vector3d Position, Matrix3f Orientation) PresentedPose(float tickFraction, bool smooth)
    {
        float alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1f;
        if (alpha >= 1f)
        {
            return (DrawPosition, DrawOrientation);
        }

        // The registry keeps each tick's pose, so a door carried by its parent glides with it; a door stepped on its
        // own blends from where its previous slide would have put it.
        Sync();
        Vector3d to = Metres(_pose);
        Vector3d from = _hasPrevious ? Metres(_previousPose) : to + (SlideOffset(State.PreviousSlideFraction) - SlideOffset(State.SlideFraction));
        Vector3d position = from + ((to - from) * alpha);
        if (State.PreviousAngleDegrees != State.AngleDegrees)
        {
            float angle = State.PreviousAngleDegrees + ((State.AngleDegrees - State.PreviousAngleDegrees) * alpha);
            return (position, RetailOrientation.ToMatrix(_base) * Matrix3f.RotationZ(angle * (MathF.PI / 180f)));
        }

        return (position, DrawOrientation);
    }

    /// <summary>The post-load placement (<c>0x447830</c>): the base orientation, the slide's start, the frame in its parent.</summary>
    /// <param name="baseOrientation">The base orientation, row-major.</param>
    /// <param name="parent">The door it sits inside, already placed, or <see langword="null"/>.</param>
    public void Initialise(float[] baseOrientation, PlacedDoor? parent)
    {
        PlaceAtStart(baseOrientation);
        Parent = parent;
        parent?._children.Add(this);
        Capture();
        Array.Copy(_pose, _previousPose, 3);
        Array.Copy(_m, _previousM, 9);
    }

    /// <summary>Keeps this tick's pose as the one the next frames blend from.</summary>
    public void KeepPrevious()
    {
        Sync();
        Array.Copy(_pose, _previousPose, 3);
        Array.Copy(_m, _previousM, 9);
        _hasPrevious = true;
    }

    /// <summary>
    /// Brings the pose up to the state: a slide the pose was not built at re-places the door (<c>0x446290</c>), an
    /// angle it was not built at re-turns it (<c>0x4B3790</c>) -- what the door's update does the tick either moves.
    /// </summary>
    public void Sync()
    {
        if (!_placed)
        {
            float[] m = [Orientation.M11, Orientation.M12, Orientation.M13, Orientation.M21, Orientation.M22, Orientation.M23, Orientation.M31, Orientation.M32, Orientation.M33];
            PlaceAtStart(m);
        }

        if (State.SlideFraction != _poseSlide)
        {
            Place();
        }

        if (State.AngleDegrees != _poseAngle)
        {
            Swing();
        }
    }

    private void PlaceAtStart(float[] baseOrientation)
    {
        _placed = true;
        _start[0] = Position.X;
        _start[1] = Position.Y;
        _start[2] = Position.Z;
        Array.Copy(baseOrientation, _base, 9);
        Place();
        Array.Copy(_base, _m, 9);
        _poseAngle = 0f;
        if (State.AngleDegrees != 0f)
        {
            Swing();
        }
    }

    /// <summary>The slide's offset from the start at a fraction, metres -- for blending between two ticks only.</summary>
    private Vector3d SlideOffset(float slide)
    {
        double sx = StopX * slide, sy = StopY * slide;
        return new Vector3d(
            (_base[1] * sy) + (_base[0] * sx),
            (_base[4] * sy) + (_base[3] * sx),
            (_base[7] * sy) + (_base[6] * sx));
    }

    /// <summary>The swing (<c>0x446D10</c> → <c>0x4B3790</c>): the base turned about its Z by the angle.</summary>
    public void Swing()
    {
        _poseAngle = State.AngleDegrees;
        float radians = ((X87)State.AngleDegrees * DegreesToRadians).F;
        float[] r = RetailOrientation.Euler(0f, 0f, radians);
        float[] b = _base;
        for (int row = 0; row < 3; row++)
        {
            float b0 = b[(3 * row) + 0], b1 = b[(3 * row) + 1], b2 = b[(3 * row) + 2];
            _m[(3 * row) + 0] = ((X87)r[0] * b0 + (X87)r[6] * b2 + (X87)r[3] * b1).F;
            _m[(3 * row) + 1] = ((X87)r[1] * b0 + (X87)r[7] * b2 + (X87)r[4] * b1).F;
            _m[(3 * row) + 2] = ((X87)r[2] * b0 + (X87)r[8] * b2 + (X87)r[5] * b1).F;
        }
    }

    /// <summary><c>0x446290</c>: the start plus the base's X and Y scaled by the stops and the slide, world units.</summary>
    public void Place()
    {
        float slide = State.SlideFraction;
        _poseSlide = slide;
        X87 sx = (X87)StopX * slide * 4096.0;
        X87 sy = (X87)StopY * slide * 4096.0;
        float[] b = _base;
        double y = ((X87)b[4] * sy + (X87)b[3] * sx).Value;
        double z = ((X87)b[7] * sy + (X87)b[6] * sx).Value;
        _pose[0] = X87.Add(((X87)b[1] * sy + (X87)b[0] * sx).Value, _start[0]);
        _pose[1] = X87.Add(y, _start[1]);
        _pose[2] = X87.Add(z, _start[2]);
    }

    /// <summary><c>0x4474E0</c>: where the door's start and base orientation sit in its parent's frame.</summary>
    public void Capture()
    {
        if (Parent is not { } parent)
        {
            return;
        }

        double[] pp = parent._pose;
        float[] p = parent._m;
        double dx = X87.Add(_start[0], -pp[0]);
        double dy = X87.Add(_start[1], -pp[1]);
        double dz = X87.Add(_start[2], -pp[2]);
        _local[0] = ((X87)p[0] * dx + (X87)p[3] * dy + (X87)p[6] * dz).Value;
        _local[1] = ((X87)p[1] * dx + (X87)p[7] * dz + (X87)p[4] * dy).Value;
        _local[2] = ((X87)p[2] * dx + (X87)p[8] * dz + (X87)p[5] * dy).Value;

        // The parent's orientation transposed, then times the base.
        float t0 = p[0], t1 = p[3], t2 = p[6], t3 = p[1], t4 = p[4], t5 = p[7], t6 = p[2], t7 = p[5], t8 = p[8];
        float[] b = _base;
        float[] l = _localM;
        l[0] = ((X87)t3 * b[1] + (X87)b[0] * t0 + (X87)b[2] * t6).F;
        l[1] = ((X87)t4 * b[1] + (X87)b[0] * t1 + (X87)t7 * b[2]).F;
        l[2] = ((X87)t5 * b[1] + (X87)t2 * b[0] + (X87)t8 * b[2]).F;
        l[3] = ((X87)t0 * b[3] + (X87)b[5] * t6 + (X87)t3 * b[4]).F;
        l[4] = ((X87)b[3] * t1 + (X87)t7 * b[5] + (X87)t4 * b[4]).F;
        l[5] = ((X87)t2 * b[3] + (X87)t8 * b[5] + (X87)t5 * b[4]).F;
        l[6] = ((X87)t3 * b[7] + (X87)t6 * b[8] + (X87)t0 * b[6]).F;
        l[7] = ((X87)t7 * b[8] + (X87)t4 * b[7] + (X87)b[6] * t1).F;
        l[8] = ((X87)t8 * b[8] + (X87)t5 * b[7] + (X87)t2 * b[6]).F;
    }

    /// <summary><c>0x4463B0</c>: tells the doors inside this one that it moved.</summary>
    public void MoveChildren()
    {
        foreach (PlacedDoor child in _children)
        {
            child.Follow(_pose, _m);
        }
    }

    /// <summary>
    /// <c>0x445DE0</c>: the parent moved, so the start and base orientation are taken back out of its frame, the door
    /// re-placed at its slide, the base re-orthonormalised and made the orientation, and the doors inside it moved.
    /// </summary>
    private void Follow(double[] pp, float[] p)
    {
        double[] l = _local;
        double x = ((X87)p[2] * l[2] + (X87)p[1] * l[1] + (X87)p[0] * l[0]).Value;
        double y = ((X87)p[5] * l[2] + (X87)p[4] * l[1] + (X87)p[3] * l[0]).Value;
        double z = ((X87)p[8] * l[2] + (X87)p[7] * l[1] + (X87)p[6] * l[0]).Value;
        _start[0] = X87.Add(pp[0], x);
        _start[1] = X87.Add(pp[1], y);
        _start[2] = X87.Add(pp[2], z);

        float[] m = _localM;
        float[] v =
        [
            ((X87)p[2] * m[6] + (X87)p[1] * m[3] + (X87)m[0] * p[0]).F,
            ((X87)p[2] * m[7] + (X87)p[1] * m[4] + (X87)m[1] * p[0]).F,
            ((X87)p[2] * m[8] + (X87)p[1] * m[5] + (X87)m[2] * p[0]).F,
            ((X87)p[4] * m[3] + (X87)m[6] * p[5] + (X87)m[0] * p[3]).F,
            ((X87)p[4] * m[4] + (X87)m[7] * p[5] + (X87)m[1] * p[3]).F,
            ((X87)p[4] * m[5] + (X87)m[8] * p[5] + (X87)m[2] * p[3]).F,
            ((X87)p[7] * m[3] + (X87)m[6] * p[8] + (X87)m[0] * p[6]).F,
            ((X87)p[7] * m[4] + (X87)m[7] * p[8] + (X87)m[1] * p[6]).F,
            ((X87)p[7] * m[5] + (X87)m[8] * p[8] + (X87)m[2] * p[6]).F,
        ];
        Array.Copy(v, _base, 9);
        Place();
        Orthonormalise(_base);
        Array.Copy(_base, _m, 9);
        MoveChildren();
    }

    /// <summary><c>0x445DE0</c>'s re-orthonormalisation, in its expression order.</summary>
    private static void Orthonormalise(float[] v)
    {
        float[] n = new float[9];
        n[0] = v[0];
        n[1] = v[1];
        n[2] = v[2];
        if (!(v[0] == 0f && v[1] == 0f && v[2] == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)v[0] * v[0] + (X87)v[1] * v[1] + (X87)v[2] * v[2]);
            n[0] = ((X87)v[0] * inverse).F;
            n[1] = ((X87)n[1] * inverse).F;
            n[2] = ((X87)n[2] * inverse).F;
        }

        float r1x = ((X87)v[2] * v[7] - (X87)v[1] * v[8]).F;
        float r1y = (-((X87)v[2] * v[6] - (X87)v[0] * v[8])).F;
        float r1z = ((X87)v[1] * v[6] - (X87)v[0] * v[7]).F;
        float r2x = ((X87)r1z * n[1] - (X87)r1y * n[2]).F;
        float r2y = (-((X87)r1z * n[0] - (X87)r1x * n[2])).F;
        float r2z = ((X87)r1y * n[0] - (X87)r1x * n[1]).F;
        n[3] = r1x;
        n[4] = r1y;
        n[5] = r1z;
        n[6] = r2x;
        n[7] = r2y;
        n[8] = r2z;
        if (!(r1x == 0f && r1y == 0f && r1z == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)r1x * r1x + (X87)r1z * r1z + (X87)r1y * r1y);
            n[3] = ((X87)r1x * inverse).F;
            n[4] = ((X87)n[4] * inverse).F;
            n[5] = ((X87)n[5] * inverse).F;
        }

        if (!(r2x == 0f && r2y == 0f && r2z == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)r2x * r2x + (X87)r2y * r2y + (X87)r2z * r2z);
            n[6] = ((X87)r2x * inverse).F;
            n[7] = ((X87)n[7] * inverse).F;
            n[8] = ((X87)n[8] * inverse).F;
        }

        Array.Copy(n, v, 9);
    }

    private static Vector3d Metres(double[] units) => new(
        units[0] / WorldScene.UnitsPerMetre,
        units[1] / WorldScene.UnitsPerMetre,
        units[2] / WorldScene.UnitsPerMetre);
}
