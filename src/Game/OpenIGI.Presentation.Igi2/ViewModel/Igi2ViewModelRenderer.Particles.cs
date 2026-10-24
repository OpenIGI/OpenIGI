using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World.Particles;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

/// <content>The level's 2-D particles -- the blood of its wounded, its smoke, its trails -- drawn with I.G.I. 2's own sprites.</content>
public sealed partial class Igi2ViewModelRenderer
{
    private Particle2DRenderer? _particleDrawer;

    /// <inheritdoc/>
    /// <remarks>The engine and its drawer are the ones both titles share; the sprites are I.G.I. 2's (<c>common/sprites/sprites.res</c>).</remarks>
    public void DrawParticles(Particle2DModule particles, in Camera3D camera, in Matrix4f projection, float blend)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _particleDrawer ??= new Particle2DRenderer(_device, _resources.FileSystem);
        _particleDrawer.Draw(particles.Systems, camera, projection, blend);
    }

    // The drawer's sprites and mesh go with the renderer's other resources.
    private void ReleaseParticles()
    {
        _particleDrawer?.Dispose();
        _particleDrawer = null;
    }
}
