using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Cameras;

/// <summary>Camera, lens and hands captured together at a title's simulation ticks for Smooth Motion.</summary>
public sealed class TitlePlayerPresentation
{
    private Frame? _previous;
    private Frame? _current;
    private Matrix3f? _previewLook;

    /// <summary>Gets whether the captured tick pair and this frame permit live mouse aim.</summary>
    public bool HasLookPreview => _previewLook is not null && _previous?.Look is not null && _current?.Look is not null;

    /// <summary>Sets the render-time mouse aim; null selects the ordinary tick camera.</summary>
    /// <param name="orientation">The preview's local-to-world right/forward/up orientation.</param>
    public void SetLookPreview(Matrix3f? orientation) => _previewLook = orientation;

    /// <summary>Starts a new camera history, including after loading or changing camera mode.</summary>
    /// <param name="player">The title player.</param>
    public void Reset(ITitlePlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _current = Capture(player);
        _previous = _current;
        _previewLook = null;
    }

    /// <summary>Captures a completed tick without changing simulation state.</summary>
    /// <param name="player">The title player.</param>
    public void CaptureTick(ITitlePlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Frame frame = Capture(player);
        _previous = _current;
        _current = frame;
        if (_previous is null || frame.Cutscene || _previous.Cutscene != frame.Cutscene
            || (frame.Eye.Position - _previous.Eye.Position).Length > 32.0)
        {
            _previous = frame;
        }
    }

    /// <summary>Presents the camera in metres, using the same tick fraction as the hands.</summary>
    /// <param name="alpha">Current tick's share; one for disabled smoothing or pause.</param>
    /// <returns>The camera.</returns>
    public Camera3D Camera(float alpha)
    {
        Frame current = _current ?? throw new InvalidOperationException("No title frame captured.");
        Frame previous = _previous ?? current;
        if (_previewLook is { } look && current.Look is { } currentLook && previous.Look is { } previousLook)
        {
            Matrix3f fromEffect = previous.Eye.Orientation * CameraOrientation(previousLook).Transposed();
            Matrix3f toEffect = current.Eye.Orientation * CameraOrientation(currentLook).Transposed();
            Matrix3f effect = alpha >= 1f ? toEffect : alpha <= 0f ? fromEffect
                : PresentationBlend.InterpolateRotation(fromEffect, toEffect, alpha);
            Vector3d position = alpha >= 1f ? current.Eye.Position : alpha <= 0f ? previous.Eye.Position
                : PresentationBlend.Lerp(previous.Eye.Position, current.Eye.Position, alpha);
            return new Camera3D(position, effect * CameraOrientation(look));
        }
        if (alpha >= 1f) { return current.Eye; }
        if (alpha <= 0f) { return previous.Eye; }
        // Camera rows are right/up/forward, a left-handed frame. Preserve that handedness while blending.
        Vector3f forward = PresentationBlend.Lerp(previous.Eye.Orientation.Row3, current.Eye.Orientation.Row3, alpha).Normalized();
        Vector3f right = PresentationBlend.Lerp(previous.Eye.Orientation.Row1, current.Eye.Orientation.Row1, alpha);
        right = (right - (forward * Vector3f.Dot(right, forward))).Normalized();
        Vector3f up = Vector3f.Cross(right, forward);
        return new Camera3D(PresentationBlend.Lerp(previous.Eye.Position, current.Eye.Position, alpha), Matrix3f.FromRows(right, up, forward));
    }

    /// <summary>Presents the world lens between its tick values.</summary>
    /// <param name="alpha">Current tick's share.</param>
    /// <returns>The horizontal half-angle tangent.</returns>
    public float FieldOfView(float alpha)
    {
        Frame current = _current ?? throw new InvalidOperationException("No title frame captured.");
        if (alpha >= 1f) { return current.FieldOfView; }
        Frame previous = _previous ?? current;
        return previous.FieldOfView + ((current.FieldOfView - previous.FieldOfView) * Math.Clamp(alpha, 0f, 1f));
    }

    /// <summary>Presents the hands and rigid attachments, retaining both skin poses for the renderer.</summary>
    /// <param name="alpha">Current tick's share.</param>
    /// <returns>The hands, or null when the title draws none.</returns>
    public TitleFirstPersonView? FirstPerson(float alpha)
    {
        TitleFirstPersonView? hands = BlendFirstPerson(alpha);
        Frame? current = _current, previous = _previous;
        if (hands is null || _previewLook is not { } look || current?.Look is not { } currentLook
            || previous?.Look is not { } previousLook)
        {
            return hands;
        }

        alpha = Math.Clamp(alpha, 0f, 1f);
        Vector3d eye = PresentationBlend.Lerp(previous.Eye.Position, current.Eye.Position, alpha) * 4096.0;
        // Strip tick mouse aim from the object transforms before blending, then apply the frame's aim.
        TitleFirstPersonView? from = previous.Hands, to = current.Hands;
        if (from is null || to is null) { return hands; }
        Vector3d fromOffset = from.Origin - (previous.Eye.Position * 4096.0);
        Vector3d toOffset = to.Origin - (current.Eye.Position * 4096.0);
        Vector3f localOffset = PresentationBlend.Lerp(previousLook.Transform(ToFloat(fromOffset)), currentLook.Transform(ToFloat(toOffset)), alpha);
        Matrix3f localOrientation = PresentationBlend.InterpolateRotation(previousLook.Transposed() * from.Orientation,
            currentLook.Transposed() * to.Orientation, alpha);
        Vector3f offset = look.Transposed().Transform(localOffset);
        return hands with { Origin = eye + new Vector3d(offset.X, offset.Y, offset.Z), Orientation = look * localOrientation };
    }

    private TitleFirstPersonView? BlendFirstPerson(float alpha)
    {
        TitleFirstPersonView? current = _current?.Hands;
        TitleFirstPersonView? previous = _previous?.Hands;
        if (current is null || previous is null || alpha >= 1f || current.Parts.Count != previous.Parts.Count)
        {
            return current;
        }

        for (int i = 0; i < current.Parts.Count; i++)
        {
            if (!string.Equals(current.Parts[i].Model, previous.Parts[i].Model, StringComparison.Ordinal)
                || current.Parts[i].Pose?.BoneCount != previous.Parts[i].Pose?.BoneCount)
            {
                return current;
            }
        }

        alpha = Math.Clamp(alpha, 0f, 1f);
        TitleViewModelPart[] parts = new TitleViewModelPart[current.Parts.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            TitleViewModelPart from = previous.Parts[i], to = current.Parts[i];
            parts[i] = to with
            {
                Position = PresentationBlend.Lerp(from.Position, to.Position, alpha),
                Orientation = PresentationBlend.InterpolateRotation(from.Orientation, to.Orientation, alpha),
                PreviousPose = from.Pose,
            };
        }

        return current with
        {
            Origin = PresentationBlend.Lerp(previous.Origin, current.Origin, alpha),
            Orientation = PresentationBlend.InterpolateRotation(previous.Orientation, current.Orientation, alpha),
            ObjectFieldOfView = previous.ObjectFieldOfView + ((current.ObjectFieldOfView - previous.ObjectFieldOfView) * alpha),
            Parts = parts, PoseInterpolation = alpha,
        };
    }

    private static Frame Capture(ITitlePlayer player) => new(
        new Camera3D(player.EyePosition * (1.0 / 4096.0), player.ViewOrientation), player.FieldOfViewScale,
        (player as ITitleFirstPerson)?.FirstPerson, player.IsCutscenePlaying,
        player is ITitlePlayerLook { CanPreviewLook: true } look ? look.LookOrientation : null);

    private static Matrix3f CameraOrientation(Matrix3f look) => Matrix3f.FromRows(look.Column1, look.Column3, look.Column2);

    private static Vector3f ToFloat(Vector3d value) => new((float)value.X, (float)value.Y, (float)value.Z);

    private sealed record Frame(Camera3D Eye, float FieldOfView, TitleFirstPersonView? Hands, bool Cutscene, Matrix3f? Look);
}
