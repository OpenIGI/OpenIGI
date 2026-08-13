using OpenIGI.Core.Numerics;

namespace OpenIGI.Engine.World;

/// <summary>General position and orientation comparisons used by the retail level verifier.</summary>
public static class LevelPositionValidator
{
    /// <summary>The default retail position tolerance, in engine units.</summary>
    /// <remarks>The reference verifier uses an exact comparison unless a caller supplies a tolerance.</remarks>
    public const long PositionToleranceUnits = 0;

    /// <summary>Retail orientation tolerance, in matrix components.</summary>
    public const float OrientationTolerance = 0.05f;

    /// <summary>Engine units represented by one runtime metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Compares authored engine units with a runtime position stored in metres.</summary>
    public static bool PosMatch(
        Vector3d authoredUnits,
        Vector3d runtimeMetres,
        long toleranceUnits = PositionToleranceUnits) =>
        toleranceUnits >= 0
        && Math.Abs(authoredUnits.X - (runtimeMetres.X * UnitsPerMetre)) <= toleranceUnits
        && Math.Abs(authoredUnits.Y - (runtimeMetres.Y * UnitsPerMetre)) <= toleranceUnits
        && Math.Abs(authoredUnits.Z - (runtimeMetres.Z * UnitsPerMetre)) <= toleranceUnits;

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
