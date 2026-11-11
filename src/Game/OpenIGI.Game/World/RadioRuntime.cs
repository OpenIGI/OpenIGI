using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>Plays the sounds a <see cref="RadioRuntime"/> asks for.</summary>
/// <remarks>
/// The world layer decides when a radio talks; the host owns the voices. A name that resolves to no sound
/// definition answers <see langword="null"/>, which is <c>sub_4E6B00</c> finding nothing: the radio carries
/// on as though it had spoken, silently.
/// </remarks>
public interface IRadioSoundPlayer
{
    /// <summary>Starts a sound at a point.</summary>
    /// <param name="sound">The authored sound name; may be empty.</param>
    /// <param name="positionUnits">Where it plays, in world units.</param>
    /// <returns>The voice, or <see langword="null"/> when nothing was started.</returns>
    object? Play(string sound, Vector3d positionUnits);

    /// <summary>Gets whether a voice <see cref="Play"/> returned is still sounding.</summary>
    /// <param name="voice">The voice.</param>
    /// <returns><see langword="true"/> while it plays.</returns>
    bool IsPlaying(object voice);

    /// <summary>Silences a voice <see cref="Play"/> returned.</summary>
    /// <param name="voice">The voice.</param>
    void Silence(object voice);
}

/// <summary>One <c>Radio</c> task: an <c>ExplodeObject</c> that chatters while it is on.</summary>
/// <remarks>
/// <para>
/// <c>TASKTYPE_RADIO</c> (<c>0x43AD80</c>) registers under <c>ExplodeObject</c> (<c>word_54097C</c>), so its
/// damage, explosion, destroyed model and <c>isExploded</c> are the family's, in
/// <see cref="DestructibleObjectRegistry"/>. What the Radio adds is <c>isOn</c> (<c>+576</c>, exported as
/// <c>%s.isOn</c> on activation, <c>0x43B1C0</c>) and its update, <c>0x43AF90</c>, run after the
/// ExplodeObject update each tick:
/// </para>
/// <list type="bullet">
/// <item>once exploded (<c>+400</c>) it is off; otherwise an authored On expression decides, and a radio
/// with none keeps what it had (off, from its constructor <c>0x43AF20</c>);</item>
/// <item>while on and silent, it counts down <c>+572</c>; at zero it plays one of its four Radio sounds
/// (<c>rand % 4</c>) and waits <c>150 + rand % 150</c> ticks after that sound has finished;</item>
/// <item>switched off, it cuts the sound it was playing and, on the tick it goes off, plays its Shutdown
/// sound once.</item>
/// </list>
/// <para>
/// Both draws come from the engine's global stream and are made whether or not the chosen name resolves:
/// every shipped radio (mission 7's Radio_3366, mission 11's Radio_201) authors no sounds at all, yet a lit
/// one still rolls every few seconds.
/// </para>
/// </remarks>
public sealed class RadioRuntime : Saves.IExchangedState
{
    /// <summary>How many Radio sound slots the type declares (<c>"Radio sound %d"</c>, 0 to 3).</summary>
    public const int SoundSlots = 4;

    /// <summary>The least wait between sounds, in ticks: <c>0x43AF90</c> adds 150 to its draw.</summary>
    public const int SoundGapTicks = 150;

    private readonly Func<bool>? _onCondition;
    private readonly Func<bool> _isExploded;
    private readonly IgiRandom _random;
    private readonly string[] _sounds;
    private readonly string _shutdownSound;
    private object? _voice;

    internal RadioRuntime(
        DeclaredTask task,
        Vector3d positionUnits,
        Func<bool>? onCondition,
        Func<bool> isExploded,
        IgiRandom random,
        string[] sounds,
        string shutdownSound)
    {
        Task = task;
        PositionUnits = positionUnits;
        _onCondition = onCondition;
        _isExploded = isExploded;
        _random = random;
        _sounds = sounds;
        _shutdownSound = shutdownSound;
    }

    /// <summary>Gets the radio's task.</summary>
    public DeclaredTask Task { get; }

    /// <summary>Gets where its sounds play, in world units (the task's position, <c>+32</c>).</summary>
    public Vector3d PositionUnits { get; }

    /// <summary>Gets or sets <c>isOn</c>.</summary>
    public bool IsOn
    {
        get => _chatter.IsOn;
        set => _chatter.IsOn = value;
    }

    // The chatter both games run, which keeps whether it is on and how long until its next sound.
    private readonly OpenIGI.Game.Objects.RadioChatter _chatter = new();

    // As the match began: whether it talked, and how long until it next does.
    private (bool On, bool? Mirrored, int Countdown) _start;

    internal void MarkLevelStart() => _start = (IsOn, MirroredOn, Countdown);

    internal void ResetToLevelStart() => (IsOn, MirroredOn, Countdown) = _start;

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        var levelStart = _start;
        MarkLevelStart();
        _start = (exchange.Bool(_start.On), exchange.Bool(_start.Mirrored), exchange.Int(_start.Countdown));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }

    /// <summary>Gets or sets the co-op server's word on <see cref="IsOn"/>, which a client's update takes in place of its own.</summary>
    internal bool? MirroredOn { get; set; }

    /// <summary>Gets the ticks left before the next sound (<c>+572</c>).</summary>
    public int Countdown
    {
        get => _chatter.Countdown;
        private set => _chatter.Countdown = value;
    }

    /// <summary>Runs <c>0x43AF90</c> for one tick, after the ExplodeObject update has run.</summary>
    /// <param name="sounds">Where the sounds go, or <see langword="null"/> when nothing can play.</param>
    public void Tick(IRadioSoundPlayer? sounds)
    {
        // What a server told a client stands; else off once blown up, else as the expression says (read only then).
        bool on;
        if (MirroredOn is bool told)
        {
            on = told;
        }
        else
        {
            bool exploded = _isExploded();
            on = OpenIGI.Game.Objects.RadioChatter.Switched(IsOn, exploded, exploded ? null : _onCondition?.Invoke());
        }

        bool holds = _voice is not null;
        bool playing = holds && sounds is not null && sounds.IsPlaying(_voice!);
        OpenIGI.Game.Objects.RadioStep step = _chatter.Step(on, holds, playing, _random);
        if (step.Silence)
        {
            sounds!.Silence(_voice!);
        }

        if (step.Drop)
        {
            _voice = null;
        }

        if (step.Play is int slot)
        {
            _voice = sounds?.Play(_sounds[slot], PositionUnits);
        }

        if (step.Shutdown)
        {
            sounds?.Play(_shutdownSound, PositionUnits);
        }
    }

    /// <summary>Builds the runtime for a declared <c>Radio</c> task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="expressions">Evaluates its On expression.</param>
    /// <param name="variables">Reads the ExplodeObject's <c>isExploded</c>.</param>
    /// <param name="random">The engine's global stream.</param>
    /// <returns>The runtime.</returns>
    internal static RadioRuntime Create(
        DeclaredTask task,
        LevelExpressionRuntime expressions,
        ScriptVarRegistry variables,
        IgiRandom random)
    {
        string onExpression = TextOrEmpty(task, "On expression");
        string[] sounds = new string[SoundSlots];
        for (int i = 0; i < SoundSlots; i++)
        {
            sounds[i] = TextOrEmpty(task, $"Radio sound {i}");
        }

        string explodedSymbol = $"{task.Name}.isExploded";
        Vector3d position = task.TryGetValue("Position", out object? raw) && raw is Vector3d at ? at : default;

        // An empty expression is no expression: 0x43AF90 only evaluates one sub_4F16C0 says is there.
        Func<bool>? onCondition = string.IsNullOrWhiteSpace(onExpression)
            ? null
            : () => expressions.EvaluateCondition(onExpression, emptyValue: false, task);
        return new RadioRuntime(
            task,
            position,
            onCondition,
            () => variables.TryRead(explodedSymbol, out QvmValue exploded) && exploded.ToInt32() != 0,
            random,
            sounds,
            TextOrEmpty(task, "Shutdown sound"));
    }
}
