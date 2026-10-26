using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Audio;

/// <summary>Where the game hears from: the microphone, which follows the game's camera, and whether it stands in a sector.</summary>
public interface IIgi2Listener
{
    /// <summary>Gets where it hears from.</summary>
    Vector3d Position { get; }

    /// <summary>Gets a value indicating whether it stands inside a building's sector.</summary>
    bool IsInside { get; }
}

/// <summary>
/// A box of ambient sound, I.G.I. 2's <c>AmbientArea</c> (ambientarea.c): while the listener is within it (or within its falloff),
/// one of its sounds at a time plays at the listener, at a volume that fades in and out, with a drawn pause between sounds.
/// </summary>
/// <remarks>
/// <para>
/// <c>AmbientArea_RunHandler</c>: unless its <c>On expression</c> fails, the listener's offset is turned into the box's frame and
/// measured in metres: outside the size plus the falloff the target volume is nought; inside the size it is one; between, one less
/// the distance past the size over the falloff. The area counts only where the listener's place allows it (inside a building or
/// out). The volume steps toward the target, up while the area counts and down otherwise, by one over the fade time in ticks plus
/// one, and never above the target. At nought the sound stops and a new pause is drawn; while a sound plays its volume follows; with
/// none playing, a sound of the list is drawn every tick the area counts, and started once the pause is spent. The pause counts down
/// every tick. Its children run under it.
/// </para>
/// <para>
/// Its <c>Noise level</c>, when above nought, lists it with the areas that muffle what soldiers hear
/// (<c>AmbientArea_GetNoiseLevelAtPosition</c>).
/// </para>
/// </remarks>
public sealed partial class Igi2AmbientAreaTask : Igi2Task, IIgi2WorldMember
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AmbientArea";

    /// <summary>The most sounds an area names.</summary>
    public const int MaxSounds = 8;

    private readonly List<string> _sounds = [];
    private LevelExpression? _on;
    private float[] _frame = EngineEuler.Rotation(0f, 0f, 0f);
    private bool _isFramed;
    private double _volume;
    private double _target;
    private int _delay;
    private Igi2Sound? _playing;

    /// <summary>Gets or sets the box's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the box's turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the box's half size, in metres.</summary>
    [ScriptParam(ScriptParamType.Real64x3, "Size")]
    public Vector3d Size { get; set; } = new(1.0, 1.0, 1.0);

    /// <summary>Gets or sets how far outside the box the sound fades away, in metres.</summary>
    [ScriptParam(ScriptParamType.Real64, "Falloff")]
    public double Falloff { get; set; } = 10.0;

    /// <summary>Gets or sets the least pause between sounds, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real64, "Min delay")]
    public double MinDelay { get; set; }

    /// <summary>Gets or sets the drawn part of the pause, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real64, "Random wait")]
    public double RandomWait { get; set; }

    /// <summary>Gets or sets the sounds, separated by spaces, commas or semicolons.</summary>
    [ScriptParam(ScriptParamType.String256, "SoundDef")]
    public string SoundDefs { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it sounds while the listener is inside a building.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Inside Buildings")]
    public bool InsideBuildings { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it sounds while the listener is outside.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Outside Buildings")]
    public bool OutsideBuildings { get; set; } = true;

    /// <summary>Gets or sets the expression it sounds under; blank for always.</summary>
    [ScriptParam(ScriptParamType.VarString, "On expression")]
    public string OnExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets how long it takes to fade in or out, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real64, "Fade time")]
    public double FadeTime { get; set; } = 1.0 / 3.0;

    /// <summary>Gets or sets a value indicating whether the music volume setting governs it rather than the effects'.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Controlled By Music Volume Setting")]
    public bool IsMusic { get; set; }

    /// <summary>Gets or sets how much it muffles what soldiers hear, nought to one.</summary>
    [ScriptParam(ScriptParamType.Real32, "Noise level")]
    public float NoiseLevel { get; set; }

    /// <summary>Gets the sounds it draws from.</summary>
    public IReadOnlyList<string> Sounds => _sounds;

    /// <summary>Gets the volume it now sounds at.</summary>
    public double Volume => _volume;

    /// <summary>Gets the ticks left before a sound may start.</summary>
    public int Delay => _delay;

    /// <summary>Gets or sets the sounds it plays through.</summary>
    [LevelService]
    public Igi2Sounds? Audio { get; set; }

    /// <summary>Gets or sets where the game hears from.</summary>
    public IIgi2Listener? Listener { get; set; }

    /// <summary>Gets or sets the run stream its pauses and choices are drawn from.</summary>
    public IgiRandom? Random { get; set; }

    /// <summary>
    /// Sets it up as a task that makes one does, <c>AmbientArea_SetParameters</c>: where it is, its frame, its half sizes in metres,
    /// its falloff and its sound list. <see cref="Prepare"/> follows, as its update-internal-data event does.
    /// </summary>
    /// <param name="position">Where it is.</param>
    /// <param name="frame">Its frame, row by row.</param>
    /// <param name="size">Its half sizes, in metres.</param>
    /// <param name="falloff">How far past its box it fades out, in metres.</param>
    /// <param name="soundDefs">Its sounds.</param>
    public void SetParameters(Vector3d position, IReadOnlyList<float> frame, Vector3d size, double falloff, string soundDefs)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(soundDefs);
        Position = position;
        Size = size;
        Falloff = falloff;
        SoundDefs = soundDefs;
        _frame = [.. frame];
        _isFramed = true;
    }

    /// <summary>
    /// Readies it once its parameters are read (<c>AmbientArea_UpdateInternalDataHandler</c>): the sound list split on spaces, commas
    /// and semicolons (at most eight), and a first pause drawn.
    /// </summary>
    public void Prepare()
    {
        _sounds.Clear();
        foreach (string sound in SoundDefs.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (_sounds.Count < MaxSounds)
            {
                _sounds.Add(sound.Length > 31 ? sound[..31] : sound);
            }
        }

        if (!_isFramed)
        {
            _frame = EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        }

        DrawDelay();
    }

    /// <summary>
    /// How much it muffles a point, or nought outside its box and falloff (one area's part of
    /// <c>AmbientArea_GetNoiseLevelAtPosition</c>, which takes the loudest, at most one).
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>Its noise level there.</returns>
    public float NoiseAt(Vector3d point)
    {
        if (!(NoiseLevel > 0f))
        {
            return 0f;
        }

        (Fpu x, Fpu y, Fpu z) = Local(point);
        float ax = ((Fpu)Math.Abs(x.F) * 0.000244140625).F, ay = ((Fpu)Math.Abs(y.F) * 0.000244140625).F;
        float az = ((Fpu)Math.Abs(z.F) * 0.000244140625).F;
        return Size.X + Falloff > ax && Size.Y + Falloff > ay && Size.Z + Falloff > az ? NoiseLevel : 0f;
    }

    /// <summary>Stops its sound and draws a new pause, as it is taken out of the world (<c>AmbientArea_RemoveHandler</c>).</summary>
    public void RemoveFromWorld()
    {
        DrawDelay();
        Stop();
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Listener is { } listener)
        {
            Step(level, listener);
        }

        level.RunChildren(this);
    }

    private void Step(Igi2LevelRun level, IIgi2Listener listener)
    {
        _on ??= Igi2LevelExpressions.Create(OnExpression, Name, level.Natives);
        _target = 0.0;
        bool counts = false;
        if (_on.IsEmpty || (byte)(long)level.Evaluate(_on) != 0)
        {
            counts = Measure(listener.Position);
        }

        if (!(listener.IsInside ? InsideBuildings : OutsideBuildings))
        {
            counts = false;
        }

        Fpu step = (Fpu)1.0 / (((Fpu)(double)Igi2LevelRun.Frequency * FadeTime) + 1.0);
        double volume = counts ? (step + _volume).Value : ((Fpu)_volume - step).Value;
        _volume = volume >= 0.0 ? Math.Min(_target, volume) : 0.0;

        if (_volume == 0.0)
        {
            DrawDelay();
            Stop();
        }
        else if (_playing is { } playing)
        {
            if (playing.IsAlive)
            {
                playing.SetVolumes(-1f, (float)_volume);
            }
            else
            {
                DrawDelay();
                Stop();
            }
        }
        else if (counts)
        {
            uint draw = Random?.NextRaw() ?? 0u;
            int pick = Fpu.ToInt32((Fpu)(double)(ushort)draw * 0.0000152587890625 * _sounds.Count);
            Stop();
            if (_delay == 0 && pick < _sounds.Count && Audio is { } audio)
            {
                // Played at the microphone (SoundSys_SetSoundRelativeToMicrophone): it follows the listener wherever it goes. The
                // music-volume flag (0x20) waits on the mixer's volume settings.
                _playing = audio.New(_sounds[pick], listener.Position, () => listener.Position);
                _playing?.SetVolumes(-1f, (float)_volume);
            }
        }

        if (_delay > 0)
        {
            _delay--;
        }
    }

    /// <summary>Sets the target volume from the listener's place in the box; returns whether the area counts there.</summary>
    private bool Measure(Vector3d listener)
    {
        (Fpu lx, Fpu ly, Fpu lz) = Local(listener);
        Fpu ax = (Fpu)Math.Abs(lx.Value) * 0.000244140625;
        Fpu ay = (Fpu)Math.Abs(ly.Value) * 0.000244140625;
        Fpu az = (Fpu)Math.Abs(lz.Value) * 0.000244140625;
        if (((Fpu)Size.X + Falloff).Value < ax.Value || ((Fpu)Falloff + Size.Y).Value < ay.Value
            || ((Fpu)Size.Z + Falloff).Value < az.Value)
        {
            return false;
        }

        if (ax.Value <= Size.X && ay.Value <= Size.Y && az.Value <= Size.Z)
        {
            _target = 1.0;
            return true;
        }

        Fpu outside = 0.0;
        outside = Past(outside, ax, Size.X);
        outside = Past(outside, ay, Size.Y);
        outside = Past(outside, az, Size.Z);
        double target = ((Fpu)1.0 - (Fpu.Sqrt(outside) / Falloff)).Value;
        _target = target < 0.0 ? 0.0 : target;
        return true;
    }

    private static Fpu Past(Fpu sum, Fpu along, double size) =>
        along.Value > size ? sum + ((along - size) * (along - size)) : sum;

    /// <summary>
    /// A point's offset from the centre in the box's frame, the inverse turn as the original forms it: left in the register for the
    /// listener, stored as floats by the noise query.
    /// </summary>
    private (Fpu X, Fpu Y, Fpu Z) Local(Vector3d point)
    {
        float[] m = _frame;
        Fpu dx = (Fpu)point.X - Position.X, dy = (Fpu)point.Y - Position.Y, dz = (Fpu)point.Z - Position.Z;
        return (
            ((Fpu)m[6] * dz) + ((Fpu)m[3] * dy) + ((Fpu)m[0] * dx),
            ((Fpu)m[7] * dz) + ((Fpu)m[4] * dy) + ((Fpu)m[1] * dx),
            ((Fpu)m[8] * dz) + ((Fpu)m[5] * dy) + ((Fpu)m[2] * dx));
    }

    /// <summary>A pause before the next sound: the least delay plus a drawn share of the random wait, in ticks.</summary>
    private void DrawDelay()
    {
        uint draw = Random?.NextRaw() ?? 0u;
        _delay = Fpu.ToInt32((((Fpu)(double)(ushort)draw * 0.0000152587890625 * RandomWait) + MinDelay) * 30.0);
    }

    private void Stop()
    {
        _playing?.Delete();
        _playing = null;
    }
}
