using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game;

/// <summary>The engine's three-angle rotation, which both titles build the same way.</summary>
/// <remarks>
/// Project I.G.I.'s <c>Matrix_SetEuler</c> (<c>0x4B38E0</c>) and I.G.I. 2's <c>Real32x9_Create</c> both make <c>Z(gamma) ·
/// Y(beta) · X(alpha)</c>; this is the sequel's arithmetic, each product truncated to 24 bits as its client does.
/// </remarks>
public static class EngineEuler
{
    /// <summary>The rotation three angles describe, <c>Real32x9_Create</c> (<c>igi2 0x562A60</c>): see <c>Igi2Pose.FromAngles</c>.</summary>
    /// <param name="alpha">The turn about X, in radians.</param>
    /// <param name="beta">The turn about Y.</param>
    /// <param name="gamma">The turn about Z.</param>
    /// <returns>The nine floats, row by row.</returns>
    public static float[] Rotation(float alpha, float beta, float gamma)
    {
        Fpu sinGamma = Fpu.Sin(gamma), cosGamma = Fpu.Cos(gamma);
        float sinBeta = Fpu.Sin(beta).F, cosBeta = Fpu.Cos(beta).F;

        // Gamma times beta.
        float p00 = (cosBeta * cosGamma).F, p01 = (-sinGamma).F, p02 = ((Fpu)sinBeta * cosGamma).F;
        float p10 = (cosBeta * sinGamma).F, p11 = cosGamma.F, p12 = ((Fpu)sinBeta * sinGamma).F;
        float p20 = -sinBeta, p22 = cosBeta;

        // Then alpha.
        Fpu sinAlpha = Fpu.Sin(alpha), cosAlpha = Fpu.Cos(alpha);
        return
        [
            p00, ((cosAlpha * p01) + (sinAlpha * p02)).F, ((cosAlpha * p02) + (-sinAlpha * p01)).F,
            p10, ((cosAlpha * p11) + (sinAlpha * p12)).F, ((cosAlpha * p12) + (-sinAlpha * p11)).F,
            p20, (sinAlpha * p22).F, (cosAlpha * p22).F,
        ];
    }

    /// <summary>
    /// The turn about the vertical an orientation stands at: the gamma of <c>Real32x9_GetAngles</c> (<c>igi2 0x562D60</c>).
    /// </summary>
    /// <param name="m0">The orientation's first element, <c>tX.vX</c>.</param>
    /// <param name="m3">Its fourth, <c>tY.vX</c>.</param>
    /// <returns>The turn, in radians from zero to a whole turn.</returns>
    /// <remarks>
    /// <c>fpatan</c> of the two, which precision control does not reach; zero when the X axis stands within a ten
    /// thousandth of vertical, and a quarter turn either way when <paramref name="m0"/> is zero. A negative turn is
    /// brought into range by a whole turn, at 24 bits.
    /// </remarks>
    public static float Gamma(float m0, float m3)
    {
        Fpu length = Fpu.Sqrt(((Fpu)m3 * m3) + ((Fpu)m0 * m0));
        if (!(length.Value > 0.0001))
        {
            return 0f;
        }

        Fpu gamma = m0 != 0f
            ? Math.Atan2(m3, m0)
            : (Fpu)(m3 < 0f ? -1.0 : 1.0) * 1.570796326794897;
        if (gamma.Value < 0.0)
        {
            gamma += 6.283185307179589;
        }

        return gamma.F;
    }

    /// <summary>
    /// The three angles an orientation stands at, <c>Real32x9_GetAngles</c> (<c>igi2 0x562D60</c>): the inverse of
    /// <see cref="Rotation"/>.
    /// </summary>
    /// <param name="m">The orientation, row by row.</param>
    /// <returns>Alpha, beta and gamma, each in radians from zero to a whole turn.</returns>
    /// <remarks>
    /// <para>
    /// With the X axis more than a ten thousandth off vertical, alpha is <c>fpatan</c> of <c>m7</c> over <c>m8</c>, beta
    /// of <c>−m6</c> over that length, and gamma as <see cref="Gamma"/> gives it; with it vertical, alpha is
    /// <c>fpatan</c> of <c>−m5</c> over <c>m4</c> and gamma is zero. A zero denominator is a quarter turn by the
    /// numerator's sign. Each negative angle is brought into range by a whole turn, at 24 bits.
    /// </para>
    /// <para>
    /// Both zero is <c>"Atan2 undefined in origo"</c> and the original stops; a matrix the game builds never has it, and
    /// this answers zero.
    /// </para>
    /// </remarks>
    public static (float Alpha, float Beta, float Gamma) Angles(IReadOnlyList<float> m)
    {
        ArgumentNullException.ThrowIfNull(m);
        Fpu length = Fpu.Sqrt(((Fpu)m[3] * m[3]) + ((Fpu)m[0] * m[0]));
        bool upright = !(length.Value > 0.0001);
        Fpu alpha = upright ? Atan2OrQuarter(-m[5], m[4]) : Atan2OrQuarter(m[7], m[8]);
        Fpu beta = Atan2OrQuarter(-m[6], length);
        return (InRange(alpha), InRange(beta), Gamma(m[0], m[3]));

        static Fpu Atan2OrQuarter(Fpu y, Fpu x) =>
            x.Value != 0.0 ? Math.Atan2(y.Value, x.Value) : y.Value == 0.0 ? 0.0 : (Fpu)(y.Value < 0.0 ? -1.0 : 1.0) * 1.570796326794897;

        static float InRange(Fpu angle) => (angle.Value < 0.0 ? angle + 6.283185307179589 : angle).F;
    }
}
