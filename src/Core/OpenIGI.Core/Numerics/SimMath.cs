namespace OpenIGI.Core.Numerics;

/// <summary>
/// The simulation's transcendental functions in double precision: the same bits on every machine the game runs on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Math.Sin"/> and its kin call the platform's own C library, and Windows', glibc's, Apple's and the browser's
/// differ in the last bit for some arguments. A level that is run again from the same inputs -- a replay, the server checking
/// one -- would then drift apart on another machine. These are written with nothing but IEEE 754 additions, multiplications,
/// divisions, square roots and bit moves, each of which every platform rounds the same, so they agree everywhere.
/// </para>
/// <para>
/// They follow the freely licensed FreeBSD/musl routines (originally Sun's fdlibm): argument reduction, then a minimax
/// polynomial, each under one ulp of the true value. <see cref="Pow"/> is the one written here: integer exponents by repeated
/// squaring, others as an exponential of a split product, a few ulps at worst.
/// </para>
/// <para>
/// The simulation's assemblies call these instead of <see cref="Math"/>'s; an architecture test holds them to it. Square roots,
/// absolute values, floors and the like stay on <see cref="Math"/>: they are exact on every platform already.
/// </para>
/// </remarks>
public static class SimMath
{
    private const double Pio2Hi = 1.57079632679489655800e+00;
    private const double Pio2Lo = 6.12323399573676603587e-17;
    private const double Pi = 3.1415926535897931160E+00;
    private const double PiLo = 1.2246467991473531772E-16;

    // Argument reduction by pi/2 (__rem_pio2): pi/2 in three pieces, each with a tail.
    private const double ToInt = 6755399441055744.0;
    private const double InvPio2 = 6.36619772367581382433e-01;
    private const double Pio2Part1 = 1.57079632673412561417e+00;
    private const double Pio2Part1Tail = 6.07710050650619224932e-11;
    private const double Pio2Part2 = 6.07710050630396597660e-11;
    private const double Pio2Part2Tail = 2.02226624879595063154e-21;
    private const double Pio2Part3 = 2.02226624871116645580e-21;
    private const double Pio2Part3Tail = 8.47842766036889956997e-32;

    // The sine kernel's coefficients (__sin).
    private const double S1 = -1.66666666666666324348e-01;
    private const double S2 = 8.33333333332248946124e-03;
    private const double S3 = -1.98412698298579493134e-04;
    private const double S4 = 2.75573137070700676789e-06;
    private const double S5 = -2.50507602534068634195e-08;
    private const double S6 = 1.58969099521155010221e-10;

    // The cosine kernel's (__cos).
    private const double C1 = 4.16666666666666019037e-02;
    private const double C2 = -1.38888888888741095749e-03;
    private const double C3 = 2.48015872894767294178e-05;
    private const double C4 = -2.75573143513906633035e-07;
    private const double C5 = 2.08757232129817482790e-09;
    private const double C6 = -1.13596475577881948265e-11;

    // The tangent kernel's (__tan), and pi/4 with its tail.
    private const double Pio4 = 7.85398163397448278999e-01;
    private const double Pio4Lo = 3.06161699786838301793e-17;
    private static readonly double[] T =
    [
        3.33333333333334091986e-01, 1.33333333333201242699e-01, 5.39682539762260521377e-02, 2.18694882948595424599e-02,
        8.86323982359930005737e-03, 3.59207910759131235356e-03, 1.45620945432529025516e-03, 5.88041240820264096874e-04,
        2.46463134818469906812e-04, 7.81794442939557092300e-05, 7.14072491382608190305e-05, -1.85586374855275456654e-05,
        2.59073051863633712884e-05,
    ];

    // The arc tangent's breakpoints, their values in two parts, and its polynomial.
    private static readonly double[] AtanHi = [4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00];
    private static readonly double[] AtanLo = [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17];
    private static readonly double[] AtanT =
    [
        3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01, -1.11111104054623557880e-01,
        9.09088713343650656196e-02, -7.69187620504482999495e-02, 6.66107313738753120669e-02, -5.83357013379057348645e-02,
        4.97687799461593236017e-02, -3.65315727442169155270e-02, 1.62858201153657823623e-02,
    ];

    // The arc sine's rational approximation.
    private const double PS0 = 1.66666666666666657415e-01;
    private const double PS1 = -3.25565818622400915405e-01;
    private const double PS2 = 2.01212532134862925881e-01;
    private const double PS3 = -4.00555345006794114027e-02;
    private const double PS4 = 7.91534994289814532176e-04;
    private const double PS5 = 3.47933107596021167570e-05;
    private const double QS1 = -2.40339491173441421878e+00;
    private const double QS2 = 2.02094576023350569471e+00;
    private const double QS3 = -6.88283971605453293030e-01;
    private const double QS4 = 7.70381505559019352791e-02;

    // The exponential's and the logarithm's: ln 2 in two parts, and their polynomials.
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double InvLn2 = 1.44269504088896338700e+00;
    private const double P1 = 1.66666666666666019037e-01;
    private const double P2 = -2.77777777770155933842e-03;
    private const double P3 = 6.61375632143793436117e-05;
    private const double P4 = -1.65339022054652515390e-06;
    private const double P5 = 4.13813679705723846039e-08;
    private const double Lg1 = 6.666666666666735130e-01;
    private const double Lg2 = 3.999999999940941908e-01;
    private const double Lg3 = 2.857142874366239149e-01;
    private const double Lg4 = 2.222219843214978396e-01;
    private const double Lg5 = 1.818357216161805012e-01;
    private const double Lg6 = 1.531383769920937332e-01;
    private const double Lg7 = 1.479819860511658591e-01;

    // The largest whole exponent Pow takes by multiplying: x * x and 1 / x are correctly rounded, 1 / (x * x) within an ulp and
    // a half; beyond, the products' roundings add up and the logarithm's route is the closer.
    private const double SquaringLimit = 2;

    // 2 pi as the nearest double: what an argument beyond the reduction's reach is first taken a remainder of.
    private const double TwoPi = 6.283185307179586;

    /// <summary>The sine of an angle.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its sine.</returns>
    public static double Sin(double x)
    {
        uint ix = High(x) & 0x7fffffffu;
        if (ix <= 0x3fe921fbu)
        {
            // |x| < 2^-26: the sine is the angle itself.
            return ix < 0x3e500000u ? x : SinKernel(x, 0.0, false);
        }

        if (ix >= 0x7ff00000u)
        {
            return x - x;
        }

        int n = ReducePio2(x, out double y0, out double y1);
        return (n & 3) switch
        {
            0 => SinKernel(y0, y1, true),
            1 => CosKernel(y0, y1),
            2 => -SinKernel(y0, y1, true),
            _ => -CosKernel(y0, y1),
        };
    }

    /// <summary>The cosine of an angle.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its cosine.</returns>
    public static double Cos(double x)
    {
        uint ix = High(x) & 0x7fffffffu;
        if (ix <= 0x3fe921fbu)
        {
            // |x| < 2^-27 * sqrt(2): one.
            return ix < 0x3e46a09eu ? 1.0 : CosKernel(x, 0.0);
        }

        if (ix >= 0x7ff00000u)
        {
            return x - x;
        }

        int n = ReducePio2(x, out double y0, out double y1);
        return (n & 3) switch
        {
            0 => CosKernel(y0, y1),
            1 => -SinKernel(y0, y1, true),
            2 => -CosKernel(y0, y1),
            _ => SinKernel(y0, y1, true),
        };
    }

    /// <summary>The sine and the cosine of an angle together.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its sine and cosine, as <see cref="Sin"/> and <see cref="Cos"/> give them.</returns>
    public static (double Sin, double Cos) SinCos(double x) => (Sin(x), Cos(x));

    /// <summary>The tangent of an angle.</summary>
    /// <param name="x">The angle, radians.</param>
    /// <returns>Its tangent.</returns>
    public static double Tan(double x)
    {
        uint ix = High(x) & 0x7fffffffu;
        if (ix <= 0x3fe921fbu)
        {
            return ix < 0x3e400000u ? x : TanKernel(x, 0.0, false);
        }

        if (ix >= 0x7ff00000u)
        {
            return x - x;
        }

        int n = ReducePio2(x, out double y0, out double y1);
        return TanKernel(y0, y1, (n & 1) != 0);
    }

    /// <summary>The arc sine.</summary>
    /// <param name="x">A value from minus one to one.</param>
    /// <returns>The angle, radians, from -pi/2 to pi/2; NaN outside the range.</returns>
    public static double Asin(double x)
    {
        uint hx = High(x);
        uint ix = hx & 0x7fffffffu;
        if (ix >= 0x3ff00000u)
        {
            // |x| = 1 is +-pi/2; beyond it, or NaN, there is no angle.
            return ((ix - 0x3ff00000u) | Low(x)) == 0 ? x * Pio2Hi : double.NaN;
        }

        if (ix < 0x3fe00000u)
        {
            return ix < 0x3e500000u && ix >= 0x00100000u ? x : x + (x * AsinRational(x * x));
        }

        double z = (1 - Math.Abs(x)) * 0.5;
        double s = Math.Sqrt(z);
        double r = AsinRational(z);
        double angle;
        if (ix >= 0x3fef3333u)
        {
            angle = Pio2Hi - ((2 * (s + (s * r))) - Pio2Lo);
        }
        else
        {
            // f + c = sqrt(z).
            double f = WithLowCleared(s);
            double c = (z - (f * f)) / (s + f);
            angle = (0.5 * Pio2Hi) - ((2 * s * r) - (Pio2Lo - (2 * c)) - ((0.5 * Pio2Hi) - (2 * f)));
        }

        return (hx >> 31) != 0 ? -angle : angle;
    }

    /// <summary>The arc cosine.</summary>
    /// <param name="x">A value from minus one to one.</param>
    /// <returns>The angle, radians, from nought to pi; NaN outside the range.</returns>
    public static double Acos(double x)
    {
        uint hx = High(x);
        uint ix = hx & 0x7fffffffu;
        if (ix >= 0x3ff00000u)
        {
            if (((ix - 0x3ff00000u) | Low(x)) == 0)
            {
                return (hx >> 31) != 0 ? 2 * Pio2Hi : 0;
            }

            return double.NaN;
        }

        if (ix < 0x3fe00000u)
        {
            return ix <= 0x3c600000u ? Pio2Hi : Pio2Hi - (x - (Pio2Lo - (x * AsinRational(x * x))));
        }

        if ((hx >> 31) != 0)
        {
            double zn = (1.0 + x) * 0.5;
            double sn = Math.Sqrt(zn);
            double wn = (AsinRational(zn) * sn) - Pio2Lo;
            return 2 * (Pio2Hi - (sn + wn));
        }

        double z = (1.0 - x) * 0.5;
        double s = Math.Sqrt(z);
        double df = WithLowCleared(s);
        double c = (z - (df * df)) / (s + df);
        double w = (AsinRational(z) * s) + c;
        return 2 * (df + w);
    }

    /// <summary>The arc tangent.</summary>
    /// <param name="x">The tangent.</param>
    /// <returns>The angle, radians, from -pi/2 to pi/2.</returns>
    public static double Atan(double x)
    {
        uint hx = High(x);
        bool negative = (hx >> 31) != 0;
        uint ix = hx & 0x7fffffffu;
        int id;
        if (ix >= 0x44100000u)
        {
            // |x| >= 2^66: +-pi/2, or NaN.
            return double.IsNaN(x) ? x : negative ? -AtanHi[3] : AtanHi[3];
        }

        if (ix < 0x3fdc0000u)
        {
            if (ix < 0x3e400000u)
            {
                return x;
            }

            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3ff30000u)
            {
                if (ix < 0x3fe60000u)
                {
                    id = 0;
                    x = ((2.0 * x) - 1.0) / (2.0 + x);
                }
                else
                {
                    id = 1;
                    x = (x - 1.0) / (x + 1.0);
                }
            }
            else if (ix < 0x40038000u)
            {
                id = 2;
                x = (x - 1.5) / (1.0 + (1.5 * x));
            }
            else
            {
                id = 3;
                x = -1.0 / x;
            }
        }

        double z = x * x;
        double w = z * z;
        double s1 = z * (AtanT[0] + (w * (AtanT[2] + (w * (AtanT[4] + (w * (AtanT[6] + (w * (AtanT[8] + (w * AtanT[10]))))))))));
        double s2 = w * (AtanT[1] + (w * (AtanT[3] + (w * (AtanT[5] + (w * (AtanT[7] + (w * AtanT[9]))))))));
        if (id < 0)
        {
            return x - (x * (s1 + s2));
        }

        double angle = AtanHi[id] - ((x * (s1 + s2)) - AtanLo[id] - x);
        return negative ? -angle : angle;
    }

    /// <summary>The angle of a point from the positive x axis.</summary>
    /// <param name="y">The point's y.</param>
    /// <param name="x">The point's x.</param>
    /// <returns>The angle, radians, from -pi to pi.</returns>
    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return x + y;
        }

        uint ix = High(x);
        uint lx = Low(x);
        uint iy = High(y);
        uint ly = Low(y);
        if (((ix - 0x3ff00000u) | lx) == 0)
        {
            return Atan(y);
        }

        // Twice the sign of x, and the sign of y.
        uint m = ((iy >> 31) & 1) | ((ix >> 30) & 2);
        ix &= 0x7fffffffu;
        iy &= 0x7fffffffu;

        if ((iy | ly) == 0)
        {
            return m switch
            {
                0 or 1 => y,
                2 => Pi,
                _ => -Pi,
            };
        }

        if ((ix | lx) == 0)
        {
            return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
        }

        if (ix == 0x7ff00000u)
        {
            if (iy == 0x7ff00000u)
            {
                return m switch
                {
                    0 => Pi / 4,
                    1 => -Pi / 4,
                    2 => 3 * Pi / 4,
                    _ => -3 * Pi / 4,
                };
            }

            return m switch
            {
                0 => 0.0,
                1 => -0.0,
                2 => Pi,
                _ => -Pi,
            };
        }

        // |y/x| > 2^64.
        if (ix + (64u << 20) < iy || iy == 0x7ff00000u)
        {
            return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
        }

        // |y/x| < 2^-64 with x negative: nought, without a spurious underflow.
        double z = (m & 2) != 0 && iy + (64u << 20) < ix ? 0 : Atan(Math.Abs(y / x));
        return m switch
        {
            0 => z,
            1 => -z,
            2 => Pi - (z - PiLo),
            _ => (z - PiLo) - Pi,
        };
    }

    /// <summary>e raised to a power.</summary>
    /// <param name="x">The power.</param>
    /// <returns>e to it.</returns>
    public static double Exp(double x)
    {
        uint hx = High(x);
        int sign = (int)(hx >> 31);
        hx &= 0x7fffffffu;

        if (hx >= 0x4086232bu)
        {
            // |x| >= 708.39, or NaN.
            if (double.IsNaN(x))
            {
                return x;
            }

            if (x > 709.782712893383973096)
            {
                return double.PositiveInfinity;
            }

            if (x < -745.13321910194110842)
            {
                return 0;
            }
        }

        double hi;
        double lo;
        int k;
        if (hx > 0x3fd62e42u)
        {
            // |x| > ln2 / 2.
            k = hx >= 0x3ff0a2b2u ? (int)((InvLn2 * x) + (sign == 0 ? 0.5 : -0.5)) : 1 - sign - sign;
            hi = x - (k * Ln2Hi);
            lo = k * Ln2Lo;
            x = hi - lo;
        }
        else if (hx > 0x3e300000u)
        {
            k = 0;
            hi = x;
            lo = 0;
        }
        else
        {
            return 1 + x;
        }

        double xx = x * x;
        double c = x - (xx * (P1 + (xx * (P2 + (xx * (P3 + (xx * (P4 + (xx * P5)))))))));
        double y = 1 + ((x * c / (2 - c)) - lo + hi);
        return k == 0 ? y : ScaleByPowerOfTwo(y, k);
    }

    /// <summary>The natural logarithm.</summary>
    /// <param name="x">A positive value.</param>
    /// <returns>Its logarithm; minus infinity for nought, NaN for a negative value.</returns>
    public static double Log(double x)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(x);
        uint hx = (uint)(bits >> 32);
        int k = 0;
        if (hx < 0x00100000u || (hx >> 31) != 0)
        {
            if (bits << 1 == 0)
            {
                return double.NegativeInfinity;
            }

            if ((hx >> 31) != 0)
            {
                return double.NaN;
            }

            // Subnormal: scaled up into the normal range.
            k -= 54;
            x *= 18014398509481984.0;
            bits = (ulong)BitConverter.DoubleToInt64Bits(x);
            hx = (uint)(bits >> 32);
        }
        else if (hx >= 0x7ff00000u)
        {
            return x;
        }
        else if (hx == 0x3ff00000u && bits << 32 == 0)
        {
            return 0;
        }

        // x reduced into [sqrt(2)/2, sqrt(2)].
        hx += 0x3ff00000u - 0x3fe6a09eu;
        k += (int)(hx >> 20) - 0x3ff;
        hx = (hx & 0x000fffffu) + 0x3fe6a09eu;
        bits = ((ulong)hx << 32) | (bits & 0xffffffffu);
        x = BitConverter.Int64BitsToDouble((long)bits);

        double f = x - 1.0;
        double hfsq = 0.5 * f * f;
        double s = f / (2.0 + f);
        double z = s * s;
        double w = z * z;
        double t1 = w * (Lg2 + (w * (Lg4 + (w * Lg6))));
        double t2 = z * (Lg1 + (w * (Lg3 + (w * (Lg5 + (w * Lg7))))));
        double r = t2 + t1;
        double dk = k;
        return (s * (hfsq + r)) + (dk * Ln2Lo) - hfsq + f + (dk * Ln2Hi);
    }

    /// <summary>A value raised to a power.</summary>
    /// <param name="x">The base.</param>
    /// <param name="y">The power.</param>
    /// <returns>x to the y, by the special cases <see cref="Math.Pow"/> keeps.</returns>
    public static double Pow(double x, double y)
    {
        if (y == 0 || x == 1)
        {
            return 1;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        bool whole = Math.Floor(y) == y;
        if (whole && Math.Abs(y) <= SquaringLimit)
        {
            return PowerBySquaring(x, (int)y);
        }

        if (double.IsInfinity(y))
        {
            double magnitude = Math.Abs(x);
            if (magnitude == 1)
            {
                return 1;
            }

            return (magnitude > 1) == (y > 0) ? double.PositiveInfinity : 0;
        }

        bool odd = whole && Math.Abs(y) < 9007199254740992.0 && Math.Abs(y % 2) == 1;
        if (x == 0 || double.IsInfinity(x))
        {
            bool large = double.IsInfinity(x);
            double result = (y > 0) == large ? double.PositiveInfinity : 0;
            return odd && double.IsNegative(x) ? -result : result;
        }

        if (x < 0 && !whole)
        {
            return double.NaN;
        }

        double power = ExpOfProduct(y, Math.Abs(x));
        return x < 0 && odd ? -power : power;
    }

    // e^(y * log x) for a positive finite x: the logarithm and the product each kept in two parts, so neither's rounding is
    // magnified by the exponential.
    private static double ExpOfProduct(double y, double x)
    {
        double logHi = LogInTwoParts(x, out double logLo);
        double hi = y * logHi;
        if (double.IsInfinity(hi) || double.IsNaN(hi))
        {
            return Exp(hi);
        }

        double lo = Math.FusedMultiplyAdd(y, logHi, -hi) + (y * logLo);
        double e = Exp(hi);
        return double.IsInfinity(e) || e == 0 ? e : e + (e * lo);
    }

    // The natural logarithm of a positive finite x as hi + lo, good to well past a double (Log's terms summed without losing
    // their roundings).
    private static double LogInTwoParts(double x, out double lo)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(x);
        uint hx = (uint)(bits >> 32);
        int k = 0;
        if (hx < 0x00100000u)
        {
            k -= 54;
            x *= 18014398509481984.0;
            bits = (ulong)BitConverter.DoubleToInt64Bits(x);
            hx = (uint)(bits >> 32);
        }

        hx += 0x3ff00000u - 0x3fe6a09eu;
        k += (int)(hx >> 20) - 0x3ff;
        hx = (hx & 0x000fffffu) + 0x3fe6a09eu;
        x = BitConverter.Int64BitsToDouble((long)(((ulong)hx << 32) | (bits & 0xffffffffu)));

        // x in [sqrt(2)/2, sqrt(2)], so f = x - 1 is exact.
        double f = x - 1.0;
        double halfF = 0.5 * f;
        double hfsq = halfF * f;
        double hfsqLo = Math.FusedMultiplyAdd(halfF, f, -hfsq);
        double s = f / (2.0 + f);
        double z = s * s;
        double w = z * z;
        double t1 = w * (Lg2 + (w * (Lg4 + (w * Lg6))));
        double t2 = z * (Lg1 + (w * (Lg3 + (w * (Lg5 + (w * Lg7))))));
        double small = s * (hfsq + t2 + t1);
        double dk = k;

        // k ln2hi is exact (ln2hi ends in zeroes); f exact; -hfsq with its rounding; the rest are small beside them.
        double hi = TwoSum(dk * Ln2Hi, f, out double error);
        hi = TwoSum(hi, -hfsq, out double error2);
        lo = error + error2 - hfsqLo + small + (dk * Ln2Lo);
        double total = hi + lo;
        lo -= total - hi;
        return total;
    }

    // a + b, and what rounding it lost.
    private static double TwoSum(double a, double b, out double error)
    {
        double sum = a + b;
        double bVirtual = sum - a;
        error = (a - (sum - bVirtual)) + (b - bVirtual);
        return sum;
    }

    // x^n for a whole n of at most SquaringLimit, by repeated squaring.
    private static double PowerBySquaring(double x, int n)
    {
        bool invert = n < 0;
        uint e = (uint)Math.Abs(n);
        double result = 1;
        double square = x;
        while (e != 0)
        {
            if ((e & 1) != 0)
            {
                result *= square;
            }

            e >>= 1;
            if (e != 0)
            {
                square *= square;
            }
        }

        return invert ? 1 / result : result;
    }

    // x * 2^n, exactly where the result is representable (scalbn).
    private static double ScaleByPowerOfTwo(double x, int n)
    {
        double up = BitConverter.Int64BitsToDouble(0x7fe0000000000000);      // 2^1023
        double down = BitConverter.Int64BitsToDouble(0x0360000000000000);    // 2^-1022 * 2^53
        if (n > 1023)
        {
            x *= up;
            n -= 1023;
            if (n > 1023)
            {
                x *= up;
                n -= 1023;
                n = Math.Min(n, 1023);
            }
        }
        else if (n < -1022)
        {
            x *= down;
            n += 1022 - 53;
            if (n < -1022)
            {
                x *= down;
                n += 1022 - 53;
                n = Math.Max(n, -1022);
            }
        }

        return x * BitConverter.Int64BitsToDouble((long)(0x3ff + n) << 52);
    }

    // The sine near nought (|x| <= pi/4), of x + y where y is x's tail when hasTail.
    private static double SinKernel(double x, double y, bool hasTail)
    {
        double z = x * x;
        double w = z * z;
        double r = S2 + (z * (S3 + (z * S4))) + (z * w * (S5 + (z * S6)));
        double v = z * x;
        return hasTail
            ? x - (((z * ((0.5 * y) - (v * r))) - y) - (v * S1))
            : x + (v * (S1 + (z * r)));
    }

    // The cosine near nought, of x + y.
    private static double CosKernel(double x, double y)
    {
        double z = x * x;
        double w = z * z;
        double r = (z * (C1 + (z * (C2 + (z * C3))))) + (w * w * (C4 + (z * (C5 + (z * C6)))));
        double hz = 0.5 * z;
        w = 1.0 - hz;
        return w + (((1.0 - w) - hz) + ((z * r) - (x * y)));
    }

    // The tangent near nought, of x + y; minus its reciprocal when odd.
    private static double TanKernel(double x, double y, bool odd)
    {
        uint hx = High(x);
        bool big = (hx & 0x7fffffffu) >= 0x3fe59428u;
        bool negative = false;
        if (big)
        {
            negative = (hx >> 31) != 0;
            if (negative)
            {
                x = -x;
                y = -y;
            }

            x = (Pio4 - x) + (Pio4Lo - y);
            y = 0.0;
        }

        double z = x * x;
        double w = z * z;
        double r = T[1] + (w * (T[3] + (w * (T[5] + (w * (T[7] + (w * (T[9] + (w * T[11])))))))));
        double v = z * (T[2] + (w * (T[4] + (w * (T[6] + (w * (T[8] + (w * (T[10] + (w * T[12]))))))))));
        double s = z * x;
        r = y + (z * ((s * (r + v)) + y)) + (s * T[0]);
        w = x + r;
        if (big)
        {
            double sign = odd ? -1 : 1;
            double t = sign - (2.0 * (x + (r - (w * w / (w + sign)))));
            return negative ? -t : t;
        }

        if (!odd)
        {
            return w;
        }

        // -1 / (x + r), worked accurately.
        double w0 = WithLowCleared(w);
        double tail = r - (w0 - x);
        double a = -1.0 / w;
        double a0 = WithLowCleared(a);
        return a0 + (a * (1.0 + (a0 * w0) + (a0 * tail)));
    }

    // The arc sine's rational part, R(z).
    private static double AsinRational(double z)
    {
        double p = z * (PS0 + (z * (PS1 + (z * (PS2 + (z * (PS3 + (z * (PS4 + (z * PS5))))))))));
        double q = 1.0 + (z * (QS1 + (z * (QS2 + (z * (QS3 + (z * QS4)))))));
        return p / q;
    }

    // x less the nearest whole multiple n of pi/2, as y0 + y1; returns n (__rem_pio2's medium case). Beyond 2^20 pi/2 -- where
    // the simulation never goes -- the argument is first taken exactly modulo the double nearest 2 pi: off from the true
    // remainder by a whole number of that double's error, so not accurate there, but finite and the same on every machine.
    private static int ReducePio2(double x, out double y0, out double y1)
    {
        uint ix = High(x) & 0x7fffffffu;
        if (ix >= 0x413921fbu)
        {
            x %= TwoPi;
            ix = High(x) & 0x7fffffffu;
        }

        // The nearest whole multiple, by the 1.5 * 2^52 trick.
        double fn = (x * InvPio2) + ToInt - ToInt;
        double r = x - (fn * Pio2Part1);
        double w = fn * Pio2Part1Tail;
        y0 = r - w;
        int ex = (int)(ix >> 20);
        int ey = (int)((High(y0) >> 20) & 0x7ff);
        if (ex - ey > 16)
        {
            double t = r;
            w = fn * Pio2Part2;
            r = t - w;
            w = (fn * Pio2Part2Tail) - ((t - r) - w);
            y0 = r - w;
            ey = (int)((High(y0) >> 20) & 0x7ff);
            if (ex - ey > 49)
            {
                t = r;
                w = fn * Pio2Part3;
                r = t - w;
                w = (fn * Pio2Part3Tail) - ((t - r) - w);
                y0 = r - w;
            }
        }

        y1 = (r - y0) - w;
        return (int)(fn % 4.0);
    }

    private static uint High(double x) => (uint)((ulong)BitConverter.DoubleToInt64Bits(x) >> 32);

    private static uint Low(double x) => (uint)BitConverter.DoubleToInt64Bits(x);

    private static double WithLowCleared(double x) =>
        BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) & unchecked((long)0xffffffff00000000UL));
}
