using OpenIGI.Core.Graphics;
using OpenIGI.Host.Services;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

/// <summary>Skipping a cutscene: the break key held until the ring in the corner fills.</summary>
public sealed partial class GameHost
{
    private static readonly Bgra32 SkipInk = Bgra32.FromRgb(236, 235, 230);
    private static readonly Bgra32 SkipShadow = Bgra32.FromRgba(0, 0, 0, 160);
    private static readonly Bgra32 SkipTrack = Bgra32.FromRgba(236, 235, 230, 70);

    private readonly CutsceneSkipHold _cutsceneSkip = new();
    private int _lastCutsceneSkipMilliseconds;

    /// <summary>Advances the skip hold by a frame, and breaks the host's cutscene the frame it completes.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void UpdateCutsceneSkip(FrameLoopState state)
    {
        int now = _loop.TimeSource.Milliseconds;
        float seconds = Math.Clamp((now - _lastCutsceneSkipMilliseconds) / 1000f, 0f, 0.1f);
        _lastCutsceneSkipMilliseconds = now;

        bool free = !_menuFlow.MenuMode && !state.Console.IsOpen;
        bool hostCutscene = free && _mission.Cutscenes?.IsPlaying == true;
        bool titleCutscene = free && TitlePlayer is { IsCutscenePlaying: true };
        // On a touch screen the whole screen is the key: a finger held anywhere fills the ring.
        bool held = _window.Input.Keyboard.IsDown(Input.KeyCode.Escape) || _window.Input.Touch.AnyDown;

        if (_cutsceneSkip.Update(hostCutscene || titleCutscene, held, seconds)
            && hostCutscene)
        {
            _mission.Cutscenes!.RequestBreak();
            _cutsceneSounds.StopAll();
            _mission.Objectives?.ClearCutsceneMessages();
        }
    }

    /// <summary>Draws the skip prompt in the corner of a cutscene: what to hold, and a ring filling as it is held.</summary>
    /// <param name="state">The frame loop's state, whose sprite batch it draws into.</param>
    private void DrawCutsceneSkip(FrameLoopState state)
    {
        if (_cutsceneSkip.Opacity <= 0f || _shellType is not { } type)
        {
            return;
        }

        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        // A thumb needs a bigger target to see than a key does.
        LayoutContext context = new(_window.Width, _window.Height, userScale: _window.Input.Touch.InUse ? 2f : 1f);
        UiPainter painter = UiPainter.Create(state.Batch, context);
        painter.PushOpacity(_cutsceneSkip.Opacity);

        float radius = context.Px(17f);
        float thickness = context.Px(3f);
        UiVec2 centre = new(
            context.SafeRect.Right - context.Px(40f) - radius,
            context.SafeRect.Bottom - context.Px(40f) - radius);

        painter.FillArc(centre, 0f, radius + thickness, 0f, MathF.Tau, SkipShadow);
        painter.FillArc(centre, radius - thickness, radius, 0f, MathF.Tau, SkipTrack);
        if (_cutsceneSkip.Progress > 0f)
        {
            painter.FillArc(centre, radius - thickness, radius, 0f, MathF.Tau * _cutsceneSkip.Progress, SkipInk);
        }

        // The key inside the ring; a touch screen has none to name, the whole screen being the button.
        if (!_window.Input.Touch.InUse)
        {
            const string key = "ESC";
            float keySize = context.Px(10f);
            UiVec2 keyExtent = type.Mono.Measure(key, keySize);
            painter.DrawText(type.Mono, key, new UiVec2(centre.X - (keyExtent.X * 0.5f), centre.Y - (keyExtent.Y * 0.5f)), keySize, SkipInk);
        }

        const string label = "HOLD TO SKIP";
        float labelSize = context.Px(15f);
        UiVec2 labelExtent = type.Heading.Measure(label, labelSize);
        painter.DrawTextShadowed(
            type.Heading,
            label,
            new UiVec2(centre.X - radius - context.Px(12f) - labelExtent.X, centre.Y - (labelExtent.Y * 0.5f)),
            labelSize,
            SkipInk,
            SkipShadow,
            new UiVec2(0f, context.Px(1f)));

        painter.PopOpacity();
        state.Batch.End();
    }
}
