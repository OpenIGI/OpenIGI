using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>
/// The turn of the soldier's spine, shoulders and head toward what it aims at: the bone callbacks
/// <c>HumanSoldier_UpdateInternalDataHandler</c> (<c>igi2 0x4EB0F0</c>) sets on its rig.
/// </content>
/// <remarks>
/// <para>
/// Each runs as the hierarchy reaches its bone, on the bone's orientation in the body's frame. The lower back finds the aim: the
/// target less the body and the bone's parent, turned into the body's frame (straight ahead when there is none or it is less
/// than a metre in front); its heading, held to sixty degrees, and its tilt are kept. With an upper-body clip playing it first
/// replaces the parent's turn with the clip's own root key. Then it turns by half the heading about Z and by a share of the tilt
/// about X; the upper back and the shoulders turn by the heading and their own shares of the tilt, and the head by the mind's head
/// turn. Every quaternion product counts operations; past a thousand the quaternion is renormalised.
/// </para>
/// <para>
/// The sines are the FPU's own: some stored to a float before use, some used as they come, as each callback does.
/// </para>
/// </remarks>
public sealed partial class Igi2HumanSoldier : IIgi2BoneCallbacks
{
    /// <summary>The most the aim's heading turns the body, sixty degrees, <c>1.0471976</c>.</summary>
    public const float AimGammaLimit = 1.0471976f;

    /// <inheritdoc/>
    protected override IIgi2BoneCallbacks? BoneCallbacks => this;

    /// <summary>Gets or sets the aim's heading, <c>vAimGamma</c> (<c>+0xBF4</c>).</summary>
    public float AimGamma { get; set; }

    /// <summary>Gets or sets the aim's tilt, <c>vAimAlpha</c> (<c>+0xBF8</c>).</summary>
    public float AimAlpha { get; set; }

    /// <inheritdoc/>
    public void Run(Igi2BoneBuffer bones, int bone, int parent)
    {
        ArgumentNullException.ThrowIfNull(bones);
        if (Model is not { } model)
        {
            return;
        }

        if (bone == model.ShouldersBone)
        {
            TurnSpine(bones, bone, 0.24705882f, 0.17777778f);
        }
        else if (bone == model.LowerBodyBone)
        {
            TurnLowerBack(bones, bone, parent);
        }
        else if (bone == model.UpperBodyBone)
        {
            TurnSpine(bones, bone, 0.33529413f, 0.31111112f);
        }
        else if (bone == model.HeadBone)
        {
            TurnHead(bones, bone);
        }
    }

    /// <summary><c>HumanSoldier_LowerBackCallback</c> (<c>igi2 0x4EA860</c>): the aim found and the lower back turned toward it.</summary>
    private void TurnLowerBack(Igi2BoneBuffer bones, int bone, int parent)
    {
        if (Mind is not { } mind || IsDead || (UpperBodyFlags & 1) != 0)
        {
            return;
        }

        double dx = 0.0, dy = 4096.0, dz = 0.0;
        if (mind.TargetPosition is { } target && (target.X != 0.0 || target.Y != 0.0 || target.Z != 0.0))
        {
            Vector3f at = bones.Translations[parent];
            Fpu ox = ((Fpu)target.X - Position.X) - at.X;
            Fpu oy = ((Fpu)target.Y - Position.Y) - at.Y;
            Fpu oz = ((Fpu)target.Z - Position.Z) - at.Z;
            Matrix3f o = Orientation;
            Fpu lx = ((oz * o.M31) + (oy * o.M21)) + (ox * o.M11);
            Fpu ly = ((oz * o.M32) + (oy * o.M22)) + (ox * o.M12);
            Fpu lz = ((oz * o.M33) + (oy * o.M23)) + (ox * o.M13);
            if (!(ly.Value < 4096.0))
            {
                (dx, dy, dz) = (lx.Value, ly.Value, lz.Value);
            }
        }

        float alpha = Igi2AiAngles.CalcAlpha(dy, dz);
        float gamma = Igi2Pose.CalcGamma(dx, dy);
        AimGamma = gamma < -AimGammaLimit ? -AimGammaLimit : gamma > AimGammaLimit ? AimGammaLimit : gamma;
        AimAlpha = alpha;

        if (Animation is Igi2HumanAnimation { UpperBody: { } upper } && upper.ActiveRotation(0) is { } key)
        {
            Igi2BoneMath.SwapParentTurn(bones, bone, parent, key);
        }

        Quaternion32 q = bones.Orientations[bone];
        int ops = bones.Ops[bone];

        // Half the heading about Z: the sine stored where the product reads it so, the cosine likewise.
        double half = (double)AimGamma * 0.5;
        Fpu sinRaw = Math.Sin(half), cosRaw = Math.Cos(half);
        Fpu sin = sinRaw.F, cos = cosRaw.F;
        ops++;
        float a = (((Fpu)q.Y - q.Z) * sin).F;
        float b = (((Fpu)q.W - q.X) * sinRaw).F;
        float h = (((Fpu)q.X + q.Y) * sinRaw).F;
        float g = (((Fpu)q.W - q.Z) * cos).F;
        Fpu e = ((Fpu)q.Z + q.W) * cosRaw;
        Fpu sum = ((Fpu)g + e) + h;
        float j = (((((Fpu)q.X - q.Y) * sin) + sum) * 0.5f).F;
        q = new Quaternion32(
            (((((Fpu)q.X + q.W) * cos) + j) - sum).F,
            (((((Fpu)q.Z + q.Y) * cosRaw) + j) - e).F,
            (((Fpu)j + b) - g).F,
            (((Fpu)j + a) - h).F);
        if (ops > Igi2BoneMath.RenormaliseOps)
        {
            ops = 0;
            q = Igi2BoneMath.Normalise(q, wzxy: true);
        }

        bones.Orientations[bone] = q;
        bones.Ops[bone] = ops;

        // A share of the tilt about X, the sines used as they come.
        Igi2BoneMath.TiltAboutX(bones, bone, (Fpu)AimAlpha * (AimAlpha >= 0f ? 0.32352942f : 0.5f));
    }

    /// <summary>
    /// <c>HumanSoldier_ShouldersCallback</c> (<c>igi2 0x4EA240</c>) and <c>HumanSoldier_UpperBackCallback</c> (<c>0x4EA550</c>): the
    /// heading and a share of the tilt (one share up, another down) made one turn, the heading taken back off it, and the bone
    /// turned by that, three operations.
    /// </summary>
    private void TurnSpine(Igi2BoneBuffer bones, int bone, float upShare, float downShare)
    {
        if (IsDead)
        {
            return;
        }

        Fpu tilt = (Fpu)AimAlpha * (AimAlpha >= 0f ? upShare : downShare);
        Fpu headingHalf = (Fpu)AimGamma * 0.5f;
        Fpu gs = ((Fpu)Math.Sin(headingHalf.Value)).F;
        Fpu gcRaw = Math.Cos(headingHalf.Value);
        Fpu tiltHalf = tilt * 0.5f;
        Fpu asRaw = Math.Sin(tiltHalf.Value), acRaw = Math.Cos(tiltHalf.Value);
        Fpu gc = gcRaw.F;

        float c0 = ((Fpu)0f * gs).F;
        float a0 = ((acRaw - asRaw) * gs).F;
        float f0 = (asRaw * gs).F;
        float g0 = (acRaw * gc).F;
        Fpu hRaw = acRaw * gc;
        float h0 = hRaw.F;
        float i0 = ((hRaw + g0) + f0).F;
        float p0 = (((asRaw * gs) + i0) * 0.5f).F;
        float qx = ((((asRaw + acRaw) * gc) + p0) - i0).F;
        Fpu qy = ((gc * 0f) + p0) - h0;
        float qz = (((Fpu)p0 + a0) - g0).F;
        float qw = (((Fpu)p0 + c0) - f0).F;

        Fpu ng = -gs;
        float h1 = (((Fpu)qz - qy) * -ng).F;
        float c1 = (((Fpu)qw - qx) * ng).F;
        float a1 = (((Fpu)qz + qy) * gc).F;
        float g1 = (((Fpu)qx + qz) * 0f).F;
        float f1 = (((Fpu)qw + qy) * (gcRaw - ng)).F;
        Fpu e1 = ((Fpu)qw - qy) * (gcRaw - gs);
        Fpu s1 = ((Fpu)f1 + e1) + g1;
        float p1 = (((((Fpu)qz - qx) * 0f) + s1) * 0.5f).F;
        Fpu x = ((((Fpu)qx + qw) * gc) + p1) - s1;
        Fpu y = ((Fpu)p1 + c1) - e1;
        Fpu z = ((Fpu)p1 + a1) - f1;
        float w = (((Fpu)p1 + h1) - g1).F;

        Quaternion32 q = bones.Orientations[bone];
        float ab = (((Fpu)q.Y - q.Z) * (z - y)).F;
        float cb = (((Fpu)q.W - q.X) * (z + y)).F;
        float ib = (((Fpu)q.X + q.Y) * (x + z)).F;
        float hb = (((Fpu)q.W - q.Z) * ((Fpu)w + y)).F;
        Fpu gb = ((Fpu)q.W + q.Z) * ((Fpu)w - y);
        float fb = ((gb + hb) + ib).F;
        float pb = (((((Fpu)q.X - q.Y) * (z - x)) + fb) * 0.5f).F;
        q = new Quaternion32(
            (((((Fpu)w + x) * ((Fpu)q.X + q.W)) + pb) - fb).F,
            (((((Fpu)q.Z + q.Y) * ((Fpu)w - x)) + pb) - gb).F,
            (((Fpu)pb + cb) - hb).F,
            (((Fpu)pb + ab) - ib).F);
        int ops = bones.Ops[bone] + 3;
        if (ops > Igi2BoneMath.RenormaliseOps)
        {
            ops = 0;
            q = Igi2BoneMath.Normalise(q, wzxy: true);
        }

        bones.Orientations[bone] = q;
        bones.Ops[bone] = ops;
    }

    /// <summary><c>HumanSoldier_HeadCallback</c> (<c>igi2 0x4EAF30</c>): the head turned about Y by the mind's head turn, one operation.</summary>
    private void TurnHead(Igi2BoneBuffer bones, int bone)
    {
        if (Mind is not { } mind || IsDead)
        {
            return;
        }

        double half = (double)mind.HeadTurn * 0.5;
        Fpu s = Math.Sin(half);
        Fpu c = ((Fpu)Math.Cos(half)).F;
        Quaternion32 q = bones.Orientations[bone];
        float a = (((Fpu)q.Z - q.Y) * -s).F;
        float cc = (((Fpu)q.W - q.X) * s).F;
        float d = (((Fpu)q.Y + q.Z) * c).F;
        float f = (((Fpu)q.Z + q.X) * 0f).F;
        float g = (((Fpu)q.Y + q.W) * (c - s)).F;
        Fpu e = (c + s) * ((Fpu)q.W - q.Y);
        Fpu sum = ((Fpu)g + e) + f;
        float p = (((((Fpu)q.Z - q.X) * 0f) + sum) * 0.5f).F;
        q = new Quaternion32(
            (((((Fpu)q.X + q.W) * c) + p) - sum).F,
            (((Fpu)p + cc) - e).F,
            (((Fpu)p + d) - g).F,
            (((Fpu)p + a) - f).F);
        int ops = bones.Ops[bone] + 1;
        if (ops > Igi2BoneMath.RenormaliseOps)
        {
            ops = 0;
            q = Igi2BoneMath.Normalise(q, wzxy: false);
        }

        bones.Orientations[bone] = q;
        bones.Ops[bone] = ops;
    }
}
