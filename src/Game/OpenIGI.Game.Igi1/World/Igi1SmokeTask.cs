using OpenIGI.Game.Level;

namespace OpenIGI.Game.Igi1.World;

/// <summary>A column of smoke: Project I.G.I.'s <c>Smoke</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x437DB0</c>) carries its particles along when it has moved, runs its children, then reads its
/// generate factor's expression and adds the particles that rate has earned, drawn from the plumes' own stream. The plume
/// is kept with the level's others (<see cref="Igi1SmokePlumes"/>); the task runs it where the level's walk reaches it.
/// </remarks>
public sealed class Igi1SmokeTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Smoke";

    /// <summary>Gets or sets the plume this task is, or <see langword="null"/> until the level's plumes run it from the walk.</summary>
    internal Igi1SmokePlume? Plume { get; set; }

    /// <summary>Gets or sets the level's plumes.</summary>
    internal Igi1SmokePlumes? Plumes { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Plume is not { } plume || Plumes is not { } plumes || !plumes.Prepare(plume))
        {
            level.RunChildren(this);
            return;
        }

        level.RunChildren(this);
        plumes.Emit(plume);
    }
}
