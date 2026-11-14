using System.Globalization;
using OpenIGI.Game.Computer;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>
/// The save page. Project I.G.I. shipped with no save games and its computer with no such screen; this is I.G.I. 2's
/// <c>ComputerSaveMenu</c> (<see cref="ComputerSaveFlow"/>) laid out in this computer's own links and fonts: IGI HQ's download and
/// upload, the five slots, the overwrite question, the upload's bar and the battery a save uses.
/// </summary>
public sealed class ComputerSavePage : ComputerPage
{
    /// <summary>The id of the link to this page on the computer's other screens.</summary>
    public const int TabId = 20;

    /// <summary>How far up the display the link to this page stands, one line above the others.</summary>
    public const float TabY = 0.74166667f;

    private const int UploadId = 5;
    private const int DownloadId = 6;
    private const int CancelId = 7;
    private const int YesId = 8;
    private const int NoId = 9;
    private const int FirstSlotId = 10;

    private readonly ComputerSaveFlow _flow = new();

    /// <summary>Initializes a new instance of the <see cref="ComputerSavePage"/> class.</summary>
    /// <param name="computer">The computer.</param>
    public ComputerSavePage(Igi1Computer computer)
        : base(computer)
    {
        float w = computer.Width, h = computer.Height;
        if (computer.MapEnabled)
        {
            computer.AddTab(1, w * 0.1f, h * 0.80833334f, "MAP", OnTab);
        }

        computer.AddTab(2, w * 0.1f, h * 0.775f, "OBJECTIVES", OnTab);
        computer.AddTab(3, w * 0.1f, h * TabY, "LOG", OnTab);
        _flow.Saves = computer.Saves;
        _flow.Open();
    }

    /// <inheritdoc/>
    public override ComputerScreenKind Kind => ComputerScreenKind.Save;

    /// <summary>Gets the page's state.</summary>
    public ComputerSaveState State => _flow.State;

    /// <summary>
    /// Gets the upload's bar as the last tick left it -- its left, top, width and height, display pixels -- or
    /// <see langword="null"/> with no upload under way.
    /// </summary>
    public (float X, float Y, float Width, float Height)? UploadBar { get; private set; }

    /// <inheritdoc/>
    protected override void Layout()
    {
        if (_flow.Run())
        {
            MakeItems();
        }

        float w = Computer.Width, h = Computer.Height;
        int x = RoundInt(w * 0.15);
        IComputerFonts fonts = Computer.World.Fonts ?? ComputerTextWrap.Estimated;
        Word(x, RoundInt(h * 0.1), Computer.Text("IGI HQ"), Igi1Computer.TabFont, 2);

        // The battery, at the right of the heading's line.
        bool flat = _flow.Battery <= 0f;
        string battery = flat
            ? Computer.Text("BATTERY LOW")
            : string.Create(CultureInfo.InvariantCulture, $"{Computer.Text("BATTERY LEVEL")}: {RoundInt(_flow.Battery * 100.0)}%");
        Word(RoundInt(w * 0.875) - fonts.StringWidth(Igi1Computer.TextFont, battery), RoundInt(h * 0.1), battery, Igi1Computer.TextFont, flat ? 0 : 1);

        UploadBar = null;
        switch (_flow.State)
        {
            case ComputerSaveState.Upload or ComputerSaveState.Download:
                Word(x, RoundInt(h * 0.1875), Computer.Text(Subtitle), Igi1Computer.TabFont, 2);
                foreach (ComputerSaveSlot slot in _flow.SlotLabels)
                {
                    WhenUnder(slot, fonts);
                }

                break;
            case ComputerSaveState.Overwrite:
                ComputerSaveSlot chosen = _flow.SlotLabels[_flow.ChosenSlot];
                Word(x, SlotY(chosen.Slot), SlotText(chosen), Igi1Computer.TextFont, 1);
                WhenUnder(chosen, fonts);
                Word(_questionX, SlotY(chosen.Slot), Computer.Text("OVERWRITE?"), Igi1Computer.TextFont, 0);
                Word(_slashX, SlotY(chosen.Slot), "/", Igi1Computer.TextFont, 1);
                break;
            case ComputerSaveState.Uploading or ComputerSaveState.Complete:
                float progress = _flow.UploadProgress;
                UploadBar = (w * 0.15625f, h * 0.7916667f, progress * 0.65625f * w, h * 0.016666668f);
                string count = _flow.State == ComputerSaveState.Complete
                    ? Computer.Text("COMPLETE")
                    : string.Create(CultureInfo.InvariantCulture, $"{RoundInt(progress * 100.0)}%");
                int line = RoundInt((h * 0.7916667) - fonts.LineHeight(Igi1Computer.TextFont) - 2.0);
                string uploading = Computer.Text("UPLOADING") + ":";
                Word(RoundInt(w * 0.15625), line, uploading, Igi1Computer.TextFont, 1);
                Word(RoundInt(w * 0.15625) + fonts.StringWidth(Igi1Computer.TextFont, uploading + " "), line, count, Igi1Computer.TextFont, 3);
                break;
        }
    }

    private int _questionX;
    private int _slashX;

    private string Subtitle => _flow.State == ComputerSaveState.Upload ? "UPLOAD MISSION DATA TO IGI HQ" : "DOWNLOAD MISSION DATA FROM IGI HQ";

    private int SlotY(int slot) => RoundInt(((slot * 0.0729166641831398) + 0.25) * Computer.Height);

    private string SlotText(ComputerSaveSlot slot) => slot.IsFree ? Computer.Text("UNUSED") : slot.Info.ToUpperInvariant();

    private void Word(int x, int y, string text, string font, int color) => Words.Add(new ComputerPageWord(text, font, x, y, color));

    // ComputerSaveMenu_SaveSlotDrawHandler: a used slot's date under its line.
    private void WhenUnder(ComputerSaveSlot slot, IComputerFonts fonts)
    {
        if (slot.When.Length > 0)
        {
            Word(RoundInt(Computer.Width * 0.15), SlotY(slot.Slot) + fonts.LineHeight(Igi1Computer.TextFont), slot.When, Igi1Computer.LogFont, 2);
        }
    }

    // The page that came up: the links of the last one go, and its own are made.
    private void MakeItems()
    {
        // An upload under way has no links at all, as the original's screen has none while it runs.
        bool busy = _flow.State is ComputerSaveState.Uploading or ComputerSaveState.Complete;
        Computer.Panel.RemoveWhere(item => busy || item.Id >= UploadId);
        float w = Computer.Width, h = Computer.Height, x = RoundInt(w * 0.15);
        IComputerFonts fonts = Computer.World.Fonts ?? ComputerTextWrap.Estimated;
        switch (_flow.State)
        {
            case ComputerSaveState.Main:
                Computer.AddLink(DownloadId, x, h * 0.1875f, Computer.Text("DOWNLOAD MISSION DATA FROM IGI HQ"), Igi1Computer.TabFont, OnItem);
                if (_flow.IsSaveAllowed)
                {
                    Computer.AddLink(UploadId, x, h * 0.2625f, Computer.Text("UPLOAD MISSION DATA TO IGI HQ"), Igi1Computer.TabFont, OnItem);
                }

                break;
            case ComputerSaveState.Upload or ComputerSaveState.Download:
                foreach (ComputerSaveSlot slot in _flow.SlotLabels)
                {
                    Computer.AddLink(FirstSlotId + slot.Slot, x, SlotY(slot.Slot), SlotText(slot), Igi1Computer.TextFont, OnItem);
                }

                Computer.AddLink(CancelId, x, h * 0.6145833f, Computer.Text("CANCEL"), Igi1Computer.TabFont, OnItem);
                break;
            case ComputerSaveState.Overwrite:
                // Yes / No at the right end of the chosen slot's line, the question before them.
                string font = Igi1Computer.TextFont;
                string no = Computer.Text("NO"), yes = Computer.Text("YES");
                int gap = fonts.StringWidth(font, " ");
                int noX = RoundInt(w * 0.875) - fonts.StringWidth(font, no);
                _slashX = noX - gap - fonts.StringWidth(font, "/");
                int yesX = _slashX - gap - fonts.StringWidth(font, yes);
                _questionX = yesX - gap - fonts.StringWidth(font, Computer.Text("OVERWRITE?"));
                int y = SlotY(_flow.ChosenSlot);
                Computer.AddLink(YesId, yesX, y, yes, font, OnItem);
                Computer.AddLink(NoId, noX, y, no, font, OnItem);
                break;
        }
    }

    private void OnTab(ComputerItem item, int id)
    {
        if ((Computer.Pressed & Igi1Computer.Left) == 0)
        {
            return;
        }

        switch (id)
        {
            case 1:
                Computer.SetScreen(ComputerScreenKind.Map);
                break;
            case 2:
                Computer.SetScreen(ComputerScreenKind.Objectives);
                break;
            case 3:
                Computer.SetScreen(ComputerScreenKind.Log);
                break;
        }
    }

    // The page's own links: a press on the one under the pointer does its part.
    private void OnItem(ComputerItem item, int id)
    {
        if (item.Id != id || (Computer.Pressed & Igi1Computer.Left) == 0)
        {
            return;
        }

        switch (id)
        {
            case UploadId:
                _flow.Upload();
                break;
            case DownloadId:
                _flow.Download();
                break;
            case CancelId:
                _flow.Cancel();
                break;
            case YesId:
                _flow.Overwrite();
                break;
            case NoId:
                _flow.Keep();
                break;
            case >= FirstSlotId and < FirstSlotId + ComputerSaveFlow.Slots:
                _flow.Choose(id - FirstSlotId);
                break;
        }
    }
}
