namespace OpenIGI.Game.Igi1.Computer;

/// <summary>
/// The map's boot box, <c>ComputerStartup</c> (<c>0x466240</c>): a line of text over a progress bar, each line held for its
/// number of ticks, a callback as each ends. While it runs on its first line the picture behind it is black.
/// </summary>
public sealed class ComputerStartup
{
    private readonly float[] _ticks;
    private readonly Action<int>?[] _done;

    /// <summary>Initializes a new instance of the <see cref="ComputerStartup"/> class.</summary>
    /// <param name="lines">The lines, by their text resources.</param>
    /// <param name="ticks">How many ticks each line is held.</param>
    /// <param name="textColor">The text's and the box's colour, 0 to 1 a channel.</param>
    /// <param name="barColor">The bar's colour.</param>
    public ComputerStartup(string[] lines, float[] ticks, (float R, float G, float B) textColor, (float R, float G, float B) barColor)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(ticks);
        Lines = lines;
        _ticks = ticks;
        _done = new Action<int>?[lines.Length];
        TextColor = textColor;
        BarColor = barColor;
    }

    /// <summary>Gets the lines, by their text resources.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Gets the text's and the box's colour.</summary>
    public (float R, float G, float B) TextColor { get; }

    /// <summary>Gets the bar's colour.</summary>
    public (float R, float G, float B) BarColor { get; }

    /// <summary>Gets the line showing.</summary>
    public int Step { get; private set; }

    /// <summary>Gets the ticks the line has shown.</summary>
    public float Timer { get; private set; }

    /// <summary>Gets a value indicating whether it is running, and so drawn.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Gets a value indicating whether it ran to its end.</summary>
    public bool IsDone { get; private set; }

    /// <summary>Gets how far the bar is filled, the line's ticks shown over its ticks.</summary>
    public float Progress => Timer / _ticks[Step];

    /// <summary>Sets what happens as a line ends (<c>0x466390</c>).</summary>
    /// <param name="step">The line.</param>
    /// <param name="done">What happens, given the line.</param>
    public void SetCallback(int step, Action<int> done) => _done[step] = done;

    /// <summary>Starts it from its first line (<c>0x4663B0</c>).</summary>
    public void Start()
    {
        IsDone = false;
        IsRunning = true;
        Step = 0;
        Timer = 0f;
    }

    /// <summary>Runs it a tick (<c>0x466C90</c>): a line ends once it has shown more than its ticks.</summary>
    public void Run()
    {
        if (!IsRunning)
        {
            return;
        }

        Timer += 1f;
        int step = Step;
        if (!(Timer > _ticks[step]))
        {
            return;
        }

        _done[step]?.Invoke(step);
        if (Step < Lines.Count - 1)
        {
            Timer = 0f;
            Step++;
            return;
        }

        IsRunning = false;
        IsDone = true;
    }
}
