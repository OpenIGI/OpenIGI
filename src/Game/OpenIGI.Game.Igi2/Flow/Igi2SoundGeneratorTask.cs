using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>
/// A sound the level's scripts start and stop where the object it hangs off stands, I.G.I. 2's <c>SoundGenerator</c>,
/// exporting <c>isPlaying</c>.
/// </summary>
/// <remarks>
/// <para>
/// It declares no parameters: what it plays is whatever its children's scripts last asked for.
/// <c>SoundGenerator_RunHandler</c> (<c>igi2 0x486C90</c>) points <c>SoundGenerator_PlaySound</c> and
/// <c>SoundGenerator_StopSound</c> at itself, runs its children — the <c>SequenceCommand</c>s that call them — and lets the
/// two natives go again.
/// </para>
/// <para>
/// <c>SoundGenerator_PlaySound</c> (<c>0x486950</c>) deletes the sound it has, takes the new name and makes the sound, and is
/// playing only when one was made; <c>SoundGenerator_StopSound</c> (<c>0x486910</c>) deletes it. Neither notices a sound ending
/// on its own, so <c>isPlaying</c> stays set after a one-shot has finished.
/// </para>
/// </remarks>
public sealed partial class Igi2SoundGeneratorTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SoundGenerator";

    /// <summary>The native its scripts start a sound with.</summary>
    public const string PlaySoundNative = "SoundGenerator_PlaySound";

    /// <summary>The native its scripts stop it with.</summary>
    public const string StopSoundNative = "SoundGenerator_StopSound";

    /// <summary>The longest name it takes, one short of its 32-byte <c>zSound</c>.</summary>
    public const int MaxSoundName = 31;

    private Igi2Sound? _sound;
    private byte _isPlaying;

    /// <summary>Gets the name of the sound it was last asked for, <c>zSound</c>.</summary>
    public string Sound { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether it made a sound and has not been stopped, <c>isPlaying</c>.</summary>
    public bool IsPlaying => _isPlaying != 0;

    /// <summary>Gets or sets the level's sounds it plays through, or <see langword="null"/> for none.</summary>
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Registers the two natives, which act on the generator their symbols' context names.</summary>
    /// <param name="natives">The namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    public static void Register(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);

        // SoundGenerator_PlaySoundScriptFunction (igi2 0x486A40): only with an argument and a generator to act on.
        functions.Register(natives, PlaySoundNative, static call =>
        {
            if (call.Count > 0 && call.Context is Igi2SoundGeneratorTask generator)
            {
                generator.Play(call.GetText(0));
            }

            return QvmValue.Zero;
        }, 1);

        // SoundGenerator_StopSoundScriptFunction (igi2 0x486AE0).
        functions.Register(natives, StopSoundNative, static call =>
        {
            (call.Context as Igi2SoundGeneratorTask)?.Stop();
            return QvmValue.Zero;
        }, 0);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.Natives.SetSymbolContext(PlaySoundNative, this);
        level.Natives.SetSymbolContext(StopSoundNative, this);
        try
        {
            level.RunChildren(this);
        }
        finally
        {
            level.Natives.SetSymbolContext(PlaySoundNative, null);
            level.Natives.SetSymbolContext(StopSoundNative, null);
        }
    }

    /// <summary>Plays a sound in place of the one it has.</summary>
    /// <param name="name">The definition's name.</param>
    public void Play(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Stop();
        Sound = name.Length > MaxSoundName ? name[..MaxSoundName] : name;
        if (Sound.Length > 0 && Sounds?.New(Sound, Where()) is { } made)
        {
            _sound = made;
            _isPlaying = 1;
        }
    }

    /// <summary>Stops the sound it has.</summary>
    public void Stop()
    {
        _sound?.Delete();
        _sound = null;
        _isPlaying = 0;
    }

    /// <inheritdoc/>
    /// <remarks><c>SoundGenerator_RegisterScriptSymbolsHandler</c> (<c>igi2 0x486B60</c>): a byte, writable.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isPlaying", () => _isPlaying, value => _isPlaying = (byte)value, sizeof(byte));
    }

    /// <summary>Where it stands: the position of the nearest task above it that has one.</summary>
    private Vector3d Where()
    {
        for (GameTask? task = Parent; task is not null; task = task.Parent)
        {
            if (task is DeclaredTask declared && declared.TryGetValue("Position", out object? value) && value is Vector3d position)
            {
                return position;
            }
        }

        return default;
    }
}
