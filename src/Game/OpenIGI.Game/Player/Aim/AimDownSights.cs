namespace OpenIGI.Game.Player.Aim;

/// <summary>A player's aim down the sights: whether it is wanted, and how far the weapon has come up to the eye.</summary>
/// <remarks>
/// <para>
/// Stepped once a simulation tick, from the tick's input alone, so a server and its client and a replay all agree on it. The
/// progress runs linearly from nought (at the hip) to one (on the sights) over the profile's
/// <see cref="AdsProfile.InSeconds"/>, and back over <see cref="AdsProfile.OutSeconds"/>; play reads it as it is -- the cone
/// tightens as the weapon comes up -- and the picture through <see cref="Shape"/>, which eases both ends.
/// </para>
/// <para>
/// Wanting the sights and having them are kept apart. A reload, a weapon change, a jump or the peek takes the weapon down
/// (<c>canAim</c> false) without forgetting the button, so a player still holding it -- or one who toggled the sights on --
/// is back on them the moment the weapon is ready again. A new weapon forgets a toggle.
/// </para>
/// </remarks>
public sealed class AimDownSights
{
    /// <summary>The length of one simulation tick, in seconds.</summary>
    public const float TickSeconds = 1f / 30f;

    /// <summary>Gets the profile of the weapon the aim is for, or <see langword="null"/> when the weapon cannot be aimed.</summary>
    public AdsProfile? Profile { get; private set; }

    /// <summary>Gets how far the weapon has come up to the eye, nought to one, as of the last tick.</summary>
    public float Progress { get; private set; }

    /// <summary>Gets <see cref="Progress"/> as of the tick before, for a frame drawn between the two.</summary>
    public float PreviousProgress { get; private set; }

    /// <summary>Gets a value indicating whether the player wants the sights: the button held, or toggled on.</summary>
    public bool Wanted { get; private set; }

    /// <summary>Gets a value indicating whether the weapon is going up to the sights, or is on them.</summary>
    public bool IsRaising { get; private set; }

    /// <summary>Gets a value indicating whether the weapon is all the way up.</summary>
    public bool IsOnSights => Progress >= 1f;

    /// <summary>Takes the weapon down at once and forgets the button: a death, a teleport, modern aim turned off.</summary>
    public void Reset()
    {
        Profile = null;
        Progress = 0f;
        PreviousProgress = 0f;
        Wanted = false;
        IsRaising = false;
    }

    /// <summary>Steps the aim one tick.</summary>
    /// <param name="options">Modern aim's settings this tick.</param>
    /// <param name="profile">The profile of the weapon in hand, or <see langword="null"/> when it cannot be aimed.</param>
    /// <param name="aimHeld">Whether the aim button is held this tick.</param>
    /// <param name="aimPressed">Whether it went down this tick.</param>
    /// <param name="canAim">Whether the weapon can be up at the eye now: ready, on the ground, not leaning out with the peek.</param>
    public void Tick(in ModernAimOptions options, AdsProfile? profile, bool aimHeld, bool aimPressed, bool canAim)
    {
        PreviousProgress = Progress;
        if (!options.Enabled || profile is null)
        {
            Profile = profile;
            Wanted = false;
            IsRaising = false;
            Progress = 0f;
            return;
        }

        if (!ReferenceEquals(profile, Profile))
        {
            Profile = profile;
            Wanted = false;
        }

        Wanted = options.Toggle ? Wanted ^ aimPressed : aimHeld;
        IsRaising = Wanted && canAim;
        Progress = IsRaising
            ? MathF.Min(1f, Progress + (TickSeconds / MathF.Max(TickSeconds, profile.InSeconds)))
            : MathF.Max(0f, Progress - (TickSeconds / MathF.Max(TickSeconds, profile.OutSeconds)));
    }

    /// <summary>Gets the progress for a frame drawn between the last two ticks.</summary>
    /// <param name="alpha">How far the frame sits between them, nought to one.</param>
    /// <returns>The blended progress.</returns>
    public float Smoothed(float alpha) =>
        PreviousProgress + ((Progress - PreviousProgress) * Math.Clamp(alpha, 0f, 1f));

    /// <summary>The picture's easing of a progress: slow to leave the hip, slow to settle on the sights.</summary>
    /// <param name="progress">A progress, nought to one.</param>
    /// <returns>The eased amount, nought to one; the same curve up and down, so a change of mind reverses smoothly.</returns>
    public static float Shape(float progress)
    {
        float t = Math.Clamp(progress, 0f, 1f);
        return t * t * t * ((t * ((t * 6f) - 15f)) + 10f);
    }

    /// <summary>A hip value blended towards its aimed value by a progress.</summary>
    /// <param name="hip">The value at the hip.</param>
    /// <param name="aimed">The value on the sights.</param>
    /// <param name="progress">The progress, nought to one.</param>
    /// <returns>The blend.</returns>
    public static float Blend(float hip, float aimed, float progress) => hip + ((aimed - hip) * Math.Clamp(progress, 0f, 1f));
}
