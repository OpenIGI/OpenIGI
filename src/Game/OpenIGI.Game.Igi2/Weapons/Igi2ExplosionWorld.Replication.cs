namespace OpenIGI.Game.Igi2.Weapons;

/// <content>Explosions told to a co-op client, and shown there as the server's went off.</content>
public sealed partial class Igi2ExplosionWorld
{
    private bool _replaying;

    /// <summary>Gets or sets what hears of each explosion this level sets off: a co-op server, to tell its clients.</summary>
    public Action<Igi2ExplosionRequest>? Started { get; set; }

    /// <summary>
    /// Sets off an explosion another machine set off, to be seen and heard here: its fire, its fragments, its mark and its sound,
    /// but no harm, no event the soldiers hear, and nothing told on.
    /// </summary>
    /// <param name="request">The explosion as the other machine started it.</param>
    /// <returns>The explosion.</returns>
    public Igi2Explosion Replay(Igi2ExplosionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _replaying = true;
        try
        {
            return Start(request with { Damage = 0f, Owner = null, WeaponType = null, AiEvent = -1 });
        }
        finally
        {
            _replaying = false;
        }
    }
}
