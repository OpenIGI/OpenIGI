using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <content>
/// The curves view under the camera lane, as Unreal's curve editor in small: one channel of the scene's camera over its clock --
/// how fast it flies or turns, or a value the game blends (place, the three angles, the lens) -- read off the game's own path, its
/// keys as dots on it. A key's dot dragged up or down writes its value on that channel.
/// </content>
internal sealed partial class SequencerPanel
{
    private const float CurvesRows = 9;
    private static readonly CutsceneChannel[] Channels = Enum.GetValues<CutsceneChannel>();

    private CutsceneChannel _channel = CutsceneChannel.Speed;

    // A key's dot held on the curves: the key, the value it is being dragged to, and the range the view is frozen at meanwhile.
    private (MissionTask Key, double Value, double Low, double High)? _curveDrag;

    private float CurvesHeight => _curves.IsChecked ? Theme.Px(CurvesRows * 15f) + Theme.Px(10f) : 0f;

    /// <summary>Shows the curves view on a channel, or hides it, as the Curves button and a click on a channel's name do.</summary>
    /// <param name="channel">The channel; null to hide the view.</param>
    public void ShowCurves(CutsceneChannel? channel)
    {
        _curves.IsChecked = channel is not null;
        _channel = channel ?? _channel;
    }

    // The channels' names, down the label column.
    private static string ChannelName(CutsceneChannel channel) => channel switch
    {
        CutsceneChannel.Speed => "Speed",
        CutsceneChannel.TurnRate => "Turn rate",
        CutsceneChannel.X => "X",
        CutsceneChannel.Y => "Y",
        CutsceneChannel.Z => "Height",
        CutsceneChannel.TurnX => "Turn X",
        CutsceneChannel.TurnY => "Turn Y",
        CutsceneChannel.TurnZ => "Turn Z",
        _ => "Lens",
    };

    private void DrawCurves(UiPainter painter, MissionTask scene, float top)
    {
        if (CurvesHeight <= 0)
        {
            return;
        }

        UiRect area = new(Timeline.X, top, Timeline.Width, CurvesHeight);
        painter.FillRect(new UiRect(Bounds.X, top, Bounds.Width, 1f), Theme.Border);
        painter.FillRect(area, Theme.Background.WithAlpha(150));

        // The channels, the one shown marked.
        for (int i = 0; i < Channels.Length; i++)
        {
            bool shown = Channels[i] == _channel;
            UiVec2 at = new(Bounds.X + Theme.Px(14f), top + Theme.Px(5f) + (i * Theme.Px(15f)));
            painter.DrawText(Theme.SmallFont, ChannelName(Channels[i]), at, Theme.SmallTextSize, shown ? Theme.Accent : Theme.TextMuted);
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        if (shots.Count == 0)
        {
            return;
        }

        CutsceneKey[] path = [.. shots.Select(static s => CutsceneMoves.KeyOf(s.Camera))];
        float scale = (float)CutsceneModel.TimeScale(scene);
        double length = CutsceneModel.Length(scene);
        (double low, double high) = _curveDrag is { } held ? (held.Low, held.High) : Range(path, scale, length, shots);
        float pad = Theme.Px(8f);
        float Y(double value) => (float)(area.Bottom - pad - ((value - low) / (high - low) * (area.Height - (2 * pad))));

        painter.PushClip(area);

        // Each key's moment, faint, so the curve's turns can be told against them.
        foreach (CutsceneShot shot in shots)
        {
            if (!CutsceneEasing.IsHelper(shot.Camera))
            {
                painter.FillRect(new UiRect(X(shot.Start), area.Y, 1f, area.Height), Theme.Border.WithAlpha(140));
            }
        }

        // The curve, a point every few pixels across the scene.
        Bgra32 ink = Bgra32.FromRgb(0xE8, 0xB0, 0x30);
        UiVec2? last = null;
        for (float x = MathF.Max(area.X, X(0)); x <= MathF.Min(area.Right, X(length)) + 1f; x += Theme.Px(3f))
        {
            double seconds = Math.Min(Seconds(x), length);
            UiVec2 point = new(x, Y(CutsceneCurves.At(path, scale, seconds, _channel)));
            if (last is { } from)
            {
                painter.StrokeSoft(from, point, Theme.Px(1.6f), ink);
            }

            last = point;
        }

        // The keys' own values, the one held where it is being dragged.
        if (CutsceneCurves.IsValue(_channel))
        {
            MissionTask? selected = _workspace.Selection.Primary;
            foreach (CutsceneShot shot in shots.Where(static s => !CutsceneEasing.IsHelper(s.Camera)))
            {
                double value = _curveDrag is { } drag && ReferenceEquals(drag.Key, shot.Camera) ? drag.Value : CutsceneCurves.Of(shot.Camera, _channel);
                bool isSelected = ReferenceEquals(shot.Camera, selected);
                UiVec2 at = new(X(shot.Start), Y(value));
                painter.FillDot(at, Theme.Px(isSelected ? 5.5f : 4.5f), Theme.Background);
                painter.FillDot(at, Theme.Px(isSelected ? 4f : 3f), isSelected ? Theme.Accent : Bgra32.White);
            }
        }

        painter.PopClip();

        string unit = CutsceneCurves.Unit(_channel);
        painter.DrawText(Theme.SmallFont, string.Create(CultureInfo.InvariantCulture, $"{high:0.#} {unit}"), new UiVec2(area.X + Theme.Px(4f), area.Y + Theme.Px(2f)), Theme.SmallTextSize, Theme.TextMuted);
        painter.DrawText(Theme.SmallFont, string.Create(CultureInfo.InvariantCulture, $"{low:0.#} {unit}"), new UiVec2(area.X + Theme.Px(4f), area.Bottom - Theme.SmallTextSize - Theme.Px(3f)), Theme.SmallTextSize, Theme.TextMuted);
    }

    // The channel's range over the scene, its keys' values in it, a little room above and below.
    private (double Low, double High) Range(CutsceneKey[] path, float scale, double length, IReadOnlyList<CutsceneShot> shots)
    {
        double low = double.MaxValue, high = double.MinValue;
        for (int i = 0; i <= 240; i++)
        {
            double value = CutsceneCurves.At(path, scale, length * i / 240, _channel);
            (low, high) = (Math.Min(low, value), Math.Max(high, value));
        }

        if (CutsceneCurves.IsValue(_channel))
        {
            foreach (CutsceneShot shot in shots)
            {
                double value = CutsceneCurves.Of(shot.Camera, _channel);
                (low, high) = (Math.Min(low, value), Math.Max(high, value));
            }
        }

        // A rate from nought up: it is never less.
        double room = Math.Max((high - Math.Min(low, 0)) * 0.08, 0.5);
        return CutsceneCurves.IsValue(_channel) ? (low - room, high + room) : (0, high + room);
    }

    // A press on the curves view: a channel's name shows it; a key's dot is taken hold of. True when the press was the view's.
    private bool PressCurves(MissionTask scene, UiVec2 pointer, float top)
    {
        if (CurvesHeight <= 0 || pointer.Y < top || pointer.Y >= top + CurvesHeight)
        {
            return false;
        }

        if (pointer.X < Timeline.X)
        {
            int row = (int)((pointer.Y - top - Theme.Px(5f)) / Theme.Px(15f));
            _channel = Channels[Math.Clamp(row, 0, Channels.Length - 1)];
            return true;
        }

        if (!CutsceneCurves.IsValue(_channel))
        {
            return false;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        CutsceneKey[] path = [.. shots.Select(static s => CutsceneMoves.KeyOf(s.Camera))];
        (double low, double high) = Range(path, (float)CutsceneModel.TimeScale(scene), CutsceneModel.Length(scene), shots);
        UiRect area = new(Timeline.X, top, Timeline.Width, CurvesHeight);
        float pad = Theme.Px(8f);
        foreach (CutsceneShot shot in shots.Where(static s => !CutsceneEasing.IsHelper(s.Camera)))
        {
            double value = CutsceneCurves.Of(shot.Camera, _channel);
            float y = (float)(area.Bottom - pad - ((value - low) / (high - low) * (area.Height - (2 * pad))));
            if (MathF.Abs(X(shot.Start) - pointer.X) <= Theme.Px(7f) && MathF.Abs(y - pointer.Y) <= Theme.Px(7f))
            {
                _workspace.Selection.Set(shot.Camera);
                Seek(shot.Start);
                _curveDrag = (shot.Camera, value, low, high);
                return true;
            }
        }

        return false;
    }

    // A held dot followed up and down.
    private bool DragCurves(float pointerY, float top)
    {
        if (_curveDrag is not { } drag)
        {
            return false;
        }

        float pad = Theme.Px(8f);
        double share = (top + CurvesHeight - pad - pointerY) / (CurvesHeight - (2 * pad));
        _curveDrag = drag with { Value = drag.Low + (share * (drag.High - drag.Low)) };
        return true;
    }

    // A dot let go: its key's value written.
    private void ReleaseCurves(MissionDocument document)
    {
        if (_curveDrag is { } drag && _scene is { } scene && Math.Abs(drag.Value - CutsceneCurves.Of(drag.Key, _channel)) > 1e-6)
        {
            _workspace.Execute(CutsceneEasing.WithFollow(document, scene, drag.Key, CutsceneCurves.Set(document, drag.Key, _channel, drag.Value)));
        }

        _curveDrag = null;
    }
}
