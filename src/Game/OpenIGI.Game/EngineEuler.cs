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
}
