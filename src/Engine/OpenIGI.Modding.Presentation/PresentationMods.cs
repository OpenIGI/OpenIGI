using OpenIGI.Core.Titles;
using OpenIGI.Presentation;

namespace OpenIGI.Modding.Presentation;

/// <summary>A mod's code that draws: what it adds to a game's presenters, said once, before the game starts.</summary>
/// <remarks>
/// Implemented beside <see cref="IOpenIgiMod"/> by the same class. <see cref="ConfigurePresentation"/> runs once for each game
/// the mod applies to, after <see cref="IOpenIgiMod.Configure"/>; a build that draws nothing never calls it.
/// </remarks>
public interface IOpenIgiPresentationMod
{
    /// <summary>Says what the mod draws in one game.</summary>
    /// <param name="title">The game.</param>
    /// <param name="presenters">The game's presenters, to add to or replace.</param>
    void ConfigurePresentation(GameTitle title, PresenterRegistry presenters);
}

/// <summary>Applies the mods' presentation to a game's presenters.</summary>
public static class PresentationMods
{
    /// <summary>Lets every mod for a game that draws add to its presenters, in the order the mods were loaded.</summary>
    /// <param name="mods">The mods.</param>
    /// <param name="title">The game.</param>
    /// <param name="presenters">The game's presenters.</param>
    /// <returns>How many mods added to them.</returns>
    public static int Apply(ModSet mods, GameTitle title, PresenterRegistry presenters)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(presenters);
        int applied = 0;
        foreach (InstalledMod mod in mods.For(title))
        {
            if (mod.Code is IOpenIgiPresentationMod drawing)
            {
                drawing.ConfigurePresentation(title, presenters);
                applied++;
            }
        }

        return applied;
    }
}
