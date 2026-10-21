using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>What the computer's save screen does to the player's saves.</summary>
public interface IIgi2ComputerSaves
{
    /// <summary>Gets the battery's charge as a share, <c>HumanPlayer_GetBatteryLevel</c>.</summary>
    float BatteryLevel { get; }

    /// <summary>Gets a value indicating whether a save may be uploaded now, <c>HumanPlayer_IsSaveAllowed</c>.</summary>
    bool IsSaveAllowed { get; }

    /// <summary>Gets the slot the quick load reads, one past it, nought for none.</summary>
    int QuickLoadSlot { get; }

    /// <summary>A slot as the screen names it.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>Whether it holds a save, its mission time and when it was written.</returns>
    (bool IsUsed, string Info, string When) Slot(int slot);

    /// <summary>A save is started, <c>HumanPlayer_BeginSaveToSlot</c>.</summary>
    /// <param name="slot">The slot.</param>
    void BeginSave(int slot);

    /// <summary>The save is finished, <c>HumanPlayer_FinishSaveToSlot</c>.</summary>
    /// <param name="slot">The slot.</param>
    void FinishSave(int slot);

    /// <summary>A load is asked for, <c>HumanPlayer_LoadFromSlot</c>.</summary>
    /// <param name="slot">The slot.</param>
    void Load(int slot);

    /// <summary>The click that chose a slot is kept from firing the gun, the input's <c>isFreezeFire</c>.</summary>
    void FreezeFire();

    /// <summary>The computer is asked to close, <c>HumanPlayer_QuitComputer</c>.</summary>
    void QuitComputer();
}

/// <summary>The save screen's pages, <c>ComputerSaveMenu</c>'s states.</summary>
public enum Igi2ComputerSaveState
{
    /// <summary>None asked for.</summary>
    None = 0,

    /// <summary>The download and upload choices.</summary>
    Main = 1,

    /// <summary>A slot to upload to.</summary>
    Upload = 2,

    /// <summary>A slot to download from.</summary>
    Download = 3,

    /// <summary>Whether to overwrite the slot chosen.</summary>
    Overwrite = 4,

    /// <summary>The upload under way.</summary>
    Uploading = 5,

    /// <summary>The upload done.</summary>
    Complete = 6,
}

/// <summary>One of the save screen's five slots, as it names it.</summary>
/// <param name="Slot">The slot.</param>
/// <param name="IsFree">Whether it holds no save.</param>
/// <param name="When">When its save was written, under its name.</param>
public readonly record struct Igi2ComputerSaveSlot(int Slot, bool IsFree, string When);

/// <content>
/// The save screen, <c>ComputerSaveMenu</c> (<c>computersavemenu.c</c>): IGI HQ's download and upload, the five slots, the overwrite
/// question, the upload's progress, and the battery a save uses.
/// </content>
public sealed partial class Igi2ComputerDisplay
{
    /// <summary>The save screen's slots.</summary>
    public const int SaveSlots = 5;

    /// <summary>The upload's progress a tick.</summary>
    public const float UploadStep = 0.009999999776482582f;

    /// <summary>The ticks the finished upload shows before the computer closes.</summary>
    public const int CompleteTicks = 30;

    private readonly Igi2ComputerSaveSlot[] _saveSlots = new Igi2ComputerSaveSlot[SaveSlots];
    private Igi2ComputerSaveState _saveRequest;

    /// <summary>Gets or sets what the save screen saves with, or <see langword="null"/> with no saves.</summary>
    public IIgi2ComputerSaves? Saves { get; set; }

    /// <summary>Gets the save screen's page.</summary>
    public Igi2ComputerSaveState SaveState { get; private set; }

    /// <summary>Gets the slot chosen.</summary>
    public int ChosenSlot { get; private set; }

    /// <summary>Gets the upload's progress, nought to one.</summary>
    public float UploadProgress { get; private set; }

    /// <summary>Gets the battery's charge as the screen last read it.</summary>
    public float Battery { get; private set; }

    /// <summary>Gets the line under the screen's title: what the slots are for.</summary>
    public string SaveSubtitle { get; private set; } = string.Empty;

    /// <summary>Gets the slots as the page shows them, by slot; only the page's own are meaningful.</summary>
    public IReadOnlyList<Igi2ComputerSaveSlot> SaveSlotLabels => _saveSlots;

    // ComputerSaveMenu_CreateHandler: the menu, the battery read, and the first page asked for.
    private void CreateSave()
    {
        CreateScreenMenuItems(Igi2ComputerMenu.Save);
        SaveState = Igi2ComputerSaveState.None;
        _saveRequest = Igi2ComputerSaveState.Main;
        Battery = Saves?.BatteryLevel ?? 0f;
        SaveSubtitle = string.Empty;
    }

    /// <summary>
    /// The save screen's own run, <c>ComputerSaveMenu_RunHandler</c>, after its menu's: the upload's progress, the finished upload's
    /// count down to the save and the computer's closing, and a page asked for replacing the shown one.
    /// </summary>
    private void RunSave()
    {
        if (SaveState == Igi2ComputerSaveState.Uploading)
        {
            UploadProgress = ((Fpu)UploadProgress + UploadStep).F;
            if (UploadProgress >= 1f)
            {
                UploadProgress = 1f;
                _saveRequest = Igi2ComputerSaveState.Complete;
            }
        }

        if (SaveState == Igi2ComputerSaveState.Complete)
        {
            if (_completeTicks <= 0)
            {
                Saves?.FinishSave(ChosenSlot);
                Saves?.QuitComputer();
            }
            else
            {
                _completeTicks--;
            }
        }

        if (_saveRequest != Igi2ComputerSaveState.None)
        {
            EnterSaveState();
        }
    }

    private int _completeTicks;

    // The page asked for: its items made afresh, the click spent.
    private void EnterSaveState()
    {
        EdgeButtons = Igi2ComputerButtons.None;
        EdgeReleaseButtons = Igi2ComputerButtons.None;
        Items = new Igi2ComputerMenuItems();
        SaveState = _saveRequest;
        _saveRequest = Igi2ComputerSaveState.None;
        switch (SaveState)
        {
            case Igi2ComputerSaveState.Upload or Igi2ComputerSaveState.Download:
                EnterSlots();
                break;
            case Igi2ComputerSaveState.Uploading:
                UploadProgress = 0f;
                break;
            case Igi2ComputerSaveState.Complete:
                _completeTicks = CompleteTicks;
                Saves?.BeginSave(ChosenSlot);
                Battery = Saves?.BatteryLevel ?? 0f;
                break;
            case Igi2ComputerSaveState.Overwrite:
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
        SaveSubtitle = Text(SaveState == Igi2ComputerSaveState.Upload ? "computer/Upload mission data to IGI H.Q" : "computer/Download mission data from IGI H.Q");
        bool chosen = false;
        for (int slot = 0; slot < SaveSlots; slot++)
        {
            NewSaveSlot(slot, isEnabled: true);
            if (!chosen && SaveState == Igi2ComputerSaveState.Upload && _saveSlots[slot].IsFree)
            {
                ChosenSlot = slot;
                _saveRequest = Igi2ComputerSaveState.Uploading;
                chosen = true;
            }
        }

        int quick = Saves?.QuickLoadSlot ?? 0;
        Items.Activate(quick != 0 ? quick + 9 : 7);
    }

    // ComputerSaveMenu_NewSaveSlot: a slot's line, its save's mission time or Unused, with when it was written under it; each
    // slot leads up and left to the one above (the first to the cancel), down and right to the one below (the last to the cancel).
    private void NewSaveSlot(int slot, bool isEnabled)
    {
        (bool used, string info, string when) = Saves?.Slot(slot) ?? (false, string.Empty, string.Empty);
        _saveSlots[slot] = new Igi2ComputerSaveSlot(slot, !used, used ? when : string.Empty);
        int up = slot == 0 ? 7 : slot + 9, down = slot == 4 ? 7 : slot + 11;
        Igi2ComputerMenuItem item = SaveTextItem(
            slot + 10, up, down, up, down, ((Fpu)Width * 0.15625).F, (((Fpu)slot * 0.0729166641831398) + 0.25).F * Height,
            used ? info : Text("computer/Unused"), TextFont, SaveSlotActivated);
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
        bool allowed = Saves?.IsSaveAllowed ?? false;
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
        if (!Clicked(item, activation))
        {
            return;
        }

        if (Battery > 0f)
        {
            PlaySound?.Invoke("mapcomp_01");
            _saveRequest = Igi2ComputerSaveState.Upload;
        }
        else
        {
            PlaySound?.Invoke("mapcomp_06");
        }
    }

    // ComputerSaveMenu_DownloadItemActivateCB.
    private void SaveDownloadActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_01");
            _saveRequest = Igi2ComputerSaveState.Download;
        }
    }

    // ComputerSaveMenu_CancelItemActivateCB.
    private void SaveCancelActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_02");
            _saveRequest = Igi2ComputerSaveState.Main;
        }
    }

    // ComputerSaveMenu_YesItemActivateCB.
    private void SaveYesActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_01");
            _saveRequest = Igi2ComputerSaveState.Uploading;
        }
    }

    // ComputerSaveMenu_NoItemActivateCB.
    private void SaveNoActivated(Igi2ComputerMenuItem item, Igi2ComputerActivation activation)
    {
        if (Clicked(item, activation))
        {
            PlaySound?.Invoke("mapcomp_02");
            _saveRequest = Igi2ComputerSaveState.Upload;
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

        Saves?.FreezeFire();
        int slot = item.Id - 10;
        bool free = slot is >= 0 and < SaveSlots && _saveSlots[slot].IsFree;
        if (SaveState == Igi2ComputerSaveState.Upload)
        {
            PlaySound?.Invoke("mapcomp_01");
            ChosenSlot = slot;
            _saveRequest = free ? Igi2ComputerSaveState.Uploading : Igi2ComputerSaveState.Overwrite;
        }

        if (SaveState == Igi2ComputerSaveState.Download)
        {
            if (!free)
            {
                PlaySound?.Invoke("mapcomp_01");
                Saves?.Load(slot);
                Saves?.QuitComputer();
            }
            else
            {
                PlaySound?.Invoke("mapcomp_06");
            }
        }
    }
}
