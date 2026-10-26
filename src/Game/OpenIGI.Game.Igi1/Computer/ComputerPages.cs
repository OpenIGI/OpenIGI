using OpenIGI.Game.Computer;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>A word of a page's text, where its line put it, with its font and colour.</summary>
/// <param name="Text">The word.</param>
/// <param name="Font">Its font.</param>
/// <param name="X">Its left, display pixels.</param>
/// <param name="Y">Its top.</param>
/// <param name="Color">Its colour, an index of <see cref="ComputerColors.Items"/>.</param>
public readonly record struct ComputerPageWord(string Text, string Font, int X, int Y, int Color);

/// <summary>What a text page draws: its words and, on the log, the rule under each line.</summary>
public abstract class ComputerPage : IComputerScreen
{
    private readonly List<ComputerWord> _wrapped = [];

    /// <summary>Initializes a new instance of the <see cref="ComputerPage"/> class.</summary>
    /// <param name="computer">The computer.</param>
    protected ComputerPage(Igi1Computer computer)
    {
        Computer = computer ?? throw new ArgumentNullException(nameof(computer));
    }

    /// <summary>Gets the words, as the last tick laid them out.</summary>
    public List<ComputerPageWord> Words { get; } = [];

    /// <summary>Gets the rules, each a hundred pixels from its point, as the last tick laid them out.</summary>
    public List<(int X, int Y)> Rules { get; } = [];

    /// <inheritdoc/>
    public abstract ComputerScreenKind Kind { get; }

    /// <summary>Gets the computer.</summary>
    protected Igi1Computer Computer { get; }

    /// <inheritdoc/>
    public void Run()
    {
        Computer.Panel.Run();
        Words.Clear();
        Rules.Clear();
        Layout();
    }

    /// <summary>Lays the page out, as its draw handler does.</summary>
    protected abstract void Layout();

    /// <summary>Sets a text over lines four hundred pixels wide (<c>0x469450</c>).</summary>
    /// <param name="x">The left of its lines.</param>
    /// <param name="y">The top of the first.</param>
    /// <param name="text">The text.</param>
    /// <param name="font">Its font.</param>
    /// <param name="color">Its colour.</param>
    /// <returns>The Y under its last line.</returns>
    protected int Wrap(int x, int y, string text, string font, int color)
    {
        _wrapped.Clear();
        int under = ComputerTextWrap.Wrap(Computer.World.Fonts ?? ComputerTextWrap.Estimated, x, y, text, font, 400, _wrapped);
        foreach (ComputerWord word in _wrapped)
        {
            Words.Add(new ComputerPageWord(word.Text, font, word.X, word.Y, color));
        }

        return under;
    }

    /// <summary>The x87's fistp, which the game's chopping control word makes a truncation.</summary>
    /// <param name="value">The value.</param>
    /// <returns>It made whole.</returns>
    protected static int RoundInt(double value) => (int)value;

}

/// <summary>
/// The objectives page, <c>ComputerObjectives</c> (<c>0x468E90</c>): each of the six rows with its state, and beside each a
/// button that opens the map over the row's task.
/// </summary>
public sealed class ComputerObjectivesPage : ComputerPage
{
    private readonly ComputerButtonItem?[] _buttons = new ComputerButtonItem?[MissionObjectiveRuntime.ObjectiveSlotCount];

    /// <summary>Initializes a new instance of the <see cref="ComputerObjectivesPage"/> class.</summary>
    /// <param name="computer">The computer.</param>
    public ComputerObjectivesPage(Igi1Computer computer)
        : base(computer)
    {
        float w = computer.Width, h = computer.Height;
        if (computer.MapEnabled)
        {
            computer.AddTab(1, w * 0.1f, h * 0.80833334f, "MAP", OnTab);
        }

        computer.AddTab(2, w * 0.1f, h * 0.775f, "LOG", OnTab);
        if (computer.MapEnabled)
        {
            // Off the display until the page is laid out and finds a row for them.
            ComputerSprites sprites = new("objective_1.spr", "objective_2.spr", "objective_2.spr");
            (int, int) size = computer.World.SpriteSize(sprites.Normal);
            for (int i = 0; i < _buttons.Length; i++)
            {
                _buttons[i] = computer.Panel.Add(new ComputerButtonItem(i + 3, w * -0.125f, h * -0.16666667f, sprites, size, OnSelect));
            }
        }
    }

    /// <inheritdoc/>
    public override ComputerScreenKind Kind => ComputerScreenKind.Objectives;

    /// <inheritdoc/>
    protected override void Layout()
    {
        // 0x469110.
        float w = Computer.Width, h = Computer.Height;
        int x = RoundInt(w * (Computer.MapEnabled ? 0.15 : 0.1));
        int y = RoundInt(h * 0.1);
        if (Computer.Objectives is not { } runtime)
        {
            return;
        }

        foreach (MissionObjective objective in runtime.Objectives)
        {
            if (Computer.MapEnabled && _buttons[objective.Slot] is { } button)
            {
                button.X = x - 34;
                button.Y = y + 2;
            }

            string state = objective.State switch
            {
                MissionObjectiveState.Failed => "FAILED",
                MissionObjectiveState.Complete => "COMPLETE",
                _ => "INCOMPLETE",
            };
            string heading = $"{Computer.Text("OBJECTIVE")} {objective.Slot + 1}: {Computer.Text(state)}";
            int under = Wrap(x, y, heading, Igi1Computer.TabFont, 2);
            string text = Computer.World.FindText(objective.TextResource) ?? $"RM: {objective.TextResource}";
            y = Wrap(x, under, text, Igi1Computer.TextFont, 1) + 8;
        }
    }

    // 0x469030.
    private void OnTab(ComputerItem item, int id)
    {
        if ((Computer.Pressed & Igi1Computer.Left) == 0)
        {
            return;
        }

        if (id == 1)
        {
            Computer.SetScreen(ComputerScreenKind.Map);
        }
        else if (id == 2)
        {
            Computer.SetScreen(ComputerScreenKind.Log);
        }
    }

    // 0x469060: the button beside a row opens the map over the row's task.
    private void OnSelect(ComputerItem item, int id)
    {
        if (item.Id != id || item is not ComputerButtonItem button)
        {
            return;
        }

        bool down = (Computer.Buttons & Igi1Computer.Left) != 0;
        button.WasDown = button.IsDown;
        button.IsDown = down;
        if (!down || id is < 3 or > 8 || Computer.Objectives is not { } runtime)
        {
            return;
        }

        int slot = id - 3;
        foreach (MissionObjective objective in runtime.Objectives)
        {
            if (objective.Slot == slot && objective.LinkTaskId != -1)
            {
                Computer.MapStartTask = Computer.World.TaskById(objective.LinkTaskId);
                Computer.SetScreen(ComputerScreenKind.Map);
            }
        }
    }
}

/// <summary>The log page, <c>ComputerLog</c> (<c>0x468920</c>): the newest fifteen messages, scrolled back by its arrows.</summary>
public sealed class ComputerLogPage : ComputerPage
{
    /// <summary>The lines shown at once.</summary>
    public const int Shown = 15;

    /// <summary>Initializes a new instance of the <see cref="ComputerLogPage"/> class.</summary>
    /// <param name="computer">The computer.</param>
    public ComputerLogPage(Igi1Computer computer)
        : base(computer)
    {
        float w = computer.Width, h = computer.Height;
        if (computer.MapEnabled)
        {
            computer.AddTab(1, w * 0.1f, h * 0.80833334f, "MAP", OnTab);
        }

        computer.AddTab(2, w * 0.1f, h * 0.775f, "OBJECTIVES", OnTab);
        float x = w * 0.090625f, y = h * 0.1f;
        ComputerSprites up = new("arrowup_1.spr", "arrowup_2.spr", "arrowup_3.spr");
        ComputerSprites down = new("arrowdown_1.spr", "arrowdown_2.spr", "arrowdown_3.spr");
        computer.Panel.Add(new ComputerButtonItem(3, x, y, up, computer.World.SpriteSize(up.Normal), OnScroll));
        computer.Panel.Add(new ComputerButtonItem(4, x, y + 36f, down, computer.World.SpriteSize(down.Normal), OnScroll));
    }

    /// <summary>Gets how many of the newest lines are scrolled past.</summary>
    public int Scroll { get; private set; }

    /// <inheritdoc/>
    public override ComputerScreenKind Kind => ComputerScreenKind.Log;

    /// <inheritdoc/>
    protected override void Layout()
    {
        // 0x468BC0: the newest first, a rule under each.
        IReadOnlyList<MissionComputerLogEntry> log = Computer.Objectives?.Log ?? [];
        float w = Computer.Width, h = Computer.Height;
        int x = RoundInt(w * 0.15), y = RoundInt(h * 0.1);
        for (int i = 0; i < log.Count && i < Shown; i++)
        {
            int index = log.Count - Scroll - i - 1;
            if (index < 0)
            {
                break;
            }

            MissionComputerLogEntry entry = log[index];
            string text = entry.Text.Length > 0 ? entry.Text : Computer.Text(entry.TextResource);
            y = Wrap(x, y, text, Igi1Computer.LogFont, 1) + 2;
            Rules.Add((x, y));
            y += 2;
        }
    }

    // 0x468B00.
    private void OnTab(ComputerItem item, int id)
    {
        if ((Computer.Pressed & Igi1Computer.Left) == 0)
        {
            return;
        }

        if (id == 1)
        {
            Computer.SetScreen(ComputerScreenKind.Map);
        }
        else if (id == 2)
        {
            Computer.SetScreen(ComputerScreenKind.Objectives);
        }
    }

    // 0x468B30: a press steps one line.
    private void OnScroll(ComputerItem item, int id)
    {
        if (item.Id != id || item is not ComputerButtonItem button)
        {
            return;
        }

        bool was = button.IsDown;
        button.WasDown = was;
        button.IsDown = (Computer.Buttons & Igi1Computer.Left) != 0;
        if (!button.IsDown)
        {
            return;
        }

        int count = Computer.Objectives?.Log.Count ?? 0;
        if (id == 3)
        {
            if (!was && Scroll > 0)
            {
                Scroll--;
            }
        }
        else if (id == 4 && !was && Scroll < count - Shown)
        {
            Scroll++;
        }
    }
}
