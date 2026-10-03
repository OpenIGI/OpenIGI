// The AP0/AP1 matrices and the glow and red-modifier stages in this file derive from the Academy
// Color Encoding System (ACES) reference rendering transform (RRT), used under the licence below.
//
// # License Terms for Academy Color Encoding System Components #
//
// Academy Color Encoding System (ACES) software and tools are provided by the
// Academy under the following terms and conditions: A worldwide, royalty-free,
// non-exclusive right to copy, modify, create derivatives, and use, in source and
// binary forms, is hereby granted, subject to acceptance of this license.
//
// Copyright © 2015 Academy of Motion Picture Arts and Sciences (A.M.P.A.S.).
// Portions contributed by others as indicated. All rights reserved.
//
// Performance of any of the aforementioned acts indicates acceptance to be bound
// by the following terms and conditions:
//
// * Copies of source code, in whole or in part, must retain the above copyright
// notice, this list of conditions and the Disclaimer of Warranty.
//
// * Use in binary form must retain the above copyright notice, this list of
// conditions and the Disclaimer of Warranty in the documentation and/or other
// materials provided with the distribution.
//
// * Nothing in this license shall be deemed to grant any rights to trademarks,
// copyrights, patents, trade secrets or any other intellectual property of
// A.M.P.A.S. or any contributors, except as expressly stated herein.
//
// * Neither the name "A.M.P.A.S." nor the name of any other contributors to this
// software may be used to endorse or promote products derivative of or based on
// this software without express prior written permission of A.M.P.A.S. or the
// contributors, as appropriate.
//
// This license shall be construed pursuant to the laws of the State of
// California, and any disputes related thereto shall be subject to the
// jurisdiction of the courts therein.
//
// Disclaimer of Warranty: THIS SOFTWARE IS PROVIDED BY A.M.P.A.S. AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
// THE IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND
// NON-INFRINGEMENT ARE DISCLAIMED. IN NO EVENT SHALL A.M.P.A.S., OR ANY
// CONTRIBUTORS OR DISTRIBUTORS, BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, RESITUTIONARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
// PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
// LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE
// OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
// ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// WITHOUT LIMITING THE GENERALITY OF THE FOREGOING, THE ACADEMY SPECIFICALLY
// DISCLAIMS ANY REPRESENTATIONS OR WARRANTIES WHATSOEVER RELATED TO PATENT OR
// OTHER INTELLECTUAL PROPERTY RIGHTS IN THE ACADEMY COLOR ENCODING SYSTEM, OR
// APPLICATIONS THEREOF, HELD BY PARTIES OTHER THAN A.M.P.A.S.,WHETHER DISCLOSED OR
// UNDISCLOSED.

#ifndef OPENIGI_ACES_TONE_MAPPING_INCLUDED
#define OPENIGI_ACES_TONE_MAPPING_INCLUDED

// The film curve the resolve runs for its "aces" and "agx" operators, and the display encode after it.
//
// The shape: the exposed scene colour goes into the Academy's AP1 working space, saturated colour is
// pushed a little further out, bright blues are bent toward cyan, and then the Academy's reference
// rendering transform (its glow and red modifiers, as published in the ACES RRT) runs ahead of a
// parametric filmic curve with a logistic toe and shoulder in log10 space. The blue bend is undone
// and the result comes back to linear sRGB. Every matrix is written row by row and applied as M * v,
// so row i is output channel i.

// Linear Rec.709/sRGB (D65) into AP1 (D60), through XYZ with a Bradford adaptation.
static const float3x3 kRec709ToWorking = float3x3(
    0.613097402, 0.339523146, 0.047379451,
    0.070193723, 0.916353879, 0.013452399,
    0.020615593, 0.109569773, 0.869814634);

// The inverse of the above.
static const float3x3 kWorkingToRec709 = float3x3(
     1.705050993, -0.621792120, -0.083258872,
    -0.130256417,  1.140804736, -0.010548319,
    -0.024003357, -0.128968976,  1.152972333);

// The Academy's published AP0 <-> AP1 pair.
static const float3x3 kArchiveToWorking = float3x3(
     1.451439316, -0.236510747, -0.214928569,
    -0.076553773,  1.176229700, -0.099675926,
     0.008316148, -0.006032450,  0.997716301);

static const float3x3 kWorkingToArchive = float3x3(
     0.695452241,  0.140678697,  0.163869062,
     0.044794563,  0.859671118,  0.095534318,
    -0.005525883,  0.004025210,  1.001500672);

// Luminance of an AP1 colour (the Academy's published row).
static const float3 kWorkingLuma = float3(0.2722287168, 0.6740817658, 0.0536895174);

// A tuning matrix applied in AP1 that widens saturated colour before the curve compresses it.
static const float3x3 kWidenGamut = float3x3(
     1.370412372, -0.329292188, -0.063683119,
    -0.083433492,  1.097092748, -0.010861380,
    -0.025793321, -0.098625799,  1.203694953);

// A tuning matrix applied in AP1 that bends bright saturated blue away from purple, and its inverse.
static const float3x3 kBlueBend = float3x3(
    0.938639378, 0.000000000, 0.061360622,
    0.000000000, 0.830794133, 0.169205867,
    0.000000000, 0.000000000, 1.000000000);

static const float3x3 kBlueUnbend = float3x3(
     1.065374875,  0.000001447, -0.065371005,
    -0.000000346,  1.203663524, -0.203667720,
     0.000000020,  0.000000021,  0.999999600);

// How far toward the bent colour the blue correction goes (and comes back).
static const float kBlueBendAmount = 0.6;

// How quickly the gamut widening fades in with luminance.
static const float kWidenLumaRate = 1.0;

// The filmic curve's shape.
static const float kCurveSlope = 0.88;
static const float kCurveToe = 0.55;
static const float kCurveShoulder = 0.26;
static const float kCurveBlackClip = 0.0;
static const float kCurveWhiteClip = 0.04;

// Chroma kept about the working luminance before and after the curve.
static const float kPreCurveChroma = 0.96;
static const float kPostCurveChroma = 0.93;

// log10(e), to take a base-10 logarithm from the natural one.
static const float kLog10OfE = 0.4342944819;

float FilmLog10(float x)
{
    return log(x) * kLog10OfE;
}

// The RRT's saturation measure: the channel spread relative to the brightest channel.
float FilmSaturation(float3 c)
{
    float hi = max(c.r, max(c.g, c.b));
    float lo = min(c.r, min(c.g, c.b));
    return (max(hi, 1e-10) - max(lo, 1e-10)) / max(hi, 0.01);
}

// The RRT's chroma-weighted brightness, with the radicand kept non-negative.
float FilmYc(float3 c)
{
    float radicand = (c.b * (c.b - c.g)) + (c.g * (c.g - c.r)) + (c.r * (c.r - c.b));
    float chroma = sqrt(max(radicand, 0.0));
    return (c.r + c.g + c.b + (1.75 * chroma)) / 3.0;
}

// A smooth step from 0 to 1 over [-2, 2], one half at the centre.
float FilmShaper(float x)
{
    float t = max(1.0 - abs(x * 0.5), 0.0);
    return 0.5 * (1.0 + (sign(x) * (1.0 - (t * t))));
}

float FilmGlowAmount(float yc, float gain, float mid)
{
    if (yc <= (2.0 / 3.0) * mid)
    {
        return gain;
    }

    if (yc >= 2.0 * mid)
    {
        return 0.0;
    }

    return gain * ((mid / yc) - 0.5);
}

float FilmHueDegrees(float3 c)
{
    if (c.r == c.g && c.g == c.b)
    {
        return 0.0;
    }

    float h = degrees(atan2(sqrt(3.0) * (c.g - c.b), (2.0 * c.r) - c.g - c.b));
    if (h < 0.0)
    {
        h += 360.0;
    }

    return clamp(h, 0.0, 360.0);
}

// One channel of the filmic curve, in log10 space with logistic toe and shoulder.
float FilmChannel(float v)
{
    float toeSpan = 1.0 + kCurveBlackClip - kCurveToe;
    float shoulderSpan = 1.0 + kCurveWhiteClip - kCurveShoulder;

    float toeMatch;
    if (kCurveToe > 0.8)
    {
        toeMatch = ((1.0 - kCurveToe - 0.18) / kCurveSlope) + FilmLog10(0.18);
    }
    else
    {
        float bt = ((0.18 + kCurveBlackClip) / toeSpan) - 1.0;
        toeMatch = FilmLog10(0.18) - (0.5 * log((1.0 + bt) / (1.0 - bt)) * (toeSpan / kCurveSlope));
    }

    float straightMatch = ((1.0 - kCurveToe) / kCurveSlope) - toeMatch;
    float shoulderMatch = (kCurveShoulder / kCurveSlope) - straightMatch;

    float l = FilmLog10(max(v, 1e-10));
    float straight = kCurveSlope * (l + straightMatch);

    float toe = straight;
    if (l < toeMatch)
    {
        toe = -kCurveBlackClip
            + ((2.0 * toeSpan) / (1.0 + exp(-((2.0 * kCurveSlope) / toeSpan) * (l - toeMatch))));
    }

    float shoulder = straight;
    if (l > shoulderMatch)
    {
        shoulder = (1.0 + kCurveWhiteClip)
            - ((2.0 * shoulderSpan) / (1.0 + exp(((2.0 * kCurveSlope) / shoulderSpan) * (l - shoulderMatch))));
    }

    float t = saturate((l - toeMatch) / (shoulderMatch - toeMatch));
    if (shoulderMatch < toeMatch)
    {
        t = 1.0 - t;
    }

    t = t * t * (3.0 - (2.0 * t));
    return lerp(toe, shoulder, t);
}

// The curve proper, AP1 in and AP1 out.
float3 FilmCurve(float3 working)
{
    float3 c = mul(kWorkingToArchive, working);

    // Glow: lift dim saturated colour a touch. The saturation is taken before the lift.
    float saturation = FilmSaturation(c);
    float yc = FilmYc(c);
    float shaped = FilmShaper((saturation - 0.4) / 0.2);
    float glow = FilmGlowAmount(yc, 0.05 * shaped, 0.08);
    c *= 1.0 + glow;

    // Red: pull saturated reds back toward a low pivot, weighted around a hue of zero.
    float centred = FilmHueDegrees(c);
    if (centred < -180.0)
    {
        centred += 360.0;
    }
    else if (centred > 180.0)
    {
        centred -= 360.0;
    }

    float weight = smoothstep(0.0, 1.0, 1.0 - abs((2.0 * centred) / 135.0));
    c.r += (weight * weight) * saturation * (0.03 - c.r) * (1.0 - 0.82);

    float3 w = max(mul(kArchiveToWorking, c), float3(0.0, 0.0, 0.0));
    w = lerp(dot(w, kWorkingLuma).xxx, w, kPreCurveChroma);

    float3 tone = float3(FilmChannel(w.r), FilmChannel(w.g), FilmChannel(w.b));
    tone = lerp(dot(tone, kWorkingLuma).xxx, tone, kPostCurveChroma);
    return max(tone, float3(0.0, 0.0, 0.0));
}

float3 ToneMapScene(float3 linearColour)
{
    float3 a = mul(kRec709ToWorking, linearColour);

    // Widen saturated colour, weighted by how far it is from grey and by how bright it is.
    float luma = dot(a, kWorkingLuma);
    float3 fromGrey = (a / max(luma, 1e-10)) - float3(1.0, 1.0, 1.0);
    float distanceSq = dot(fromGrey, fromGrey);
    float widen = (1.0 - exp2(-4.0 * distanceSq)) * (1.0 - exp2(-4.0 * kWidenLumaRate * luma * luma));
    a = max(lerp(a, mul(kWidenGamut, a), widen), float3(0.0, 0.0, 0.0));

    a = lerp(a, mul(kBlueBend, a), kBlueBendAmount);
    a = FilmCurve(a);
    a = lerp(a, mul(kBlueUnbend, a), kBlueBendAmount);

    return max(mul(kWorkingToRec709, a), float3(0.0, 0.0, 0.0));
}

// The sRGB transfer function (IEC 61966-2-1), per channel.
float3 LinearToSrgbDisplay(float3 c)
{
    float3 low = c * 12.92;
    float3 high = (1.055 * pow(max(c, float3(0.0, 0.0, 0.0)), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4))) - 0.055;
    return float3(
        c.r < 0.00313067 ? low.r : high.r,
        c.g < 0.00313067 ? low.g : high.g,
        c.b < 0.00313067 ? low.b : high.b);
}

#endif
