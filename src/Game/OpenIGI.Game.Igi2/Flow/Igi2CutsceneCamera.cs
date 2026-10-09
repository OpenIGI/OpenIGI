using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>Something a cutscene camera can look at: where it is and which way it faces, its <c>GetCameraTargetInfo</c> handler.</summary>
/// <remarks>
/// A rigid object's (<c>RigidDynCubeObj_GetCameraTargetInfoHandler</c>, <c>igi2 0x58DCE0</c>) and a boned one's
/// (<c>BoneDynCubeObj_GetCameraTargetInfoHandler</c>, <c>0x5676A0</c>) give their position and frame; a spline path node's
/// (<c>SplinePathNodeQTask_GetCameraTargetInfoHandler</c>, <c>0x547E30</c>) its own.
/// </remarks>
public interface IIgi2CameraTarget
{
    /// <summary>Gets where it is.</summary>
    Vector3d CameraTargetPosition { get; }

    /// <summary>Gets which way it faces, row by row.</summary>
    IReadOnlyList<float> CameraTargetOrientation { get; }
}

/// <summary>
/// What a running cutscene sets the camera to for a tick, <c>QCamera_Set</c> from <c>CutScene_InterpolateCamera</c>
/// (<c>igi2 0x438F10</c>), with what its filters would show.
/// </summary>
/// <param name="Position">Where the camera is.</param>
/// <param name="Orientation">
/// Which way it faces, row by row, in the camera's convention: its columns are right, down and forward.
/// </param>
/// <param name="FieldOfView">The tangent of half the horizontal angle it sees.</param>
/// <param name="LightTime">The time of day it is lit for, in hours.</param>
/// <param name="Speed">The step it took since the last tick.</param>
/// <param name="ViewportHeight">How much of the screen's height the picture fills, the letterbox: one is all of it.</param>
/// <param name="FilterId">The filter over the picture, <c>CAMERAFILTER_TYPE_*</c>: nought none, one lines, two and three bands, four a hand-held camera.</param>
/// <param name="FilterColor">The band filter's colour.</param>
/// <param name="FilterNoise">The filters' noise.</param>
/// <param name="IsCameraJustChanged">Whether the camera it is on changed this tick, which starts the filters afresh.</param>
public sealed record Igi2CutsceneView(
    Vector3d Position,
    IReadOnlyList<float> Orientation,
    float FieldOfView,
    float LightTime,
    Vector3f Speed,
    float ViewportHeight,
    int FilterId,
    Vector3f FilterColor,
    float FilterNoise,
    bool IsCameraJustChanged);

/// <summary>
/// The random stream a cutscene camera shakes by, <c>CutScene_tRandomState</c>: three words seeded once when the game starts
/// (<c>CutScene_Open</c>, <c>igi2 0x43ACEC</c>), and kept from level to level.
/// </summary>
public sealed class Igi2CutsceneShake
{
    private uint _a = 0xC8FB1A07;
    private uint _b = 0x39EA7756;
    private uint _c = 0x7FF13CCD;

    /// <summary>The next draw, the low half of the first word once each is stirred into the next.</summary>
    /// <returns>A value from 0 to 65535.</returns>
    public int Next()
    {
        _c ^= _a >> 7;
        _b ^= _c;
        _a += _b;
        return (int)(_a & 0xFFFF);
    }
}

/// <summary>The arithmetic of a cutscene's camera, in the FPU's chopped single precision and its instructions' order.</summary>
public static class Igi2CutsceneCameras
{
    /// <summary>The filter that draws lines over the picture, <c>CAMERAFILTER_TYPE_LINE</c>.</summary>
    public const int LineFilter = 1;

    /// <summary>The filter that draws bands, <c>CAMERAFILTER_TYPE_BAND</c>.</summary>
    public const int BandFilter = 2;

    /// <summary>The band filter that counts its cameras, <c>CAMERAFILTER_TYPE_BAND_UPDATE</c>.</summary>
    public const int BandUpdateFilter = 3;

    /// <summary>The hand-held camera filter, <c>CAMERAFILTER_TYPE_HANDICAM</c>.</summary>
    public const int HandiCamFilter = 4;

    /// <summary>The widest shake's turn, <c>0x3F08B8DD</c> (about 30.6 degrees at a shake of one).</summary>
    public const float ShakeTurn = 0.53407079f;

    /// <summary>The widest shake's step, <c>0x45800000</c>: a world metre at a shake of one.</summary>
    public const float ShakeStep = 4096f;

    // Filter_Open (igi2 0x5410A0) registers these constants.
    private static readonly Dictionary<string, int> Filters = new(StringComparer.Ordinal)
    {
        ["CAMERAFILTER_TYPE_NONE"] = 0,
        ["CAMERAFILTER_TYPE_LINE"] = LineFilter,
        ["CAMERAFILTER_TYPE_BAND"] = BandFilter,
        ["CAMERAFILTER_TYPE_BAND_UPDATE"] = BandUpdateFilter,
        ["CAMERAFILTER_TYPE_HANDICAM"] = HandiCamFilter,
    };

    /// <summary>A filter's id by the name a camera's <c>FILTER</c> parameter gives, as <c>Script_ParseLineInt32</c> reads it.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The id; nought for a name it does not know.</returns>
    public static int FilterId(string name) => Filters.TryGetValue(name?.Trim() ?? string.Empty, out int id) ? id : 0;

    /// <summary>
    /// One value of a camera between its neighbours, <c>CutScene_InterpolateScalar</c> (<c>igi2 0x438DF0</c>): a cubic from the
    /// second key to the third whose slopes are the neighbours' differences, each scaled by how long its side lasts.
    /// </summary>
    /// <param name="v0">The value at the key before.</param>
    /// <param name="v1">The value at the key it runs from.</param>
    /// <param name="v2">The value at the key it runs to.</param>
    /// <param name="v3">The value at the key after.</param>
    /// <param name="t0">The time of the key before.</param>
    /// <param name="t1">The time of the key it runs from.</param>
    /// <param name="t2">The time of the key it runs to.</param>
    /// <param name="t3">The time of the key after.</param>
    /// <param name="t">The time now.</param>
    /// <returns>The value, as the FPU holds it.</returns>
    public static Fpu InterpolateScalar(double v0, double v1, double v2, double v3, double t0, double t1, double t2, double t3, double t)
    {
        double slopeTo = (((Fpu)v3 - v1) * 0.5).Value;

        double slopeFrom = 0.0;
        Fpu fromSpan = (Fpu)t2 - t0;
        if (fromSpan.Value != 0.0)
        {
            Fpu lead = (Fpu)t1 - t0;
            slopeFrom = (((lead + lead) / fromSpan) * (((Fpu)v2 - v0) * 0.5)).Value;
        }

        Fpu slopeToScaled = 0.0;
        Fpu toSpan = (Fpu)t3 - t1;
        if (toSpan.Value != 0.0)
        {
            Fpu span = (Fpu)t2 - t1;
            slopeToScaled = ((span + span) / toSpan) * slopeTo;
        }

        double cubic = (((((Fpu)v1 + v1) - ((Fpu)v2 + v2)) + slopeToScaled) + slopeFrom).Value;
        double square = (((((Fpu)v2 * 3.0) - ((Fpu)v1 * 3.0)) - ((Fpu)slopeFrom + slopeFrom)) - slopeToScaled).Value;
        Fpu u = 0.0;
        double length = ((Fpu)t2 - t1).Value;
        if (length != 0.0)
        {
            u = ((Fpu)t - t1) / length;
        }

        Fpu u2 = u * u;
        return ((Fpu)v1) + ((((u2 * u) * cubic) + ((Fpu)square * u2)) + (u * slopeFrom));
    }

    /// <summary>
    /// The frame that looks from a point at a target, <c>EditCamera_FollowTarget</c> (<c>igi2 0x581290</c>): forward to the
    /// target, right across the world's up, and down the forward across that; columns right, down and forward.
    /// </summary>
    /// <param name="from">Where the camera is.</param>
    /// <param name="target">Where it looks.</param>
    /// <returns>The frame, row by row.</returns>
    /// <remarks>On the target itself it looks along the world's X.</remarks>
    public static float[] FollowTarget(Vector3d from, Vector3d target)
    {
        Fpu fx = (Fpu)target.X - from.X;
        float fy = ((Fpu)target.Y - from.Y).F;
        float fz = ((Fpu)target.Z - from.Z).F;
        if (fx.Value == 0.0 && fy == 0f && fz == 0f)
        {
            fx = 1f;
        }
        else
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((fx * fx) + ((Fpu)fy * fy)) + ((Fpu)fz * fz));
            fx *= inverse;
            fy = ((Fpu)fy * inverse).F;
            fz = ((Fpu)fz * inverse).F;
        }

        // Right is forward across (0, 0, 1); up is right across forward.
        Fpu along = (Fpu)fz * 0f;
        float rx = ((Fpu)fy - along).F;
        float ry = (-(fx - along)).F;
        float rz = (((Fpu)0f * fx) - ((Fpu)fy * 0f)).F;
        float ux = (((Fpu)ry * fz) - ((Fpu)rz * fy)).F;
        float uy = (-(((Fpu)rx * fz) - ((Fpu)rz * fx))).F;
        float uz = (((Fpu)rx * fy) - ((Fpu)ry * fx)).F;
        Unit(ref rx, ref ry, ref rz);
        Unit(ref ux, ref uy, ref uz);
        return [rx, -ux, fx.F, ry, -uy, fy, rz, -uz, fz];
    }

    /// <summary>
    /// The frame turned by a shake, <c>Real32x9_Multiply</c> inline in <c>CutScene_InterpolateCamera</c> (<c>igi2 0x4398C5</c>):
    /// each element summed over its three products last to first, but the middle and bottom rows' last element first, last,
    /// then the middle.
    /// </summary>
    /// <param name="o">The frame, row by row.</param>
    /// <param name="s">The shake's rotation, row by row.</param>
    /// <returns>The frame times the shake.</returns>
    public static float[] Shake(IReadOnlyList<float> o, IReadOnlyList<float> s)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(s);
        float[] m = new float[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Fpu a = (Fpu)o[(3 * row) + 2] * s[6 + column];
                Fpu b = (Fpu)o[(3 * row) + 1] * s[3 + column];
                Fpu c = (Fpu)o[3 * row] * s[column];
                m[(3 * row) + column] = row > 0 && column == 2 ? ((c + a) + b).F : ((a + b) + c).F;
            }
        }

        return m;
    }

    // Real32x9 unit inline: each part times one over the length, when any part is not nought.
    private static void Unit(ref float x, ref float y, ref float z)
    {
        if (x == 0f && y == 0f && z == 0f)
        {
            return;
        }

        Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((((Fpu)x * x) + ((Fpu)y * y)) + ((Fpu)z * z));
        x = ((Fpu)x * inverse).F;
        y = ((Fpu)y * inverse).F;
        z = ((Fpu)z * inverse).F;
    }
}
