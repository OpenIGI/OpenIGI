using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>A title's mouse aim, separate from tick-driven camera effects, for render-time look.</summary>
public interface ITitlePlayerLook
{
    /// <summary>Gets whether the player currently controls its own view.</summary>
    bool CanPreviewLook { get; }

    /// <summary>Gets the committed mouse aim's local-to-world right/forward/up orientation.</summary>
    Matrix3f LookOrientation { get; }

    /// <summary>Accumulates one rendered frame's aim, preserving pitch clamps before the next tick.</summary>
    /// <param name="mouseX">This frame's horizontal counts.</param>
    /// <param name="mouseY">This frame's vertical counts.</param>
    /// <param name="sensitivity">The current profile sensitivity.</param>
    /// <param name="invertMouse">Whether vertical look is inverted.</param>
    void GatherLook(float mouseX, float mouseY, float sensitivity, bool invertMouse) { }

    /// <summary>Discards the frame aim when the view loses control or its tick has consumed it.</summary>
    void ResetLook() { }

    /// <summary>Predicts mouse aim with the counts waiting for the next tick, without changing gameplay.</summary>
    /// <param name="mouseX">Unconsumed horizontal counts.</param>
    /// <param name="mouseY">Unconsumed vertical counts.</param>
    /// <param name="sensitivity">The current profile sensitivity.</param>
    /// <param name="invertMouse">Whether vertical look is inverted.</param>
    /// <returns>The previewed local-to-world right/forward/up orientation.</returns>
    Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse);
}
