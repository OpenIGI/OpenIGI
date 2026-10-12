namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// The laser designator's module state (laserdesignator.c, laserdesignatoroverlay.c): what its dot rests on and since when, and
/// whether its overlay shows a lock, which the level's scripts read and set to call in a strike.
/// </summary>
public sealed class Igi2LaserDesignatorState
{
    /// <summary>Gets or sets the task the dot rests on, <c>LaserDesignator_ptHitQTask</c>, or <see langword="null"/>.</summary>
    public object? HitTask { get; set; }

    /// <summary>Gets or sets the level tick the dot first came to rest on it, <c>LaserDesignator_nTickFirstHit</c>.</summary>
    public int TickFirstHit { get; set; }

    /// <summary>Gets or sets a value indicating whether the overlay shows a lock, <c>LaserDesignatorOverlay_IsLockedOn</c>.</summary>
    public bool IsLockedOn { get; set; }

    /// <summary>Forgets the dot and the lock, as a level starts.</summary>
    public void Reset()
    {
        HitTask = null;
        TickFirstHit = 0;
        IsLockedOn = false;
    }
}
