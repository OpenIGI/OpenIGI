using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Panels;

/// <summary>A ready-made camera move: the shots a cinematographer would set for it, each a place and what it looks at.</summary>
internal enum CameraMove
{
    /// <summary>Round the subject, a quarter turn a shot.</summary>
    Orbit,

    /// <summary>From well back straight in towards the subject.</summary>
    PushIn,

    /// <summary>Comes in high and settles low in front of the subject.</summary>
    FlyOver,

    /// <summary>Rises from the subject's height to look down on it.</summary>
    CraneUp,

    /// <summary>Stands still and turns across the subject from one side to the other.</summary>
    Pan,
}

/// <summary>One shot of a move: where the camera stands, its turn as the game stores it, and how long it holds.</summary>
/// <param name="PositionUnits">Where, in world units.</param>
/// <param name="Angles">Its alpha, beta and gamma, in radians.</param>
/// <param name="Seconds">How long until the next.</param>
internal readonly record struct MoveShot(Vector3d PositionUnits, Vector3f Angles, double Seconds);

/// <summary>Lays a move's shots round a subject, each turned to look at it as the game turns a camera to a target.</summary>
internal static class CameraMoves
{
    /// <summary>A move's shots.</summary>
    /// <param name="move">The move.</param>
    /// <param name="subjectMetres">What it looks at, in world metres.</param>
    /// <param name="fromMetres">Where the camera stands now: the side the move starts from.</param>
    /// <param name="seconds">How long the whole move takes.</param>
    /// <returns>The shots, in order; the last holds for nought, so the move ends on it.</returns>
    public static IReadOnlyList<MoveShot> Shots(CameraMove move, Vector3d subjectMetres, Vector3d fromMetres, double seconds)
    {
        Vector3d away = fromMetres - subjectMetres;
        double distance = Math.Clamp(Math.Sqrt((away.X * away.X) + (away.Y * away.Y)), 8, 120);
        double heading = Math.Atan2(away.Y, away.X);
        Vector3d Around(double angle, double reach, double height) =>
            subjectMetres + new Vector3d(Math.Cos(angle) * reach, Math.Sin(angle) * reach, height);

        Vector3d[] places = move switch
        {
            CameraMove.Orbit => [.. Enumerable.Range(0, 5).Select(i => Around(heading + (i * Math.PI / 2), distance, distance * 0.35))],
            CameraMove.PushIn => [Around(heading, distance * 1.6, distance * 0.4), Around(heading, distance * 0.45, distance * 0.15)],
            CameraMove.FlyOver => [Around(heading, distance * 2.5, distance * 1.4), Around(heading + 0.6, distance * 1.2, distance * 0.6), Around(heading + 1.0, distance * 0.6, distance * 0.15)],
            CameraMove.CraneUp => [Around(heading, distance, 1.8), Around(heading, distance * 0.8, distance * 1.2)],
            _ => [Around(heading, distance, distance * 0.25), Around(heading, distance, distance * 0.25)],
        };

        List<MoveShot> shots = [];
        for (int i = 0; i < places.Length; i++)
        {
            // A pan stands still and turns: from a third of the way left of the subject to as far right.
            Vector3d look = move == CameraMove.Pan
                ? subjectMetres + (new Vector3d(-Math.Sin(heading), Math.Cos(heading), 0) * (distance * (i == 0 ? 0.6 : -0.6)))
                : subjectMetres;
            Vector3d at = places[i] * WorldUnits.PerMetre;
            Matrix3f turn = CutsceneCameraMath.LookAt(at, look * WorldUnits.PerMetre);
            double hold = i == places.Length - 1 ? 0 : seconds / (places.Length - 1);
            shots.Add(new MoveShot(at, turn.ToEngineEulerAngles(), hold));
        }

        return shots;
    }
}
