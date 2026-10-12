using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Audio;

/// <summary>
/// The game's microphone (microphone.c): it hears from where the game's camera is, and stands in the sector the camera stands in
/// (<c>Microphone_RunHandler</c> copies the camera's place and its sector each tick), worked out once a tick.
/// </summary>
/// <param name="position">Where the camera is.</param>
/// <param name="locator">Which sector a point stands in, or <see langword="null"/> when the level has none.</param>
/// <param name="tick">The game's tick, which the sector is worked out once in.</param>
public sealed class Igi2CameraListener(Func<Vector3d> position, Igi2SectorLocator? locator, Func<int> tick) : IIgi2Listener
{
    private int _insideTick = int.MinValue;
    private bool _inside;

    /// <inheritdoc/>
    public Vector3d Position => position();

    /// <inheritdoc/>
    public bool IsInside
    {
        get
        {
            int now = tick();
            if (now != _insideTick)
            {
                _insideTick = now;
                _inside = locator?.SectorAt(position()) is not null;
            }

            return _inside;
        }
    }
}
