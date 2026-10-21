namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The base animation each of the player's states plays, <c>HumanPlayer_atStateAnimData[n].iWeaponTypeAnim</c>, as
/// <c>HumanPlayer_InitStateTable</c> (<c>igi2 0x49EE00</c>) fills it.
/// </summary>
/// <remarks>
/// Slots without a state of their own still name an animation: the short hits (58–64), the hit and death rows
/// (81–82, 92–107) and the weapon raise and lower (87–91), which other code plays by slot.
/// </remarks>
public static class Igi2PlayerStateAnimations
{
    /// <summary>The table, by state slot.</summary>
    public static IReadOnlyList<int> Table { get; } =
    [
        60, 1, 61, 5, 9, 8, 10, 12, 11, 7, // 0
        6, 13, 17, 16, 18, 20, 19, 15, 14, 229, // 10
        230, 231, 232, 100, 119, 101, 102, 106, 105, 107, // 20
        109, 108, 104, 103, 100, 193, 52, 0, 0, 0, // 30
        0, 21, 0, 0, 0, 136, 137, 138, 139, 140, // 40
        141, 142, 0, 100, 143, 215, 0, 0, 170, 215, // 50
        215, 176, 173, 167, 164, 215, 215, 215, 117, 54, // 60
        55, 57, 56, 58, 21, 120, 121, 122, 123, 124, // 70
        128, 126, 187, 78, 79, 80, 81, 233, 234, 60, // 80
        235, 236, 215, 216, 217, 218, 219, 220, 209, 210, // 90
        199, 200, 201, 202, 203, 204, 228, 237, 0, // 100
    ];
}
