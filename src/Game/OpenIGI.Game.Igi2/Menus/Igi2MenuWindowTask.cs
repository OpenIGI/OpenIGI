using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Menus;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Menus;

/// <summary>A window of I.G.I. 2's menu as it takes its data in and gives it back, <c>MenuWindow</c>'s init and change handlers.</summary>
/// <remarks>Every I.G.I. 2 menu window is one, whichever shared window it is: the menu reaches its windows' data through this.</remarks>
public interface IIgi2MenuData
{
    /// <summary>Takes its data in, then its windows do (<c>MenuWindow_InitDataHandler</c>).</summary>
    /// <param name="level">The level its scripts are evaluated in.</param>
    void InitData(Igi2LevelRun level);

    /// <summary>Gives its data back, then its windows do (<c>MenuWindow_ChangeDataHandler</c>).</summary>
    /// <param name="level">The level its scripts are evaluated in.</param>
    void ChangeData(Igi2LevelRun level);
}

/// <summary>What every I.G.I. 2 menu window's data handlers share: the walk into its windows, and its scripts' evaluation.</summary>
public static class Igi2MenuData
{
    /// <summary>Has every window under a window take its data in.</summary>
    /// <param name="window">The window.</param>
    /// <param name="level">The level its scripts are evaluated in.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void InitChildren(GameTask window, Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(level);
        foreach (IIgi2MenuData child in window.Children.OfType<IIgi2MenuData>())
        {
            child.InitData(level);
        }
    }

    /// <summary>Has every window under a window give its data back.</summary>
    /// <param name="window">The window.</param>
    /// <param name="level">The level its scripts are evaluated in.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void ChangeChildren(GameTask window, Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(level);
        foreach (IIgi2MenuData child in window.Children.OfType<IIgi2MenuData>())
        {
            child.ChangeData(level);
        }
    }

    /// <summary>Runs one of a window's scripts, compiling it the first time.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="compiled">The script compiled, kept by the window.</param>
    /// <param name="source">Its source.</param>
    /// <param name="owner">The window, which the script's <c>this</c> names.</param>
    /// <param name="value">Receives its value.</param>
    /// <returns><see langword="false"/> for an empty script.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static bool TryRun(Igi2LevelRun level, ref LevelExpression? compiled, string source, GameTask owner, out double value)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(owner);
        compiled ??= Igi2LevelExpressions.Create(source, owner.Name, level.Natives);
        if (compiled.IsEmpty)
        {
            value = 0.0;
            return false;
        }

        value = level.Evaluate(compiled);
        return true;
    }
}

/// <summary>An I.G.I. 2 menu window not yet made of a shared control: its data handlers walk into its windows.</summary>
public abstract class Igi2MenuWindowTask : MenuWindowTask, IIgi2MenuData
{
    /// <inheritdoc/>
    public virtual void InitData(Igi2LevelRun level) => Igi2MenuData.InitChildren(this, level);

    /// <inheritdoc/>
    public virtual void ChangeData(Igi2LevelRun level) => Igi2MenuData.ChangeChildren(this, level);

    /// <summary>Runs one of this window's scripts, compiling it the first time.</summary>
    /// <param name="level">The level it is evaluated in.</param>
    /// <param name="compiled">The script compiled, kept by the window.</param>
    /// <param name="source">Its source.</param>
    /// <param name="value">Receives its value.</param>
    /// <returns><see langword="false"/> for an empty script.</returns>
    protected bool TryRun(Igi2LevelRun level, ref LevelExpression? compiled, string source, out double value) =>
        Igi2MenuData.TryRun(level, ref compiled, source, this, out value);
}
