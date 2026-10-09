using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;

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
public sealed class RadioRuntime
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
    public bool IsOn { get; set; }

    /// <summary>Gets the ticks left before the next sound (<c>+572</c>).</summary>
    public int Countdown { get; private set; }

    /// <summary>Runs <c>0x43AF90</c> for one tick, after the ExplodeObject update has run.</summary>
    /// <param name="sounds">Where the sounds go, or <see langword="null"/> when nothing can play.</param>
    public void Tick(IRadioSoundPlayer? sounds)
    {
        bool wasOn = IsOn;
        if (_isExploded())
        {
            IsOn = false;
        }
        else if (_onCondition is { } on)
        {
            IsOn = on();
        }

        if (IsOn)
        {
            if (_voice is { } playing)
            {
                if (sounds is not null && sounds.IsPlaying(playing))
                {
                    return;
                }

                _voice = null;
            }

            if (Countdown > 0)
            {
                Countdown--;
                return;
            }

            string sound = _sounds[_random.NextBounded(SoundSlots)];
            Countdown = (int)_random.NextBounded(SoundGapTicks) + SoundGapTicks;
            _voice = sounds?.Play(sound, PositionUnits);
            return;
        }

        if (_voice is { } cut)
        {
            if (sounds is not null && sounds.IsPlaying(cut))
            {
                sounds.Silence(cut);
            }

            _voice = null;
        }

        if (wasOn)
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
        string onExpression = Text(task, "On expression");
        string[] sounds = new string[SoundSlots];
        for (int i = 0; i < SoundSlots; i++)
        {
            sounds[i] = Text(task, $"Radio sound {i}");
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
            Text(task, "Shutdown sound"));
    }

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;
}
