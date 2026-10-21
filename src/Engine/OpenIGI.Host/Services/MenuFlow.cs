using OpenIGI.Core.Graphics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI;

namespace OpenIGI.Host.Services;

/// <summary>
/// Where the menu stands: whether it is up and over what, which backdrop it is showing or waiting for, and the
/// cross-dissolve that carries one backdrop into the next.
/// </summary>
/// <param name="device">The device the outgoing backdrop's last frame is held on.</param>
/// <remarks>
/// The backdrop's level itself is <see cref="MenuBackdrop"/>'s. What is kept here is the flow around it: the
/// requests that pick the next level, the hold that catches the outgoing one, and the dissolve between the two.
/// </remarks>
internal sealed class MenuFlow(IGraphicsDevice device) : IHostService
{
    // The last frame of the outgoing backdrop, held on the GPU so it can be dissolved off over the incoming one.
    // Sized to the window and made again when that changes.
    private TextureHandle _heldFrame;
    private (int Width, int Height) _heldFrameSize;

    /// <inheritdoc/>
    public string Name => "Menu flow";

    /// <summary>Gets or sets whether the shell has the frame's input and draw.</summary>
    public bool MenuMode { get; set; }

    /// <summary>Gets or sets whether the shell is up over a live mission -- the pause menu -- rather than the showcase.</summary>
    /// <remarks>
    /// It always implies <see cref="MenuMode"/>; what it adds is that the backdrop is already the right picture. So
    /// nothing is streamed in behind the menu, the showcase orbit does not take the camera off the player, and the
    /// world is frozen instead of advanced. Escape opens it and Escape closes it again.
    /// </remarks>
    public bool PauseMenu { get; set; }

    /// <summary>Gets whether the menu is up over the showcase, where backdrops are built, swapped and dissolved.</summary>
    public bool OverShowcase => MenuMode && !PauseMenu;

    /// <summary>Gets or sets whether the showcase's first backdrop is still to be asked for.</summary>
    public bool ShowcasePending { get; set; }

    /// <summary>Gets or sets the mission the showcase is on, or was last asked for.</summary>
    public int CurrentShowcaseMissionId { get; set; }

    /// <summary>Gets or sets the mission a preview was asked for while the selection was still moving.</summary>
    public int? PendingPreviewMissionId { get; set; }

    /// <summary>Gets or sets when, on the loop's clock, <see cref="PendingPreviewMissionId"/> was last changed.</summary>
    public int PreviewRequestedAtMs { get; set; }

    /// <summary>Gets or sets whether the frame on screen is to be held at the end of this pass's scene.</summary>
    /// <remarks>
    /// Set on the frame the backdrop is to be held and cleared once the hold has happened; the swap then runs on the
    /// frame after, so what was captured is the OLD level.
    /// </remarks>
    public bool HoldFrameWanted { get; set; }

    /// <summary>Gets or sets whether a hold has happened and the swap it was made for may now run.</summary>
    public bool HeldFrameReady { get; set; }

    /// <summary>Gets or sets whether what was held is a picture, rather than the dark clear before the first backdrop.</summary>
    /// <remarks>The very first backdrop has no outgoing level to hold, so there is nothing to dissolve from and it simply appears.</remarks>
    public bool HeldFrameIsPicture { get; set; }

    /// <summary>Gets or sets whether the held frame is waiting for the incoming level's lighting to converge before it dissolves off.</summary>
    public bool DissolveRevealPending { get; set; }

    /// <summary>Gets or sets whether every other mission's files have been asked to be read through in the background.</summary>
    public bool MissionsSwept { get; set; }

    /// <summary>Gets or sets how many backdrops have been swapped in, for a soak's memory reports.</summary>
    public int BackdropSwaps { get; set; }

    /// <summary>Gets or sets a field that finished baking for the backdrop, waiting for the frame after the hold to be adopted.</summary>
    public DistanceFieldBake.Result? BakedFieldForBackdrop { get; set; }

    /// <summary>Gets or sets which row -backdropcycle is on, so each is asked for once rather than on every frame past its mark.</summary>
    public long BackdropCycleRow { get; set; } = -1;

    /// <summary>Gets or sets which step -presetcycle is on, latched the same way.</summary>
    public long PresetCycleStep { get; set; } = -1;

    /// <summary>Gets the backdrop cross-dissolve.</summary>
    /// <remarks>
    /// 1 is the outgoing level's held frame fully covering the incoming one, 0 is the incoming level alone; it runs on
    /// an eased tween on real elapsed time. There is no dip through black any more: the two pictures blend directly
    /// into each other.
    /// </remarks>
    public Tween Dissolve { get; } = new(0f, duration: 0.42f, Ease.InOutQuad);

    /// <summary>Gets the held frame, which the loading sequence also draws its backdrop from.</summary>
    public TextureHandle HeldFrame => _heldFrame;

    /// <summary>Puts the flow back to a front end that has not shown anything yet.</summary>
    /// <param name="showcaseMissionId">The mission the showcase opens on.</param>
    public void Reset(int showcaseMissionId)
    {
        MenuMode = false;
        PauseMenu = false;
        ShowcasePending = false;
        CurrentShowcaseMissionId = showcaseMissionId;
        PendingPreviewMissionId = null;
        PreviewRequestedAtMs = 0;
        HoldFrameWanted = false;
        HeldFrameReady = false;
        MissionsSwept = false;
        BackdropSwaps = 0;
        HeldFrameIsPicture = false;
        DissolveRevealPending = false;
        BakedFieldForBackdrop = null;
    }

    /// <summary>Records that the wanted hold has happened.</summary>
    /// <param name="picture">Whether a level was on screen to be held, rather than the dark clear.</param>
    public void CompleteHold(bool picture)
    {
        HoldFrameWanted = false;
        HeldFrameReady = true;
        HeldFrameIsPicture = picture;
    }

    /// <summary>Grabs the frame just rendered into the held frame, sizing it to the window first.</summary>
    /// <param name="width">The window's width.</param>
    /// <param name="height">The window's height.</param>
    /// <remarks>
    /// Called after the 3D pass and BEFORE the menu is drawn over it, so what is held is the outgoing level alone.
    /// Catching it after the menu would put a second copy of the menu in the texture and ghost it against the live
    /// one for the length of the dissolve.
    /// </remarks>
    public void HoldFrame(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_heldFrameSize != (width, height))
        {
            ReleaseHeldFrame();
            _heldFrame = device.CreateTexture(
                new TextureDescription(width, height, 1, TextureFilter.Linear, TextureAddressMode.Clamp),
                new Bgra32[width * height]);
            _heldFrameSize = (width, height);
        }

        device.CopyFrameToTexture(_heldFrame, width, height);
    }

    /// <summary>Draws the held frame over the live one, so the two cross-dissolve.</summary>
    /// <param name="painter">The shell's painter, which draws over the resolved scene and under the menu.</param>
    /// <param name="width">The window's width.</param>
    /// <param name="height">The window's height.</param>
    /// <remarks>
    /// The renderer keeps one live world, so two levels can never be on screen at once. What can be is the last
    /// <em>frame</em> of the outgoing one: it is held as a texture and faded off over the incoming level, which is a
    /// true dissolve rather than a dip through black. The camera's orbit is slow enough that a still is
    /// indistinguishable from the live picture over a third of a second.
    /// </remarks>
    public void DrawDissolve(UiPainter painter, int width, int height)
    {
        float amount = Dissolve.Value;
        if (amount <= 0.002f || !_heldFrame.IsValid)
        {
            return;
        }

        painter.DrawSprite(
            _heldFrame,
            new UiRect(0f, 0f, width, height),
            RectF.Unit,
            Bgra32.White.WithAlpha((byte)(255f * Math.Clamp(amount, 0f, 1f))));
    }

    /// <summary>Frees the held frame, which the next hold makes again.</summary>
    public void ReleaseHeldFrame()
    {
        if (_heldFrame.IsValid)
        {
            device.DestroyTexture(_heldFrame);
        }

        _heldFrame = default;
        _heldFrameSize = default;
    }

    /// <inheritdoc/>
    public void Dispose() => ReleaseHeldFrame();
}
