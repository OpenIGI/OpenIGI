using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>Whether a lying body has room to turn (<c>human.c Human_IsGammaRotationAllowed</c>).</content>
public abstract partial class Igi2Human
{
    /// <summary>How far a lying body turns between two of the lines its turn is checked with, ten degrees.</summary>
    private const float TurnRoomStep = 0.1745329350233078f;

    /// <summary>The most lines a turn is checked with, <c>72</c>: two a step while fewer than 71 are taken.</summary>
    private const int MaxTurnRoomLines = 72;

    /// <summary>A lying body's turn that strikes something steeper than this, the cosine of 60 degrees, is refused.</summary>
    private const float TurnRoomSteepest = 0.5f;

    /// <summary>A refused turn pushes a lying body off the walls struck by this much per radian, <c>81.92</c>.</summary>
    private const float TurnRoomPush = 81.91999816894531f;

    /// <summary>The lying body's head and feet, in its own frame, as a turn sweeps them: half a metre ahead, a fifth behind.</summary>
    private const float TurnRoomAhead = 2048f;

    private const float TurnRoomBehind = -819.2000122070312f;

    private const float TurnRoomSide = 204.8000030517578f;

    private const float TurnRoomHeight = 819.2000122070312f;

    private readonly WorldLineTrace _turnRoom = new(MaxTurnRoomLines)
    {
        Mesh = FootingMesh,
        Includes = static owner => !IsSweptThrough(owner),
    };

    /// <summary>
    /// Refuses a lying body a turn it has no room for: <c>Human_UpdateBody</c>'s check, through
    /// <c>Human_IsGammaRotationAllowed</c> (<c>igi2 0x505D40</c>).
    /// </summary>
    /// <param name="turn">This tick's turn, radians.</param>
    /// <returns>The turn the body takes: all of it, or none.</returns>
    /// <remarks>
    /// A refused turn pushes the body away from what it struck, along the averaged normal by the size of the turn, unless the
    /// walls struck lie both ahead and behind, or both left and right, of the body.
    /// </remarks>
    private Fpu CheckTurnRoom(Fpu turn)
    {
        float side = turn.Value < 0.0 ? -1f : 1f;
        Vector3f head = new(((Fpu)(-TurnRoomSide) * side).F, TurnRoomAhead, TurnRoomHeight);
        Vector3f feet = new(((Fpu)side * TurnRoomSide).F, TurnRoomBehind, TurnRoomHeight);
        if (IsGammaRotationAllowed(Orientation, Position, head, feet, turn.F, out int hitBits, out Vector3f pushNormal))
        {
            return turn;
        }

        bool boxedAlong = (hitBits & 1) != 0 && (hitBits & 2) != 0;
        bool boxedAcross = (hitBits & 4) != 0 && (hitBits & 8) != 0;
        if (!boxedAlong && !boxedAcross)
        {
            Fpu size = Fpu.Abs(turn);
            DeltaTranslation = new Vector3f(
                ((size * pushNormal.X * TurnRoomPush) + DeltaTranslation.X).F,
                ((size * pushNormal.Y * TurnRoomPush) + DeltaTranslation.Y).F,
                ((size * pushNormal.Z * TurnRoomPush) + DeltaTranslation.Z).F);
        }

        return (Fpu)0f;
    }

    /// <summary>
    /// Whether a body may turn by an angle, <c>Human_IsGammaRotationAllowed</c> (<c>igi2 0x505D40</c>): a line is swept from a
    /// point above its centre to where two of its points are carried by each ten degrees of the turn, and any line striking
    /// something steeper than sixty degrees refuses it.
    /// </summary>
    /// <param name="o">The body's orientation.</param>
    /// <param name="centre">The body's position.</param>
    /// <param name="front">The first point, in the body's frame; its height also lifts every line's start.</param>
    /// <param name="rear">The second point, in the body's frame.</param>
    /// <param name="gamma">The turn, radians.</param>
    /// <param name="hitBits">Where the refusing hits lie in the body's frame: 1 ahead, 2 behind, 4 to the right, 8 to the left.</param>
    /// <param name="averageNormal">The refusing hits' unit normals, averaged.</param>
    /// <returns><see langword="true"/> when nothing refuses the turn.</returns>
    private bool IsGammaRotationAllowed(
        Matrix3f o, Vector3d centre, Vector3f front, Vector3f rear, float gamma, out int hitBits, out Vector3f averageNormal)
    {
        hitBits = 0;
        averageNormal = default;
        bool allowed = true;
        int hits = 0;
        Fpu sign = (Fpu)1f;

        Fpu fz = (Fpu)front.Z;
        Vector3d start = new(
            (((((Fpu)o.M11 + o.M12) * 0.0) + (fz * o.M13)) + centre.X).Value,
            (((((Fpu)o.M21 + o.M22) * 0.0) + (fz * o.M23)) + centre.Y).Value,
            (((((Fpu)o.M31 + o.M32) * 0.0) + (fz * o.M33)) + centre.Z).Value);
        Fpu left = (Fpu)gamma;
        if (left.Value < 0.0)
        {
            sign = (Fpu)(-1f);
            left = -left;
        }

        Fpu angle = (Fpu)0f;
        int lines = 0;
        while (left.Value != 0.0)
        {
            if (left.Value <= TurnRoomStep)
            {
                angle += left;
                left = (Fpu)0f;
            }
            else
            {
                angle += TurnRoomStep;
                left -= TurnRoomStep;
            }

            if (lines >= MaxTurnRoomLines - 1)
            {
                break;
            }

            Fpu sin = Fpu.Sin(sign * angle);
            Fpu cos = Fpu.Cos(sign * angle);
            _turnRoom.Starts[lines] = start;
            _turnRoom.Ends[lines] = Carried(o, centre, front, sin, cos);
            _turnRoom.Starts[lines + 1] = start;
            _turnRoom.Ends[lines + 1] = Carried(o, centre, rear, sin, cos);
            lines += 2;
        }

        _turnRoom.Count = lines;
        _turnRoom.Evaluator = this;
        Surroundings.Collision.TraceLines(_turnRoom, SweepGroups);
        if (lines <= 0)
        {
            return true;
        }

        Fpu sumX = (Fpu)0f, sumY = (Fpu)0f, sumZ = (Fpu)0f;
        for (int i = 0; i < lines; i++)
        {
            WorldIntersection hit = _turnRoom.Intersections[i];
            if (!hit.IsHit || Math.Abs(hit.Normal.Z) >= TurnRoomSteepest)
            {
                continue;
            }

            allowed = false;
            float dx = ((Fpu)hit.Position.X - centre.X).F;
            float dy = ((Fpu)hit.Position.Y - centre.Y).F;
            float dz = ((Fpu)hit.Position.Z - centre.Z).F;
            Fpu across = (((Fpu)dy * o.M21) + ((Fpu)dz * o.M31)) + ((Fpu)dx * o.M11);
            Fpu along = (((Fpu)dy * o.M22) + ((Fpu)dx * o.M12)) + ((Fpu)dz * o.M32);
            hitBits |= along.Value < 0.0 ? 2 : 1;
            hitBits |= across.Value < 0.0 ? 8 : 4;
            hits++;

            Vector3f n = hit.Normal;
            if (n.X != 0f || n.Y != 0f || n.Z != 0f)
            {
                Fpu inverse = (Fpu)1f / Fpu.Sqrt(((Fpu)n.Z * n.Z) + ((Fpu)n.Y * n.Y) + ((Fpu)n.X * n.X));
                n = new Vector3f(((Fpu)n.X * inverse).F, ((Fpu)n.Y * inverse).F, ((Fpu)n.Z * inverse).F);
                hit.Normal = n;
            }

            sumX += n.X;
            sumY += n.Y;
            sumZ = (Fpu)n.Z + sumZ;
        }

        averageNormal = new Vector3f(sumX.F, sumY.F, sumZ.F);
        if (hits != 0)
        {
            Fpu inverse = (Fpu)1.0 / (double)hits;
            averageNormal = new Vector3f((inverse * sumX).F, (inverse * sumY).F, (inverse * sumZ).F);
        }

        return allowed;
    }

    /// <summary>
    /// Where a point of the body's frame is carried by a turn about its up axis: turned, taken into the world's frame and added
    /// to the centre, in the client's grouping (at its precision a value kept as a float is the value kept in a register).
    /// </summary>
    private static Vector3d Carried(Matrix3f o, Vector3d centre, Vector3f point, Fpu sin, Fpu cos)
    {
        Fpu flat = (Fpu)point.Z * 0.0;
        Fpu x = (((-sin) * point.Y) + (cos * point.X)) + flat;
        Fpu y = ((cos * point.Y) + (sin * point.X)) + flat;
        Fpu up = (((Fpu)point.X + point.Y) * 0.0) + point.Z;
        Fpu wx = ((up * o.M13) + (y * o.M12)) + (x * o.M11);
        Fpu wy = ((x * o.M21) + (up * o.M23)) + (y * o.M22);
        Fpu wz = ((x * o.M31) + (up * o.M33)) + (y * o.M32);
        return new Vector3d((wx + centre.X).Value, (wy + centre.Y).Value, (wz + centre.Z).Value);
    }
}
