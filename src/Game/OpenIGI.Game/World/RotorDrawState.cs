using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The exact repeated model transforms used to render one retail Rotor primitive.</summary>
public readonly record struct RotorDrawState(
    Vector3d Position,
    Matrix3f BaseOrientation,
    int SampleCount,
    float FirstAngle,
    float SampleStep,
    float Tilt)
{
    /// <summary>Returns one blade sample as base · phase-X · aerodynamic-tilt-Z.</summary>
    public Matrix3f GetOrientation(int sample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sample);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sample, SampleCount);

        return BaseOrientation
            * Matrix3f.RotationX(FirstAngle + (sample * SampleStep))
            * Matrix3f.RotationZ(Tilt);
    }
}
