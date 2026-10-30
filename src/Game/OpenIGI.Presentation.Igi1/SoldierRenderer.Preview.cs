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
        if (_preview is not null && _previewModel == look)
        {
            return;
        }

        ReleasePreview();
        PlayerBodyRenderer body = new(this, _device, _sim, _logger, new SoldierPlayerBody());
        if (body.Load(lookMission, look, AnimationsFor(look, lookMission, player), feet, facing))
        {
            _preview = body;
            _previewModel = look;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Stood on the player's idle, the clip the player's own rig plays standing still.</remarks>
    public void TickPreviewBody()
    {
        if (_preview is not { } body)
        {
            return;
        }

        int idle = HumanLocomotionStates.For(HumanLocomotionStates.Idle).AnimationId;
        PlayerBodyPose still = new(-1, true, 0f, 0, 0f);
        body.Follow(_previewSpot.Feet, _previewSpot.Facing, idle, true, in still);
        body.Tick();
    }

    /// <inheritdoc/>
    public void DrawPreviewBody(in Camera3D camera, in Matrix4f projection)
    {
        if (_preview is { } body)
        {
            body.ResolveFrameState();
            body.Draw(in camera, in projection);
        }
    }

    private void ReleasePreview()
    {
        _preview?.Release();
        _preview = null;
        _previewModel = null;
    }
}
