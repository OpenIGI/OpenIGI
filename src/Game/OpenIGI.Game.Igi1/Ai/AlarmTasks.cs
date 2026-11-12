using OpenIGI.Game.Level;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>A lamp that turns while an alarm sounds: Project I.G.I.'s <c>AlarmLight</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x43A270</c>) does nothing more once the light is shot out (<c>+417</c>). Until then it reads its
/// expression, changes to its on or off model as that turns, runs its children, and last runs the reflector inside it
/// (<c>+232</c>). The light is kept with the level's speakers (<see cref="AlarmSpeakerRegistry"/>), which also sound it.
/// </remarks>
public sealed class AlarmLightTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = WorldScene.AlarmLightType;

    /// <summary>Gets or sets the light this task is, or <see langword="null"/> until the level's speakers run it from the walk.</summary>
    internal AlarmSpeaker? Light { get; set; }

    /// <summary>Gets or sets the level's speakers.</summary>
    internal AlarmSpeakerRegistry? Speakers { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Light is not { } light || Speakers is not { } speakers)
        {
            level.RunChildren(this);
            return;
        }

        // Its sound first, by what it was as the handler began: a light that comes on is heard from the next tick.
        speakers.SoundLight(light);
        if (light.IsDestroyed)
        {
            // Nothing runs, the reflector neither: it only comes to rest where it stopped.
            speakers.RunReflectors(light);
            return;
        }

        speakers.RunLight(light);
        level.RunChildren(this);
        speakers.RunReflectors(light);
    }
}

/// <summary>A loudspeaker that wails while its expression holds: Project I.G.I.'s <c>Siren</c>, built on <c>ExplodeObject</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x4494D0</c>) runs the <c>ExplodeObject</c>'s first -- its children, then its blast -- and then
/// starts and stops its working and faulty loops, which the level's speakers do (<see cref="AlarmSpeakerRegistry"/>).
/// </remarks>
public sealed class SirenTask : DeclaredLevelTask, IExplodeObjectTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = WorldScene.SirenType;

    /// <inheritdoc/>
    public ExplodeObjectUpdate? Explosion { get; set; }

    /// <summary>Gets or sets the siren as the level's speakers keep it, or <see langword="null"/> until they do.</summary>
    internal AlarmSpeaker? Speaker { get; set; }

    /// <summary>Gets or sets the level's speakers.</summary>
    internal AlarmSpeakerRegistry? Speakers { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Explosion?.Run();
        if (Speaker is { } speaker)
        {
            Speakers?.SoundSiren(speaker);
        }
    }
}
