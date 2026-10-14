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

    /// <summary>Starts a new camera history, including after loading or changing camera mode.</summary>
    /// <param name="player">The title player.</param>
    public void Reset(ITitlePlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        _current = Capture(player);
        _previous = _current;
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
        (player as ITitleFirstPerson)?.FirstPerson, player.IsCutscenePlaying);

    private sealed record Frame(Camera3D Eye, float FieldOfView, TitleFirstPersonView? Hands, bool Cutscene);
}
