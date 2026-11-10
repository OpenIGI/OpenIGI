using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Computer;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Rendering;

/// <content>
/// The save page: the computer's uplink to headquarters, where a mission is saved and loaded as I.G.I. 2's computer does it
/// (<see cref="ComputerSaveFlow"/>) -- an upload to one of five slots, which takes a moment and one of the battery's charges, and
/// a download from a slot that holds one.
/// </content>
public sealed partial class TacticalComputerRenderer
{
    /// <summary>The seconds one of the save screen's ticks takes, the game's own thirtieth.</summary>
    private const float SaveTick = 1f / 30f;

    private readonly ComputerSaveFlow _save = new();
    private readonly List<SaveRow> _saveRows = [];
    private float _saveClock;
    private bool _saveShown;
    private int _hoverSaveRow = -1;

    /// <inheritdoc/>
    public IComputerSaves? Saves
    {
        get => _save.Saves;
        set => _save.Saves = value;
    }

    // An upload under way holds the page until it is done, as the original's screen has no way off it.
    private bool SaveBusy => _page == Page.Save && _save.State is ComputerSaveState.Uploading or ComputerSaveState.Complete;

    private enum SaveRowKind
    {
        Upload,
        Download,
        Slot,
        Cancel,
        Yes,
        No,
    }

    private readonly record struct SaveRow(UiRect Rect, SaveRowKind Kind, int Slot, string Title, string Detail, bool Dimmed);

    // The page's run: its ticks at the game's own rate, its rows under the pointer, and a click on one.
    private void UpdateSave(InputState input, float step, LayoutContext context, in TacticalComputerChrome chrome)
    {
        if (_page != Page.Save || Saves is null)
        {
            _saveShown = false;
            _hoverSaveRow = -1;
            return;
        }

        if (!_saveShown)
        {
            _saveShown = true;
            _saveClock = 0f;
            _save.Open();
            _save.Run();
        }

        _saveClock += step;
        while (_saveClock >= SaveTick && _page == Page.Save && Saves is not null)
        {
            _saveClock -= SaveTick;
            _save.Run();
        }

        LayOutSaveRows(context, chrome.Page);
        _hoverSaveRow = -1;
        for (int i = 0; i < _saveRows.Count; i++)
        {
            if (TacticalComputerChrome.Contains(_saveRows[i].Rect, _mouseX, _mouseY))
            {
                _hoverSaveRow = i;
            }
        }

        if (_hoverSaveRow < 0 || !input.Mouse.WasPressed(MouseButton.Left))
        {
            return;
        }

        SaveRow row = _saveRows[_hoverSaveRow];
        switch (row.Kind)
        {
            case SaveRowKind.Upload:
                _save.Upload();
                break;
            case SaveRowKind.Download:
                _save.Download();
                break;
            case SaveRowKind.Cancel:
                _save.Cancel();
                break;
            case SaveRowKind.Yes:
                _save.Overwrite();
                break;
            case SaveRowKind.No:
                _save.Keep();
                break;
            case SaveRowKind.Slot:
                _save.Choose(row.Slot);
                break;
        }
    }

    // The rows the shown page offers, down the card from under its heading.
    private void LayOutSaveRows(LayoutContext context, UiRect card)
    {
        _saveRows.Clear();
        float pad = context.Px(34f);
        float x = card.X + pad, width = card.Width - (pad * 2f);
        float y = card.Y + context.Px(88f) + context.Px(40f);
        float height = context.Px(52f), gap = context.Px(8f);

        void Add(SaveRowKind kind, int slot, string title, string detail, bool dimmed = false)
        {
            _saveRows.Add(new SaveRow(new UiRect(x, y, width, height), kind, slot, title, detail, dimmed));
            y += height + gap;
        }

        switch (_save.State)
        {
            case ComputerSaveState.Main:
                if (_save.IsSaveAllowed)
                {
                    bool flat = _save.Battery <= 0f;
                    Add(SaveRowKind.Upload, -1, "UPLOAD MISSION DATA TO HQ", flat ? "BATTERY LOW" : "SAVE", flat);
                }

                Add(SaveRowKind.Download, -1, "DOWNLOAD MISSION DATA FROM HQ", "LOAD");
                break;
            case ComputerSaveState.Upload or ComputerSaveState.Download:
                foreach (ComputerSaveSlot slot in _save.SlotLabels)
                {
                    Add(SaveRowKind.Slot, slot.Slot, SlotTitle(slot), slot.When, slot.IsFree);
                }

                y += gap;
                Add(SaveRowKind.Cancel, -1, "CANCEL", string.Empty);
                break;
            case ComputerSaveState.Overwrite:
                Add(SaveRowKind.Yes, -1, "OVERWRITE " + SlotTitle(_save.SlotLabels[_save.ChosenSlot]), "YES");
                Add(SaveRowKind.No, -1, "KEEP IT", "NO");
                break;
        }
    }

    private static string SlotTitle(ComputerSaveSlot slot) =>
        string.Create(CultureInfo.InvariantCulture, $"SLOT 0{slot.Slot + 1}   {(slot.IsFree ? "UNUSED" : slot.Info.ToUpperInvariant())}");

    private void DrawSave(UiPainter painter, LayoutContext context, UiFont body, UiFont label, UiRect card, float pad, float y)
    {
        float width = card.Width - (pad * 2f);
        string heading = _save.State switch
        {
            ComputerSaveState.Upload => "UPLOAD TO WHICH SLOT",
            ComputerSaveState.Download => "DOWNLOAD FROM WHICH SLOT",
            ComputerSaveState.Overwrite => "THAT SLOT HOLDS A SAVE",
            ComputerSaveState.Uploading => "UPLOADING",
            ComputerSaveState.Complete => "UPLOAD COMPLETE",
            _ => "IGI HQ UPLINK",
        };
        UiVectors.DrawTracked(painter, label, heading, new UiVec2(card.X + pad, y), context.Px(14f), TextMuted, context.Px(2f));
        DrawBattery(painter, context, label, new UiVec2(card.Right - pad, y));

        if (_save.State is ComputerSaveState.Uploading or ComputerSaveState.Complete)
        {
            // The upload's bar, filling as the original's does over its hundred ticks.
            float barY = y + context.Px(48f);
            var track = new UiRect(card.X + pad, barY, width, context.Px(8f));
            painter.FillRect(track, UiPalette.White(0.08f));
            painter.FillRect(track with { Width = width * Math.Clamp(_save.UploadProgress, 0f, 1f) }, Signal);
            string count = _save.State == ComputerSaveState.Complete
                ? "COMPLETE"
                : string.Create(CultureInfo.InvariantCulture, $"{(int)(_save.UploadProgress * 100f)}%");
            UiVectors.DrawTracked(painter, label, count, new UiVec2(card.X + pad, barY + context.Px(22f)), context.Px(16f), Text, context.Px(2f));
            return;
        }

        // Laid out again on the card as it is drawn, which rises a little as the display comes up.
        LayOutSaveRows(context, card);
        for (int i = 0; i < _saveRows.Count; i++)
        {
            SaveRow row = _saveRows[i];
            UiRect rect = row.Rect;
            bool hovered = i == _hoverSaveRow;
            painter.FillRect(rect, hovered ? UiPalette.AccentAt(0.13f) : UiPalette.White(0.05f));
            painter.FillRect(new UiRect(rect.X, rect.Y, context.Px(3f), rect.Height), hovered ? Accent : row.Dimmed ? Border : SignalDim);
            Bgra32 colour = row.Dimmed ? TextMuted : hovered ? UiPalette.AccentLight : Text;
            UiVectors.DrawTracked(painter, label, row.Title, new UiVec2(rect.X + context.Px(20f), rect.Y + context.Px(17f)),
                context.Px(16f), colour, context.Px(2f));
            if (row.Detail.Length > 0)
            {
                float size = context.Px(12f), tracking = context.Px(1.6f);
                float detailWidth = UiVectors.MeasureTracked(label, row.Detail, size, tracking);
                UiVectors.DrawTracked(painter, label, row.Detail, new UiVec2(rect.Right - context.Px(20f) - detailWidth, rect.Y + context.Px(20f)),
                    size, TextMuted, tracking);
            }
        }
    }

    // The battery a save is made on: its charge as a gauge, right-aligned on the heading's line.
    private void DrawBattery(UiPainter painter, LayoutContext context, UiFont label, UiVec2 rightTop)
    {
        float charge = Math.Clamp(_save.Battery, 0f, 1f);
        bool flat = charge <= 0f;
        float gaugeWidth = context.Px(120f), gaugeHeight = context.Px(10f);
        var gauge = new UiRect(rightTop.X - gaugeWidth, rightTop.Y + context.Px(3f), gaugeWidth, gaugeHeight);
        painter.FillRect(gauge, UiPalette.White(0.08f));
        painter.FillRect(gauge with { Width = gaugeWidth * charge }, charge > 0.34f ? Complete : Hostile);
        painter.StrokeRect(gauge, MathF.Max(1f, context.Px(1f)), Border);

        string text = flat ? "BATTERY LOW" : "BATTERY";
        float size = context.Px(11f), tracking = context.Px(1.8f);
        float textWidth = UiVectors.MeasureTracked(label, text, size, tracking);
        UiVectors.DrawTracked(painter, label, text, new UiVec2(gauge.X - context.Px(12f) - textWidth, rightTop.Y + context.Px(2f)),
            size, flat ? Hostile : TextMuted, tracking);
    }
}
