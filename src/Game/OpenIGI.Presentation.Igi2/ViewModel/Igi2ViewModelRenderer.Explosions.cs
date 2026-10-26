using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>Explosion0's FireBalls, drawn by the drawer both titles share with I.G.I. 2's own <c>1006_01_1</c>.</content>
public sealed partial class Igi2ViewModelRenderer
{
    private RetailExplosionEffects? _explosions;
    private bool _fireBallLoaded;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Explosion0_FireBallEnterHandler</c> is Project I.G.I.'s routine: the dome turned to face the eye, its size over 2048
    /// across, its depth growing over its life, its texture's animation stepped once through. The fragments are models of the
    /// level, drawn with the rest (<see cref="ExplosionPieceModels"/>).
    /// </remarks>
    public void DrawExplosions(IExplosionScene explosions, in Camera3D camera, in Matrix4f projection, float blend)
    {
        ArgumentNullException.ThrowIfNull(explosions);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_models is null || explosions.FireBalls.Count == 0)
        {
            return;
        }

        _explosions ??= new RetailExplosionEffects(_device, null);
        if (!_fireBallLoaded)
        {
            _fireBallLoaded = true;
            LoadFireBall(_explosions);
        }

        _explosions.Draw(explosions, camera, projection, blend);
    }

    // The FireBall's model from the level's packs, and its material's animated texture through the level's textures.
    private void LoadFireBall(RetailExplosionEffects explosions)
    {
        if (_models!(ExplosionFireBall.Model) is not { } model
            || MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre)) is not { } geometry)
        {
            _logger.Warning(LogCategory, $"The level holds no '{ExplosionFireBall.Model}'; its explosions are drawn without fire.");
            return;
        }

        MeshSubset? textured = geometry.Subsets.FirstOrDefault(static subset => subset.IndexCount > 0 && subset.HasMaterial);
        if (textured is not { } subset
            || !_table!.TryResolveTexture(ExplosionFireBall.Model, subset.MaterialSlot, out string? first)
            || first is null)
        {
            return;
        }

        TextureHandle[] frames = RetailExplosionEffects.AnimationFrames(
            first,
            name => _textures!.Resolve(Igi2ModelTable.TextureResourceName(name)));
        _ = explosions.UseFireBall(geometry, frames, RetailExplosionEffects.BlendOf(model));
        _logger.Information(LogCategory, $"FireBall '{ExplosionFireBall.Model}': {frames.Length} frame(s).");
    }

    private void ReleaseExplosions()
    {
        _explosions?.Release();
        _explosions = null;
        _fireBallLoaded = false;
    }
}
