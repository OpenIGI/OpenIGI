using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Titles;

namespace OpenIGI.Engine.Titles;

/// <summary>Where each game the player has installed keeps its data, so content of one game can read another's.</summary>
/// <remarks>
/// The host provides it to every session. A mod that mounts another game's data (<c>IModBuilder.MountTitleData</c>) asks it
/// when the session loads its tables, and goes without when that game is not installed.
/// </remarks>
public interface ITitleDataLocator
{
    /// <summary>Finds where a game's data is installed.</summary>
    /// <param name="title">The game.</param>
    /// <param name="dataRoot">Receives the installation's directory.</param>
    /// <returns><see langword="true"/> when the game is installed.</returns>
    bool TryGetDataRoot(GameTitle title, [NotNullWhen(true)] out string? dataRoot);
}
