using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Computer;
using OpenIGI.Input;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.UI.Layout;

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

    // The battery's charge as an upload began and as it will stand once that save is written: the gauge runs from the one
    // to the other as the upload does, since the charge itself is only spent after the page has gone.
    private ComputerSaveState _saveStateShown;
    private float _batteryFrom;
    private float _batteryTo;

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
    private void UpdateSave(InputState input, float step, in TacticalComputerChrome chrome)
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

        if (_save.State == ComputerSaveState.Uploading && _saveStateShown != ComputerSaveState.Uploading)
        {
            _batteryFrom = Saves?.BatteryLevel ?? 0f;
            _batteryTo = Saves?.BatteryAfterSave ?? 0f;
            PlaySound?.Invoke(StaticSound);
        }

        _saveStateShown = _save.State;

        LayOutSaveRows(chrome);
        int hoverBefore = _hoverSaveRow;
        _hoverSaveRow = -1;
        for (int i = 0; i < _saveRows.Count; i++)
        {
            if (TacticalComputerChrome.Contains(_saveRows[i].Rect, _mouseX, _mouseY))
            {
                _hoverSaveRow = i;
            }
        }

        // A row newly under the pointer clicks, and a row chosen sounds, as the original's items do.
        if (_hoverSaveRow >= 0 && _hoverSaveRow != hoverBefore && !_saveRows[_hoverSaveRow].Dimmed)
        {
            PlaySound?.Invoke(HoverSound);
        }

        if (_hoverSaveRow < 0 || !input.Mouse.WasPressed(MouseButton.Left))
        {
            return;
        }

        PlaySound?.Invoke(ClickSound);

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

    // The rows the shown page offers, down the panel from under its heading.
    private void LayOutSaveRows(in TacticalComputerChrome chrome)
    {
        _saveRows.Clear();
        UiRect card = chrome.Page;
        float pad = PagePad(chrome);
        float x = card.X + pad, width = card.Width - (pad * 2f);
        float y = PageBodyTop(chrome);
        float height = chrome.Line + chrome.Px(16f), gap = chrome.Px(6f);

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

    // The page's name: what the uplink is doing.
    private string SaveHeading() => _save.State switch
    {
        ComputerSaveState.Upload => "UPLOAD TO WHICH SLOT",
        ComputerSaveState.Download => "DOWNLOAD FROM WHICH SLOT",
        ComputerSaveState.Overwrite => "THAT SLOT HOLDS A SAVE",
        ComputerSaveState.Uploading => "UPLOADING",
        ComputerSaveState.Complete => "UPLOAD COMPLETE",
        _ => "IGI HQ UPLINK",
    };

    private void DrawSave(ComputerCanvas canvas, UiRect card, float pad, float y)
    {
        HudPalette palette = canvas.Palette;
        ComputerText text = canvas.Text;
        float width = card.Width - (pad * 2f);

        if (_save.State is ComputerSaveState.Uploading or ComputerSaveState.Complete)
        {
            // The upload's bar, filling as the original's does over its hundred ticks.
            float progress = Math.Clamp(_save.UploadProgress, 0f, 1f);
            var track = new UiRect(card.X + pad, y + canvas.Px(8f), width, MathF.Max(4f, canvas.Px(8f)));
            canvas.Painter.FillRect(track, ComputerCanvas.Fade(palette.Primary, 0.14f));
            canvas.Painter.FillRect(track with { Width = MathF.Round(width * progress) }, palette.Primary);
            canvas.Painter.StrokeRect(track, canvas.Hairline, ComputerCanvas.Fade(palette.Primary, 0.6f));
            string count = _save.State == ComputerSaveState.Complete
                ? "COMPLETE"
                : string.Create(CultureInfo.InvariantCulture, $"{(int)(progress * 100f)}%");
            text.Draw(count, card.X + pad, track.Bottom + canvas.Px(10f), palette.Highlight);
            return;
        }

        float bar = MathF.Max(2f, MathF.Round(canvas.Px(2.5f)));
        LayOutSaveRows(canvas.Chrome);
        for (int i = 0; i < _saveRows.Count; i++)
        {
            SaveRow row = _saveRows[i];
            UiRect rect = row.Rect;
            bool hovered = i == _hoverSaveRow;
            canvas.Painter.FillRect(rect, ComputerCanvas.Fade(palette.Primary, hovered ? 0.2f : 0.06f));
            canvas.Painter.FillRect(
                new UiRect(rect.X, rect.Y, bar, rect.Height),
                hovered ? palette.Highlight : row.Dimmed ? ComputerCanvas.Fade(canvas.Quiet, 0.6f) : palette.Primary);

            Bgra32 ink = row.Dimmed ? canvas.Quiet : hovered ? palette.Highlight : palette.Primary;
            float line = rect.Y + ((rect.Height - text.LineHeight) * 0.5f);
            text.Draw(text.Capitals(row.Title), rect.X + bar + canvas.Px(12f), line, ink);
            if (row.Detail.Length > 0)
            {
                text.DrawRight(text.Capitals(row.Detail), rect.Right - canvas.Px(10f), line, hovered ? palette.Highlight : canvas.Quiet);
            }
        }
    }

    // The battery a save is made on: its charge as a gauge, right-aligned on the heading's line.
    private void DrawBattery(ComputerCanvas canvas, float right, float y)
    {
        HudPalette palette = canvas.Palette;
        // The charge as it stands now, not as it stood when the page came up; through an upload, running down to what
        // that save leaves.
        float live = _save.State is ComputerSaveState.Uploading or ComputerSaveState.Complete
            ? _batteryFrom + ((_batteryTo - _batteryFrom) * Math.Clamp(_save.UploadProgress, 0f, 1f))
            : Saves?.BatteryLevel ?? _save.Battery;
        float charge = Math.Clamp(live, 0f, 1f);
        bool flat = charge <= 0f;
        float gaugeWidth = canvas.Px(90f), gaugeHeight = MathF.Max(4f, canvas.Px(8f));
        var gauge = new UiRect(right - gaugeWidth, MathF.Round(y + ((canvas.Text.LineHeight - gaugeHeight) * 0.5f)), gaugeWidth, gaugeHeight);
        canvas.Painter.FillRect(gauge, ComputerCanvas.Fade(palette.Primary, 0.14f));
        canvas.Painter.FillRect(gauge with { Width = MathF.Round(gaugeWidth * charge) }, charge > 0.34f ? palette.Primary : palette.Danger);
        canvas.Painter.StrokeRect(gauge, canvas.Hairline, ComputerCanvas.Fade(palette.Primary, 0.6f));
        canvas.Text.DrawRight(flat ? "BATTERY LOW" : "BATTERY", gauge.X - canvas.Px(10f), y, flat ? palette.Danger : canvas.Quiet);
    }
}
