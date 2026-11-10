namespace OpenIGI.Game.Computer;

/// <summary>What a computer's save screen does to the player's saves.</summary>
public interface IComputerSaves
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
public enum ComputerSaveState
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
/// <param name="Info">Its save's mission time, or empty.</param>
/// <param name="When">When its save was written, or empty.</param>
public readonly record struct ComputerSaveSlot(int Slot, bool IsFree, string Info, string When);

/// <summary>What choosing a slot came to.</summary>
public enum ComputerSaveChoice
{
    /// <summary>Nothing: no slot page is up.</summary>
    None,

    /// <summary>A free slot is being uploaded to.</summary>
    Upload,

    /// <summary>A used slot, whose overwriting is asked about.</summary>
    Overwrite,

    /// <summary>A used slot is being downloaded and the computer closed.</summary>
    Download,

    /// <summary>A free slot, which holds nothing to download.</summary>
    Empty,
}

/// <summary>
/// The save screen of a computer, <c>ComputerSaveMenu</c> (I.G.I. 2's <c>computersavemenu.c</c>), without its items: IGI HQ's
/// download and upload, the five slots, the overwrite question, the upload's progress and the battery a save uses. Every computer
/// that offers saves runs this one and lays its own controls over it.
/// </summary>
public sealed class ComputerSaveFlow
{
    /// <summary>The save screen's slots.</summary>
    public const int Slots = 5;

    /// <summary>The upload's progress a tick.</summary>
    public const float UploadStep = 0.009999999776482582f;

    /// <summary>The ticks the finished upload shows before the computer closes.</summary>
    public const int CompleteTicks = 30;

    private readonly ComputerSaveSlot[] _slots = new ComputerSaveSlot[Slots];
    private ComputerSaveState _request;
    private int _completeTicks;
    private bool _finished;

    /// <summary>Gets or sets what the screen saves with, or <see langword="null"/> with no saves.</summary>
    public IComputerSaves? Saves { get; set; }

    /// <summary>Gets the page showing.</summary>
    public ComputerSaveState State { get; private set; }

    /// <summary>Gets the slot chosen.</summary>
    public int ChosenSlot { get; private set; }

    /// <summary>Gets the upload's progress, nought to one.</summary>
    public float UploadProgress { get; private set; }

    /// <summary>Gets the battery's charge as the screen last read it.</summary>
    public float Battery { get; private set; }

    /// <summary>Gets the slots as the slot pages last read them, by slot.</summary>
    public IReadOnlyList<ComputerSaveSlot> SlotLabels => _slots;

    /// <summary>Gets a value indicating whether an upload is offered at all.</summary>
    public bool IsSaveAllowed => Saves?.IsSaveAllowed ?? false;

    /// <summary>Gets the slot the quick load reads, one past it, nought for none.</summary>
    public int QuickLoadSlot => Saves?.QuickLoadSlot ?? 0;

    /// <summary>The screen comes up, <c>ComputerSaveMenu_CreateHandler</c>: the battery read, the first page asked for.</summary>
    public void Open()
    {
        State = ComputerSaveState.None;
        _request = ComputerSaveState.Main;
        _finished = false;
        Battery = Saves?.BatteryLevel ?? 0f;
    }

    /// <summary>
    /// Runs it a tick, <c>ComputerSaveMenu_RunHandler</c>: the upload's progress, the finished upload's count down to the save and
    /// the computer's closing, and a page asked for replacing the shown one.
    /// </summary>
    /// <returns><see langword="true"/> when another page came up, whose controls are to be made.</returns>
    public bool Run()
    {
        if (State == ComputerSaveState.Uploading)
        {
            UploadProgress = (float)((double)UploadProgress + UploadStep);
            if (UploadProgress >= 1f)
            {
                UploadProgress = 1f;
                _request = ComputerSaveState.Complete;
            }
        }

        if (State == ComputerSaveState.Complete)
        {
            if (_completeTicks <= 0)
            {
                // Once: the original's screen is gone with its computer by the next tick, and a display that takes a moment
                // to close must not save again.
                if (!_finished)
                {
                    _finished = true;
                    Saves?.FinishSave(ChosenSlot);
                    Saves?.QuitComputer();
                }
            }
            else
            {
                _completeTicks--;
            }
        }

        if (_request == ComputerSaveState.None)
        {
            return false;
        }

        State = _request;
        _request = ComputerSaveState.None;
        switch (State)
        {
            case ComputerSaveState.Upload or ComputerSaveState.Download:
                ReadSlots();
                break;
            case ComputerSaveState.Uploading:
                UploadProgress = 0f;
                break;
            case ComputerSaveState.Complete:
                _completeTicks = CompleteTicks;
                Saves?.BeginSave(ChosenSlot);
                Battery = Saves?.BatteryLevel ?? 0f;
                break;
        }

        return true;
    }

    /// <summary>The upload is clicked, <c>ComputerSaveMenu_UploadItemActivateCB</c>: with charge left, on to the slots.</summary>
    /// <returns><see langword="false"/> with the battery flat.</returns>
    public bool Upload()
    {
        if (Battery > 0f)
        {
            _request = ComputerSaveState.Upload;
            return true;
        }

        return false;
    }

    /// <summary>The download is clicked, <c>ComputerSaveMenu_DownloadItemActivateCB</c>.</summary>
    public void Download() => _request = ComputerSaveState.Download;

    /// <summary>The slots are left, <c>ComputerSaveMenu_CancelItemActivateCB</c>.</summary>
    public void Cancel() => _request = ComputerSaveState.Main;

    /// <summary>The chosen slot is overwritten, <c>ComputerSaveMenu_YesItemActivateCB</c>.</summary>
    public void Overwrite() => _request = ComputerSaveState.Uploading;

    /// <summary>The chosen slot is kept, <c>ComputerSaveMenu_NoItemActivateCB</c>.</summary>
    public void Keep() => _request = ComputerSaveState.Upload;

    /// <summary>
    /// A slot is clicked, <c>ComputerSaveMenu_SlotItemActivateCB</c>: the click kept from the gun; uploading, a free slot is saved
    /// to and a used one asked about; downloading, a used slot is loaded and the computer closed.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>What it came to.</returns>
    public ComputerSaveChoice Choose(int slot)
    {
        Saves?.FreezeFire();
        bool free = slot is >= 0 and < Slots && _slots[slot].IsFree;
        if (State == ComputerSaveState.Upload)
        {
            ChosenSlot = slot;
            _request = free ? ComputerSaveState.Uploading : ComputerSaveState.Overwrite;
            return free ? ComputerSaveChoice.Upload : ComputerSaveChoice.Overwrite;
        }

        if (State == ComputerSaveState.Download)
        {
            if (free)
            {
                return ComputerSaveChoice.Empty;
            }

            Saves?.Load(slot);
            Saves?.QuitComputer();
            return ComputerSaveChoice.Download;
        }

        return ComputerSaveChoice.None;
    }

    // The slots as a slot page shows them; an upload goes straight to the first free one.
    private void ReadSlots()
    {
        bool chosen = false;
        for (int slot = 0; slot < Slots; slot++)
        {
            (bool used, string info, string when) = Saves?.Slot(slot) ?? (false, string.Empty, string.Empty);
            _slots[slot] = new ComputerSaveSlot(slot, !used, used ? info : string.Empty, used ? when : string.Empty);
            if (!chosen && State == ComputerSaveState.Upload && !used)
            {
                ChosenSlot = slot;
                _request = ComputerSaveState.Uploading;
                chosen = true;
            }
        }
    }
}
