using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// One shot's flash at a weapon object's muzzle, I.G.I. 2's <c>MuzzleFlash</c> (<c>muzzleflash.c</c>): a child of the weapon that
/// fired it, made only when the gun names a front or a side sprite (<c>MuzzleFlash_New</c>, <c>igi2 0x4D12A0</c>).
/// </summary>
/// <remarks>
/// It starts at full strength. Once it has been drawn, each tick takes 0.3 off its strength and adds 0.02 to its size, and it is
/// deleted when no strength is left (<c>MuzzleFlash_RunHandler</c>): four ticks of light. It draws nothing at all without a front
/// sprite (<c>MuzzleFlash_EnterHandler</c>).
/// </remarks>
public sealed class Igi2MuzzleFlash
{
    /// <summary>The strength a drawn flash loses a tick.</summary>
    public const double Fade = 0.30000001192092896;

    /// <summary>The size a drawn flash gains a tick.</summary>
    public const float Growth = 0.019999999552965164f;

    /// <summary>Initializes a new instance of the <see cref="Igi2MuzzleFlash"/> class.</summary>
    /// <param name="weaponObject">Which of the holder's weapon objects fired, the bullets fired so far modulo two.</param>
    /// <param name="front">The texture seen down the barrel, or <see langword="null"/>.</param>
    /// <param name="side">The texture seen from the side, or <see langword="null"/>.</param>
    /// <param name="size">The gun's flash size.</param>
    public Igi2MuzzleFlash(int weaponObject, string? front, string? side, float size)
    {
        WeaponObject = weaponObject;
        Front = front;
        Side = side;
        Size = size;
    }

    /// <summary>Gets which of the holder's weapon objects it sits at.</summary>
    public int WeaponObject { get; }

    /// <summary>Gets the texture seen down the barrel, or <see langword="null"/>.</summary>
    public string? Front { get; }

    /// <summary>Gets the texture seen from the side, or <see langword="null"/>.</summary>
    public string? Side { get; }

    /// <summary>Gets its size, grown since it was made.</summary>
    public float Size { get; private set; }

    /// <summary>Gets its strength, one when made.</summary>
    public float Intensity { get; private set; } = 1f;

    /// <summary>Gets a value indicating whether it has been drawn, after which it fades.</summary>
    public bool IsDrawn { get; private set; }

    /// <summary>One tick of it, <c>MuzzleFlash_RunHandler</c> (<c>igi2 0x4D11F0</c>).</summary>
    /// <returns>Whether it is still there.</returns>
    public bool Run()
    {
        if (!IsDrawn)
        {
            return true;
        }

        double left = Intensity - Fade;
        Intensity = (float)left;
        Size += Growth;
        return left > 0.0;
    }

    /// <summary>What the flash draws as it stands, marking it drawn.</summary>
    /// <returns>The flash to draw, or <see langword="null"/> when it draws nothing.</returns>
    public TitleMuzzleFlash? Present()
    {
        IsDrawn = true;
        return Front is null ? null : new TitleMuzzleFlash(TextureName(Front), Side is null ? null : TextureName(Side), Size, Intensity);
    }

    // A gun names its sprites bare, from the shared textures.
    private static string TextureName(string name) =>
        name.Contains(':', StringComparison.Ordinal) ? name : "LOCAL:common/textures/" + name;
}

/// <summary>A weapon's flashes, its <c>MuzzleFlash</c> children, run with it.</summary>
public sealed class Igi2MuzzleFlashes
{
    private readonly List<Igi2MuzzleFlash> _flashes = [];

    /// <summary>Gets the flashes still there, oldest first.</summary>
    public IReadOnlyList<Igi2MuzzleFlash> Active => _flashes;

    /// <summary>Adds a flash.</summary>
    /// <param name="flash">The flash.</param>
    public void Add(Igi2MuzzleFlash flash)
    {
        ArgumentNullException.ThrowIfNull(flash);
        _flashes.Add(flash);
    }

    /// <summary>Runs each flash, dropping those that have faded.</summary>
    public void Run() => _flashes.RemoveAll(static f => !f.Run());

    /// <summary>The flashes at a weapon object, each marked drawn.</summary>
    /// <param name="weaponObject">The weapon object.</param>
    /// <returns>What they draw, oldest first; <see langword="null"/> for none.</returns>
    public IReadOnlyList<TitleMuzzleFlash>? Present(int weaponObject)
    {
        List<TitleMuzzleFlash>? shown = null;
        foreach (Igi2MuzzleFlash flash in _flashes)
        {
            if (flash.WeaponObject == weaponObject && flash.Present() is { } drawn)
            {
                (shown ??= []).Add(drawn);
            }
        }

        return shown;
    }
}
