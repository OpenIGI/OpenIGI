using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>A look shown standing in the level behind the menu, for a player choosing how to appear in co-op.</summary>
public sealed partial class SoldierRenderer
{
    // The player's stand (clip 2) is one held pose, a statue; this is one of its slow cyclic idles, twenty seconds of
    // breathing and weight shifting.
    private const int PreviewIdleAnimation = 203;

    private PlayerBodyRenderer? _preview;
    private string? _previewModel;
    private (Vector3d Feet, float Facing) _previewSpot;

    /// <inheritdoc/>
    /// <remarks>
    /// The same body a partner gets -- its look's model, the player's clips on its own skeleton -- idling where it is put.
    /// A model this level doesn't ship is borrowed as a partner's is (<see cref="BorrowLevel"/>).
    /// </remarks>
    public void ShowPreviewBody(LoadedMission mission, string? model, Vector3d feet, float facing)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (model is null
            || mission.PacksInLoadOrder.SelectMany(static p => p.AnimationSets).FirstOrDefault(static s => s.Skeleton.Id == 0) is not { } player)
        {
            ReleasePreview();
            return;
        }

        _previewSpot = (feet, facing);
        (LoadedMission lookMission, string look) = ResolveLook(mission, model, OpenIGI.Game.Multiplayer.PlayerSkins.Default.Model);
        if (_preview is null || _previewModel != look)
        {
            ReleasePreview();
            PlayerBodyRenderer body = new(this, _device, _sim, _logger, new SoldierPlayerBody());
            if (!body.Load(lookMission, look, AnimationsFor(look, lookMission, player), feet, facing))
            {
                return;
            }

            _preview = body;
            _previewModel = look;
        }

        // Stood where it is asked to every frame, so a turn follows the drag rather than the tick.
        StandPreview();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing else in this renderer ticks -- no mission runs behind the menu -- so the tick its bodies pose once per is
    /// the preview's own, counted here.
    /// </remarks>
    public void TickPreviewBody()
    {
        _sim.TickCount++;
        StandPreview();
    }

    /// <inheritdoc/>
    public void DrawPreviewBody(in Camera3D camera, in Matrix4f projection) => _preview?.Draw(in camera, in projection);

    // On a slow breathing idle of the player's own: posed once a tick, placed every call.
    private void StandPreview()
    {
        PlayerBodyPose still = new(-1, true, 0f, 0, 0f);
        _preview?.Follow(_previewSpot.Feet, _previewSpot.Facing, PreviewIdleAnimation, true, in still);
    }

    private void ReleasePreview()
    {
        _preview?.Release();
        _preview = null;
        _previewModel = null;
    }
}
