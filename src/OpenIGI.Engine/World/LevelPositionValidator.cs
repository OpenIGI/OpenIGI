using OpenIGI.Core.Numerics;

namespace OpenIGI.Engine.World;

/// <summary>General position and orientation comparisons used by the retail level verifier.</summary>
public static class LevelPositionValidator
{
    /// <summary>Retail position tolerance, in engine units.</summary>
    public const double PositionToleranceUnits = 0.75;

    /// <summary>Retail orientation tolerance, in matrix components.</summary>
    public const float OrientationTolerance = 0.05f;

    /// <summary>Engine units represented by one runtime metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Compares authored engine units with a runtime position stored in metres.</summary>
    public static bool PosMatch(Vector3d authoredUnits, Vector3d runtimeMetres) =>
        Math.Abs(authoredUnits.X - (runtimeMetres.X * UnitsPerMetre)) < PositionToleranceUnits
        && Math.Abs(authoredUnits.Y - (runtimeMetres.Y * UnitsPerMetre)) < PositionToleranceUnits
        && Math.Abs(authoredUnits.Z - (runtimeMetres.Z * UnitsPerMetre)) < PositionToleranceUnits;

    /// <summary>Compares two orientations using the strict retail epsilon.</summary>
    public static bool OriMatch(Matrix3f authored, Matrix3f runtime) =>
        MaxDifference(authored, runtime) < OrientationTolerance;

    private static float MaxDifference(Matrix3f left, Matrix3f right)
    {
        float maximum = 0f;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                maximum = Math.Max(maximum, Math.Abs(left[row, column] - right[row, column]));
            }
        }

        return maximum;
    }
}
