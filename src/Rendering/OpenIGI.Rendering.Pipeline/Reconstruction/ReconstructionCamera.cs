using System.Numerics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Reconstruction;

/// <summary>The camera as an NVIDIA reconstruction is told it: projection, motion, basis and jitter.</summary>
/// <remarks>
/// <para>
/// Everything is in <see cref="Matrix4x4"/>'s convention — row vectors, <c>v * M</c> — which is Streamline's
/// own, so nothing is transposed on the way across. The engine's matrices act on column vectors, so every
/// one built here from engine types is written as its transpose.
/// </para>
/// <para>
/// The projection is the one the depth buffer holds, not the one the game builds. The game's is OpenGL's,
/// clip depth from −1 to 1; the vertex stages remap it to reverse-Z (<c>z' = (w − z) / 2</c>), so near lands
/// on one and far on nought. Handing over the game's matrix would describe a depth buffer this renderer
/// does not have, so the reverse-Z projection is rebuilt from the same scales and planes.
/// </para>
/// <para>
/// The previous-frame transform is built in camera-relative space: this world's coordinates run to tens of
/// kilometres, and the frame-to-frame travel is a few centimetres, so the difference is taken in double
/// precision before anything is narrowed to float — the same reason the engine's own motion is measured
/// that way.
/// </para>
/// </remarks>
public static class ReconstructionCamera
{
    /// <summary>What the motion texture is multiplied by: it holds current minus previous, in UV.</summary>
    /// <remarks>
    /// Streamline wants previous minus current, normalised so ±1 spans the screen. A UV delta of one already
    /// spans it, so the magnitude is right and only the sign flips — on both axes, since the UV's Y runs down
    /// the image exactly as Streamline's does.
    /// </remarks>
    public static readonly Vector2 MotionVectorScale = new(-1f, -1f);

    /// <summary>The jitter's vertical sign in image space, where the game's is in OpenGL's clip space.</summary>
    public const float VerticalJitterSign = -1f;

    /// <summary>Builds the constants for one frame.</summary>
    /// <param name="frame">The frame's camera and the one before it.</param>
    /// <returns>The constants.</returns>
    public static RhiStreamlineConstants Build(in ReconstructionFrame frame)
    {
        Matrix4x4 viewToClip = Projection(frame.Unprojection);
        Matrix4x4 clipToView = InverseProjection(frame.Unprojection);
        Matrix4x4 previousViewToClip = Projection(frame.PreviousUnprojection);
        Matrix4x4 previousClipToView = InverseProjection(frame.PreviousUnprojection);

        // This frame's view → camera-relative world → shifted by how far the camera moved → the previous
        // frame's view. Camera-relative on both sides, so only the small difference is ever a float.
        Vector3 travel = new(
            (float)(frame.CameraPosition.X - frame.PreviousCameraPosition.X),
            (float)(frame.CameraPosition.Y - frame.PreviousCameraPosition.Y),
            (float)(frame.CameraPosition.Z - frame.PreviousCameraPosition.Z));

        Matrix4x4 viewToWorld = Rotation(frame.ViewToWorld);
        Matrix4x4 worldToPreviousView = Matrix4x4.Transpose(Rotation(frame.PreviousViewToWorld));
        Matrix4x4 previousViewToWorld = Rotation(frame.PreviousViewToWorld);
        Matrix4x4 worldToView = Matrix4x4.Transpose(viewToWorld);

        Matrix4x4 clipToPrevClip = clipToView
            * viewToWorld
            * Matrix4x4.CreateTranslation(travel)
            * worldToPreviousView
            * previousViewToClip;

        Matrix4x4 prevClipToClip = previousClipToView
            * previousViewToWorld
            * Matrix4x4.CreateTranslation(-travel)
            * worldToView
            * viewToClip;

        Matrix3f r = frame.ViewToWorld;
        return new RhiStreamlineConstants
        {
            CameraViewToClip = viewToClip,
            ClipToCameraView = clipToView,
            ClipToPrevClip = clipToPrevClip,
            PrevClipToClip = prevClipToClip,
            Jitter = new Vector2(frame.JitterX, frame.JitterY * VerticalJitterSign),
            MotionVectorScale = MotionVectorScale,
            CameraPosition = new Vector3((float)frame.CameraPosition.X, (float)frame.CameraPosition.Y, (float)frame.CameraPosition.Z),

            // View space to world: the basis vectors are its columns, and the camera looks down its own −Z.
            CameraRight = new Vector3(r.M11, r.M21, r.M31),
            CameraUp = new Vector3(r.M12, r.M22, r.M32),
            CameraForward = new Vector3(-r.M13, -r.M23, -r.M33),
            Near = frame.Unprojection.Near,
            Far = frame.Unprojection.Far,
            VerticalFieldOfView = 2f * MathF.Atan(1f / MathF.Max(frame.Unprojection.YScale, 1e-6f)),
            AspectRatio = frame.Unprojection.YScale / MathF.Max(frame.Unprojection.XScale, 1e-6f),
            DepthInverted = true,
            CameraMotionIncluded = true,

            // The motion is written with the jitter cancelled out of it, not carried in it.
            MotionVectorsJittered = false,
            Reset = frame.Reset,
        };
    }

    /// <summary>World-to-view and its inverse, about a fixed origin, for a reconstruction that works in world space.</summary>
    /// <param name="viewToWorld">The camera's rotation, view to world.</param>
    /// <param name="cameraPosition">The camera's position.</param>
    /// <param name="origin">The origin the world is described about; fixed for a level so it does not move under the history.</param>
    /// <returns>The pair, in <see cref="Matrix4x4"/>'s convention.</returns>
    public static (Matrix4x4 WorldToView, Matrix4x4 ViewToWorld) WorldTransforms(
        in Matrix3f viewToWorld,
        Vector3d cameraPosition,
        Vector3d origin)
    {
        Vector3 position = new(
            (float)(cameraPosition.X - origin.X),
            (float)(cameraPosition.Y - origin.Y),
            (float)(cameraPosition.Z - origin.Z));

        Matrix4x4 toWorld = Rotation(viewToWorld) * Matrix4x4.CreateTranslation(position);
        Matrix4x4 toView = Matrix4x4.CreateTranslation(-position) * Matrix4x4.Transpose(Rotation(viewToWorld));
        return (toView, toWorld);
    }

    /// <summary>The reverse-Z projection this renderer's depth buffer holds, near on one and far on nought.</summary>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <returns>The projection, as row vectors.</returns>
    public static Matrix4x4 Projection(in DepthUnprojection unprojection)
    {
        float near = unprojection.Near;
        float far = unprojection.Far;
        float span = MathF.Max(far - near, 1e-6f);

        // Column-vector form: x' = xs·x, y' = ys·y, z' = n/(f−n)·z + nf/(f−n), w' = −z. Written transposed.
        return new Matrix4x4(
            unprojection.XScale, 0f, 0f, 0f,
            0f, unprojection.YScale, 0f, 0f,
            0f, 0f, near / span, -1f,
            0f, 0f, near * far / span, 0f);
    }

    /// <summary>The inverse of <see cref="Projection"/>, built analytically rather than inverted numerically.</summary>
    /// <param name="unprojection">The projection's scales and planes.</param>
    /// <returns>The inverse, as row vectors.</returns>
    public static Matrix4x4 InverseProjection(in DepthUnprojection unprojection)
    {
        float near = unprojection.Near;
        float far = unprojection.Far;
        float span = MathF.Max(far - near, 1e-6f);
        float a = near / span;
        float b = near * far / span;

        // Column-vector form: x = x'/xs, y = y'/ys, z = −w', w = (z' + a·w')/b. Written transposed.
        return new Matrix4x4(
            1f / unprojection.XScale, 0f, 0f, 0f,
            0f, 1f / unprojection.YScale, 0f, 0f,
            0f, 0f, 0f, 1f / b,
            0f, 0f, -1f, a / b);
    }

    /// <summary>A view-to-world rotation as a row-vector matrix: the transpose of the engine's.</summary>
    private static Matrix4x4 Rotation(in Matrix3f m) => new(
        m.M11, m.M21, m.M31, 0f,
        m.M12, m.M22, m.M32, 0f,
        m.M13, m.M23, m.M33, 0f,
        0f, 0f, 0f, 1f);
}

/// <summary>One frame's camera and the one before it, as the reconstruction constants are built from.</summary>
/// <param name="Unprojection">This frame's projection scales and planes.</param>
/// <param name="PreviousUnprojection">The previous frame's.</param>
/// <param name="ViewToWorld">This frame's rotation, view to camera-relative world.</param>
/// <param name="PreviousViewToWorld">The previous frame's.</param>
/// <param name="CameraPosition">This frame's camera position, in double precision.</param>
/// <param name="PreviousCameraPosition">The previous frame's.</param>
/// <param name="JitterX">The horizontal jitter, in render pixels, as the game applied it.</param>
/// <param name="JitterY">The vertical jitter, in render pixels, in the game's OpenGL clip convention.</param>
/// <param name="Reset">Whether the previous frame bears no relation to this one.</param>
public readonly record struct ReconstructionFrame(
    DepthUnprojection Unprojection,
    DepthUnprojection PreviousUnprojection,
    Matrix3f ViewToWorld,
    Matrix3f PreviousViewToWorld,
    Vector3d CameraPosition,
    Vector3d PreviousCameraPosition,
    float JitterX,
    float JitterY,
    bool Reset);
