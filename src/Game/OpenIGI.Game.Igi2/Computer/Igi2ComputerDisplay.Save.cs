using OpenIGI.Game.Computer;

namespace OpenIGI.Game.Igi2.Computer;

/// <content>
/// The save screen, <c>ComputerSaveMenu</c> (<c>computersavemenu.c</c>): IGI HQ's download and upload, the five slots, the overwrite
/// question, the upload's progress, and the battery a save uses.
/// </content>
public sealed partial class Igi2ComputerDisplay
{
    /// <summary>The save screen's slots.</summary>
    public const int SaveSlots = ComputerSaveFlow.Slots;

    /// <summary>The upload's progress a tick.</summary>
    public const float UploadStep = ComputerSaveFlow.UploadStep;

    /// <summary>The ticks the finished upload shows before the computer closes.</summary>
    public const int CompleteTicks = ComputerSaveFlow.CompleteTicks;

    // The screen's pages and what they do to the saves, the one every computer runs.
    private readonly ComputerSaveFlow _save = new();

    /// <summary>Gets or sets what the save screen saves with, or <see langword="null"/> with no saves.</summary>
    public IComputerSaves? Saves
    {
        get => _save.Saves;
        set => _save.Saves = value;
    }

    /// <summary>Gets the save screen's page.</summary>
    public ComputerSaveState SaveState => _save.State;

    /// <summary>Gets the slot chosen.</summary>
    public int ChosenSlot => _save.ChosenSlot;

    /// <summary>Gets the upload's progress, nought to one.</summary>
    public float UploadProgress => _save.UploadProgress;

    /// <summary>Gets the battery's charge as the screen last read it.</summary>
    public float Battery => _save.Battery;

    /// <summary>Gets the line under the screen's title: what the slots are for.</summary>
    public string SaveSubtitle { get; private set; } = string.Empty;

    /// <summary>Gets the slots as the page shows them, by slot; only the page's own are meaningful.</summary>
    public IReadOnlyList<ComputerSaveSlot> SaveSlotLabels => _save.SlotLabels;

    // ComputerSaveMenu_CreateHandler: the menu, the battery read, and the first page asked for.
    private void CreateSave()
    {
        CreateScreenMenuItems(Igi2ComputerMenu.Save);
        _save.Open();
        SaveSubtitle = string.Empty;
    }

    /// <summary>
    /// The save screen's own run, <c>ComputerSaveMenu_RunHandler</c>, after its menu's: the upload's progress, the finished upload's
    /// count down to the save and the computer's closing, and a page asked for replacing the shown one.
    /// </summary>
    private void RunSave()
    {
        if (_save.Run())
        {
            EnterSaveState();
        }
    }

    // The page that came up: its items made afresh, the click spent.
    private void EnterSaveState()
    {
        EdgeButtons = Igi2ComputerButtons.None;
        EdgeReleaseButtons = Igi2ComputerButtons.None;
        Items = new Igi2ComputerMenuItems();
        switch (SaveState)
        {
            case ComputerSaveState.Upload or ComputerSaveState.Download:
                EnterSlots();
                break;
            case ComputerSaveState.Uploading or ComputerSaveState.Complete:
                break;
            case ComputerSaveState.Overwrite:
                EnterOverwrite();
                break;
            default:
                EnterMain();
                break;
        }
    }

    // The slots to upload to or download from, under a cancel: an upload goes straight to the first free slot.
    private void EnterSlots()
    {
        float x = ((Fpu)Width * 0.15625).F;
        Items.Add(SaveTextItem(7, 14, 10, 14, 10, x, ((Fpu)Height * 0.6145833134651184).F, Text("computer/Cancel"), LabelFont, SaveCancelActivated));
        SaveSubtitle = Text(SaveState == ComputerSaveState.Upload ? "computer/Upload mission data to IGI H.Q" : "computer/Download mission data from IGI H.Q");
        for (int slot = 0; slot < SaveSlots; slot++)
        {
            NewSaveSlot(slot, isEnabled: true);
        }

        int quick = _save.QuickLoadSlot;
        Items.Activate(quick != 0 ? quick + 9 : 7);
    }

    // ComputerSaveMenu_NewSaveSlot: a slot's line, its save's mission time or Unused, with when it was written under it; each
    // slot leads up and left to the one above (the first to the cancel), down and right to the one below (the last to the cancel).
    private void NewSaveSlot(int slot, bool isEnabled)
    {
        ComputerSaveSlot label = _save.SlotLabels[slot];
        int up = slot == 0 ? 7 : slot + 9, down = slot == 4 ? 7 : slot + 11;
        Igi2ComputerMenuItem item = SaveTextItem(
            slot + 10, up, down, up, down, ((Fpu)Width * 0.15625).F, (((Fpu)slot * 0.0729166641831398) + 0.25).F * Height,
            label.IsFree ? Text("computer/Unused") : label.Info, TextFont, SaveSlotActivated);
        item.IsDisabled = !isEnabled;
        Items.Add(item);
    }

    // The overwrite question on the chosen slot's line: Yes / No at its right end, the question before them, the slot shown
    // without its item lit.
    private void EnterOverwrite()
    {
        float y = (((Fpu)ChosenSlot * 0.0729166641831398) + 0.25).F * Height;
        string no = Text("computer/No"), yes = Text("computer/Yes");
        float noX = (Width * 0.875f) - StringWidth(TextFont, no);
        float slashX = (int)(long)(noX - StringWidth(TextFont, SaveSlash));
        float yesX = slashX - StringWidth(TextFont, yes);
        Items.Add(SaveTextItem(9, 8, 8, 8, 8, noX, y, no, TextFont, SaveNoActivated));
        Items.Add(SaveTextItem(8, 9, 9, 9, 9, yesX, y, yes, TextFont, SaveYesActivated));
        OverwriteX = (int)(long)(yesX - StringWidth(TextFont, Text("computer/Overwrite?")) - StringWidth(TextFont, " "));
        SlashX = slashX;
        OverwriteY = (int)(long)y;
        NewSaveSlot(ChosenSlot, isEnabled: false);
        EdgeButtons = Igi2ComputerButtons.None;
        EdgeReleaseButtons = Igi2ComputerButtons.None;
        Items.Activate(8);
    }

    /// <summary>The separator between the overwrite question's answers.</summary>
    public const string SaveSlash = " / ";

    /// <summary>Gets where the overwrite question starts.</summary>
    public float OverwriteX { get; private set; }

    /// <summary>Gets where its separator is.</summary>
    public float SlashX { get; private set; }

    /// <summary>Gets the question's line.</summary>
    public float OverwriteY { get; private set; }

    // The first page: the menu, IGI HQ's download and, while a save may be made, its upload above it; the upload is lit.
    private void EnterMain()
    {
        CreateScreenMenuItems(Igi2ComputerMenu.Save);
        Igi2ComputerMenuItem? top = Items.Items.Count > 0 ? Items.Items[^1] : null;
        Igi2ComputerMenuItem? bottom = Items.Items.Count > 0 ? Items.Items[0] : null;
        bool allowed = _save.IsSaveAllowed;
        if (bottom is not null)
        {
            bottom.LinkDown = 6;
            bottom.LinkRight = 6;
        }

        if (top is not null)
        {
            top.LinkUp = allowed ? 5 : 6;
            top.LinkLeft = top.LinkUp;
        }

        int downward = allowed ? 5 : top?.Id ?? 0;
        float x = ((Fpu)Width * 0.15625).F;
        Items.Add(SaveTextItem(6, bottom?.Id ?? 0, downward, bottom?.Id ?? 0, downward, x, ((Fpu)Height * 0.1875).F,
            Text("computer/Download mission data from IGI H.Q"), LabelFont, SaveDownloadActivated));
        if (allowed)
        {
            Items.Add(SaveTextItem(5, 6, top?.Id ?? 0, 6, top?.Id ?? 0, x, ((Fpu)Height * 0.22291666269302368).F,
                Text("computer/Upload mission data to IGI H.Q"), LabelFont, SaveUploadActivated));
        }

        SaveSubtitle = string.Empty;
        EdgeButtons = Igi2ComputerButtons.None;
        EdgeReleaseButtons = Igi2ComputerButtons.None;
        Items.Activate(5);
    }

    private Igi2ComputerMenuItem SaveTextItem(
        int id, int up, int down, int left, int right, float x, float y, string text, string font, Action<Igi2ComputerMenuItem, Igi2ComputerActivation> activated)
    {
        Igi2ComputerMenuItem item = new(id, activated)
        {
            LinkUp = up,
            LinkDown = down,
            LinkLeft = left,
            LinkRight = right,
            X = x,
            Y = y,
            Text = text,
            Font = font,
            Colour = Igi2ComputerColour.Green,
            HighlightColour = Igi2ComputerColour.LightGreen,
            Width = StringWidth(font, text),
            Height = LineHeight(font),
        };
        return item;
    }

    // The save screen's callbacks: newly lit, an item clicks; clicked, it does its page's part.
    private bool Clicked(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (item.IsActivated && !item.IsActive)
        {
            PlaySound?.Invoke("mapcomp_05");
        }

        return (EdgeButtons & Igi2ComputerButtons.Select) != 0 && activation.MenuItem == item.Id;
    }

    // ComputerSaveMenu_UploadItemActivateCB: with charge left, to the slots; without, a buzz.
    private void SaveUploadActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            bool charged = _save.Upload();
            PlaySound?.Invoke(charged ? "mapcomp_01" : "mapcomp_06");
        }
    }

    // ComputerSaveMenu_DownloadItemActivateCB.
    private void SaveDownloadActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_01");
            _save.Download();
        }
    }

    // ComputerSaveMenu_CancelItemActivateCB.
    private void SaveCancelActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_02");
            _save.Cancel();
        }
    }

    // ComputerSaveMenu_YesItemActivateCB.
    private void SaveYesActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_01");
            _save.Overwrite();
        }
    }

    // ComputerSaveMenu_NoItemActivateCB.
    private void SaveNoActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_02");
            _save.Keep();
        }
    }

    // ComputerSaveMenu_SlotItemActivateCB: the click kept from the gun; uploading, a free slot is saved to and a used one asked
    // about; downloading, a used slot is loaded and the computer closed, a free one buzzes.
    private void SaveSlotActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (!Clicked(item, activation))
        {
            return;
        }

        switch (_save.Choose(item.Id - 10))
        {
            case ComputerSaveChoice.Upload or ComputerSaveChoice.Overwrite or ComputerSaveChoice.Download:
                PlaySound?.Invoke("mapcomp_01");
                break;
            case ComputerSaveChoice.Empty:
                PlaySound?.Invoke("mapcomp_06");
                break;
        }
    }
}
