using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Audio;

/// <summary>The level's sounds: the ones <c>SoundSys_NewSoundFn</c> (<c>igi2 0x42F6F0</c>) makes, each following what it hangs off.</summary>
/// <remarks>
/// <para>
/// Whether a sound is made is the definitions' answer alone: there is one for every name they define, whatever the
/// mixer does with it, so the game decides the same whether or not anything is heard. Retail's own answer differs only
/// when it has no sound at all (<c>SoundSys_isSoundDisabled</c>), which a played game never is.
/// </para>
/// <para>
/// A sound task asks its parent where it is every tick (<c>Attach_eAttachEvent</c>); a sound here is told by the
/// position it was made to follow, once a tick in <see cref="Run"/>.
/// </para>
/// </remarks>
/// <param name="definitions">The level's definitions.</param>
/// <param name="audio">The mixer, or <see langword="null"/> where nothing is heard.</param>
public sealed class Igi2Sounds(Igi2SoundDefinitions definitions, ITitleAudio? audio)
{
    /// <summary>The ticks a second the sound system counts in, <c>SoundSys_nFrequency</c>, which <c>Flow_SetFrequency</c> sets to the game's.</summary>
    public const int Frequency = 30;

    private readonly List<Igi2Sound> _playing = [];

    /// <summary>Gets the level's definitions.</summary>
    public Igi2SoundDefinitions Definitions { get; } = definitions ?? throw new ArgumentNullException(nameof(definitions));

    /// <summary>Gets the sounds made and not yet ended or deleted.</summary>
    public IReadOnlyList<Igi2Sound> Playing => _playing;

    /// <summary>Makes a sound, <c>SoundSys_NewSoundFn</c> then <c>SoundSys_eSetPositionQTaskEvent</c>.</summary>
    /// <param name="name">The definition's name.</param>
    /// <param name="position">Where it starts.</param>
    /// <param name="follow">Where it is each tick after, or <see langword="null"/> to stay put.</param>
    /// <param name="looping">Whether it loops, or <see langword="null"/> for as its definition says.</param>
    /// <param name="parameters">
    /// What its owner hands it each tick (<c>SoundSys_eSoundUpdateParameterListQTaskEvent</c>), for a definition that is a set of
    /// sounds -- a car's group of start, stop and engine graphs -- or <see langword="null"/> for an owner that hands none.
    /// </param>
    /// <returns>The sound, or <see langword="null"/> for an empty name or one nothing defines.</returns>
    public Igi2Sound? New(string name, Vector3d position, Func<Vector3d>? follow = null, bool? looping = null, Func<IReadOnlyList<float>>? parameters = null)
    {
        if (!Definitions.TryResolve(name, out SoundDefinition? definition))
        {
            // A name this game never defines may still be heard where the mixer finds it elsewhere: I.G.I. 2's clips name all
            // of Project I.G.I.'s reload clicks and ship two. Played or not at all.
            if (audio is null || !audio.CanBorrow(name) || audio.Play(name, position, looping) is not { } borrowed)
            {
                return null;
            }

            Igi2Sound found = new(this, name, borrowed, follow);
            _playing.Add(found);
            return found;
        }

        // A group, a graph or a trigger is instantiated whole and driven by its owner (SoundSys_NewSoundFn makes the definition's
        // tree as a child of the owner); a plain sound is one voice.
        Igi2Sound sound = definition is { IsPlayable: false }
            ? new(this, name, null, follow, audio?.PlaySet(name), position, parameters, isSet: true)
            : new(this, name, audio?.Play(name, position, looping), follow);
        _playing.Add(sound);
        return sound;
    }

    /// <summary>
    /// How long a definition sounds, in ticks, <c>SoundSys_GetSoundLength</c> (<c>igi2 0x42F870</c>): the longest of its sounds,
    /// each its sample's length times the game's frequency, cut to a whole tick (<c>SoundDefSound_GetSoundLengthHandler</c>,
    /// <c>igi2ed 0x431930</c>, and the trigger's and the graph's alike), a group's the longest of its children's
    /// (<c>SoundDefGroup_GetSoundLengthHandler</c>, <c>0x4348D0</c>).
    /// </summary>
    /// <param name="name">The definition's name.</param>
    /// <returns>The ticks, or nought for a name nothing defines.</returns>
    /// <remarks>The dedicated server's handlers multiply by nought, having no samples; this is the client's answer.</remarks>
    public int LengthTicks(string name) =>
        Definitions.TryResolve(name, out SoundDefinition? definition) && definition is not null ? LengthTicks(definition, 0) : 0;

    /// <summary>Runs the sounds one tick: each follows what it hangs off, and counts the time it has played.</summary>
    /// <remarks>One ended or deleted is let go.</remarks>
    public void Run()
    {
        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            Igi2Sound sound = _playing[i];
            if (!sound.IsAlive)
            {
                _playing.RemoveAt(i);
                continue;
            }

            sound.Tick();
        }
    }

    private int LengthTicks(SoundDefinition definition, int longest)
    {
        if (definition.Kind == SoundDefinitionKind.Group)
        {
            foreach (SoundDefinition child in definition.Children)
            {
                longest = LengthTicks(child, longest);
            }

            return longest;
        }

        // The sample's length is taken as a float, then by the frequency, and cut.
        float seconds = (float)(audio?.SampleSeconds(definition.BankKey) ?? 0.0);
        int ticks = Fpu.ToInt32((Fpu)(double)Frequency * seconds);
        return ticks > longest ? ticks : longest;
    }

    /// <summary>Lets go of a deleted sound.</summary>
    internal void Forget(Igi2Sound sound) => _playing.Remove(sound);
}

/// <summary>One sound the level made: its voice, and what it follows.</summary>
public sealed class Igi2Sound : IIgi2SoundInstance
{
    /// <summary>The length of a tick, in seconds.</summary>
    private const float TickSeconds = 1f / 30f;

    private readonly Igi2Sounds _owner;
    private readonly ITitleVoice? _voice;
    private readonly Func<Vector3d>? _follow;
    private readonly ITitleSoundSet? _set;
    private readonly Func<IReadOnlyList<float>>? _parameters;
    private readonly Vector3d _made;
    private readonly bool _isSet;
    private bool _deleted;

    internal Igi2Sound(
        Igi2Sounds owner,
        string name,
        ITitleVoice? voice,
        Func<Vector3d>? follow,
        ITitleSoundSet? set = null,
        Vector3d made = default,
        Func<IReadOnlyList<float>>? parameters = null,
        bool isSet = false)
    {
        _isSet = isSet;
        _owner = owner;
        Name = name;
        _voice = voice;
        _follow = follow;
        _set = set;
        _made = made;
        _parameters = parameters;
    }

    /// <summary>Gets the definition's name.</summary>
    public string Name { get; }

    /// <summary>Gets how long it has played, in seconds.</summary>
    public float Seconds { get; private set; }

    /// <summary>Gets the sound's own volume, <c>vBaseVolume</c>: one unless set.</summary>
    public float BaseVolume { get; private set; } = 1f;

    /// <summary>Gets the share of its volume it plays at, <c>vRelativeVolume</c>: one unless set.</summary>
    public float RelativeVolume { get; private set; } = 1f;

    /// <inheritdoc/>
    /// <remarks>The voice sounds at the product of the two (<c>SoundSys_SetSoundVolumes</c>): a base below nought keeps the base.</remarks>
    public void SetVolumes(float baseVolume, float relativeVolume)
    {
        if (baseVolume >= 0f)
        {
            BaseVolume = baseVolume;
        }

        RelativeVolume = relativeVolume;
        _voice?.SetGain(((Fpu)BaseVolume * RelativeVolume).F);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Until it is deleted, and while the mixer still plays it: one never heard has nothing to end but its deletion. A set -- a
    /// car's engine group -- lasts until it is deleted, heard or not: its graphs loop.
    /// </remarks>
    public bool IsAlive => !_deleted && (_isSet || _voice is { IsPlaying: true });

    /// <inheritdoc/>
    public void MoveToHuman()
    {
        if (_follow is not null)
        {
            _voice?.MoveTo(_follow());
        }
    }

    /// <inheritdoc/>
    public void Delete()
    {
        if (_deleted)
        {
            return;
        }

        _deleted = true;
        _voice?.Silence();
        _set?.Silence();
        _owner.Forget(this);
    }

    /// <summary>One tick: it follows, and its time goes on.</summary>
    internal void Tick()
    {
        MoveToHuman();
        _set?.Tick(_follow?.Invoke() ?? _made, _parameters?.Invoke() ?? []);
        Seconds += TickSeconds;
    }
}
