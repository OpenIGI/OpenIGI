using OpenIGI.Core.Numerics;
using OpenIGI.Game.Config;

namespace OpenIGI.Game.Menus;

/// <summary>
/// The controls list's behaviour: a row per action, clicked to take the next key or button held.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from the <c>ControlsMenu</c> class (<c>0x4230A0</c>). It is made at 360 by 200 (<c>0x4232B0</c>), which its
/// frame lines up, and shows <see cref="VisibleRows"/> of its <see cref="RowCount"/> rows from <see cref="Offset"/>, which the
/// slider beside it scrolls.
/// </para>
/// <para>
/// Its update (<c>0x4234E0</c>) hit-tests its own rectangle. A click picks the row under the pointer, shows the prompt in its
/// key column and lets the manager's pointer and keys go (<c>0x418FC0</c>). Each tick after, Escape held cancels; otherwise the
/// first key or button in the table (<see cref="RetailInputKeys"/>) that is held is the row's, and a row that had it takes the
/// row's old one instead. Either way the queued input is thrown away (<c>0x48A8C0</c>) and the manager takes input again
/// (<c>0x418FD0</c>).
/// </para>
/// <para>
/// The rows are read from the profile as the screen takes its data in (<c>0x423DB0</c>) and written back as it commits
/// (<c>0x423EB0</c>), through the manager's <see cref="IMenuInputBindings"/>.
/// </para>
/// </remarks>
public partial class ControlsMenuTask
{
    /// <summary>The rows, one for each action Project I.G.I. binds (<c>0x4062B0</c>).</summary>
    public const int RowCount = 27;

    /// <summary>The rows shown at once.</summary>
    public const int VisibleRows = 10;

    /// <summary>The rows the list scrolls by: the slider's whole length.</summary>
    public const int ScrollRows = RowCount - VisibleRows;

    /// <summary>The height of a row.</summary>
    public const int RowHeight = 20;

    /// <summary>The width of the action column; the key column follows it.</summary>
    public const int LabelWidth = 210;

    /// <summary>The width of the key column.</summary>
    public const int KeyWidth = 150;

    /// <summary>The menu text a row's key column shows while it waits for a key.</summary>
    public const string Prompt = "Select a key or button";

    /// <summary>The font the rows are written in.</summary>
    public const string FontResource = "font1.fnt";

    /// <summary>The sound a row plays as it is clicked.</summary>
    public const string ChooseSoundName = "menu_sel2";

    /// <summary>The sound Escape plays as it cancels.</summary>
    public const string CancelSoundName = "menu_cancel";

    // The scan codes of Escape, which cancels, and of the two Windows keys, which are never taken.
    private const int EscapeCode = 0x01;
    private const int LeftWindowsCode = 0xDB;
    private const int RightWindowsCode = 0xDC;

    // The list's own stream, seeded as the list is made (0x4232B0 -> 0x4B4720), which picks the sound a key taken plays.
    private readonly IgiRandom _random = IgiRandom.Seeded();
    private readonly int[] _keys = [.. Enumerable.Repeat(-1, RowCount)];
    private int _pointerY;

    /// <summary>Initializes a new instance of the <see cref="ControlsMenuTask"/> class.</summary>
    public ControlsMenuTask()
    {
        LayoutX = 300;
        LayoutY = 230;
        LayoutWidth = LabelWidth + KeyWidth;
        LayoutHeight = VisibleRows * RowHeight;
    }

    /// <summary>Gets the actions, a row each, in the order of the original's table (<c>0x536558</c>).</summary>
    public static IReadOnlyList<string> Actions { get; } =
    [
        "MoveLeft", "MoveRight", "MoveUp", "MoveDown", "Fire", "AlternateFire", "Reload", "Activate", "Crouch", "Jump",
        "NextWeapon", "PrevWeapon", "Peek", "Binoculars", "MapComputer", "ToggleWalkRun", "ZoomIn", "ZoomOut",
        "WeaponCategory1", "WeaponCategory2", "WeaponCategory3", "WeaponCategory4", "WeaponCategory5", "WeaponCategory6",
        "WeaponCategory7", "WeaponCategory8", "WeaponCategory9",
    ];

    /// <summary>Gets the row waiting for a key, or -1 while none is.</summary>
    public int CapturingRow { get; private set; } = -1;

    /// <summary>Gets a value indicating whether a row is waiting for a key.</summary>
    public bool IsCapturing => CapturingRow >= 0;

    /// <inheritdoc/>
    protected override bool TakesPointer => true;

    /// <summary>Gets the key a row holds.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The key, or <see langword="null"/> when the row has none the table knows.</returns>
    public RetailInputKey? KeyOf(int row) =>
        row is >= 0 and < RowCount && _keys[row] >= 0 ? RetailInputKeys.All[_keys[row]] : null;

    /// <summary>Gets the menu text a row's key column shows: the prompt while it waits, else its key.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The text's key in the menu's strings, empty for a row with no key.</returns>
    public string KeyTextOf(int row) => row == CapturingRow ? Prompt : KeyOf(row)?.Name ?? string.Empty;

    /// <inheritdoc/>
    protected override void OnPointer(MenuPointer pointer, bool inside) => _pointerY = pointer.Y;

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        if (IsReset)
        {
            Reset();
            IsReset = false;
        }

        MenuManagerTask? manager = Manager as MenuManagerTask;
        if (!IsCapturing)
        {
            if (WasClicked)
            {
                int row = ((_pointerY - LayoutY) / RowHeight) + Offset;
                if (row is >= 0 and < RowCount)
                {
                    CapturingRow = row;
                    manager?.Navigator.Deactivate();
                    Manager?.PlaySound(ChooseSoundName);
                }
            }
        }
        else if (manager?.Host is { } host)
        {
            Capture(manager, host);
        }

        base.OnUpdate();
    }

    /// <inheritdoc/>
    protected override void OnLoadData()
    {
        if (Bindings is not { } bindings)
        {
            return;
        }

        for (int row = 0; row < RowCount; row++)
        {
            _keys[row] = bindings.Find(Actions[row]) is { } binding ? RetailInputKeys.IndexOf(binding.Device, binding.Key) : -1;
        }
    }

    /// <inheritdoc/>
    protected override void OnSaveData()
    {
        if (Bindings is not { } bindings)
        {
            return;
        }

        for (int row = 0; row < RowCount; row++)
        {
            if (KeyOf(row) is { } key)
            {
                bindings.Bind(new InputBinding(Actions[row], key.Device, key.Name));
            }
        }
    }

    private IMenuInputBindings? Bindings => (Manager as MenuManagerTask)?.Configuration as IMenuInputBindings;

    // 0x4237C0: every row from its default, to be committed with the screen.
    private void Reset()
    {
        if (Bindings is not { } bindings)
        {
            return;
        }

        for (int row = 0; row < RowCount; row++)
        {
            _keys[row] = bindings.DefaultOf(Actions[row]) is { } binding ? RetailInputKeys.IndexOf(binding.Device, binding.Key) : -1;
        }
    }

    private void Capture(MenuManagerTask manager, IMenuHost host)
    {
        if (host.IsKeyDown(EscapeCode))
        {
            Manager?.PlaySound(CancelSoundName);
            EndCapture(manager);
            return;
        }

        int mouse = host.MouseButtons;
        int taken = -1;
        for (int i = 0; i < RetailInputKeys.All.Count && taken < 0; i++)
        {
            RetailInputKey key = RetailInputKeys.All[i];
            bool held = key.IsMouse ? mouse == key.Code : host.IsKeyDown(key.Code);
            if (held && key.Code is not (LeftWindowsCode or RightWindowsCode))
            {
                taken = i;
            }
        }

        if (taken < 0)
        {
            return;
        }

        // One of seven key sounds, menu_k01 to menu_k07.
        Manager?.PlaySound($"menu_k0{(Math.Abs((long)(int)_random.NextRaw()) % 7) + 1}");

        int row = CapturingRow;
        int old = _keys[row];
        _keys[row] = taken;
        for (int other = 0; other < RowCount; other++)
        {
            if (other != row && _keys[other] == taken)
            {
                _keys[other] = old;
            }
        }

        EndCapture(manager);
    }

    private void EndCapture(MenuManagerTask manager)
    {
        manager.FlushInput();
        manager.Navigator.Activate();
        CapturingRow = -1;
    }
}
