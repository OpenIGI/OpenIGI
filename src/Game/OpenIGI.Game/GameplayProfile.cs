namespace OpenIGI.Game;

/// <summary>A named set of gameplay-presentation settings, the third of the port's three profiles.</summary>
/// <remarks>
/// Deliberately separate from the graphics preset. A player who drops to <c>gfx_preset vanilla</c> for
/// frame rate is asking for a cheaper picture, not for the game as it shipped — and should not silently
/// lose ragdolls for it. <c>-vanilla</c> is what asks for the 2000 build, and that emits all three.
/// </remarks>
public enum GameplayProfile
{
    /// <summary>The settings do not correspond to any named profile.</summary>
    Custom,

    /// <summary>The original's behaviour: every remastered presentation change turned off.</summary>
    Vanilla,

    /// <summary>The remastered presentation.</summary>
    Enhanced,
}
