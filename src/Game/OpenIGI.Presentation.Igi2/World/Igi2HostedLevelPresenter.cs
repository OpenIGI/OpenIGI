using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Presentation.Igi2.ViewModel;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.World;

/// <summary>Draws a hosted level's I.G.I. 2 soldiers as I.G.I. 2 draws its own: each body posed by its skeleton, its weapon in hand.</summary>
/// <remarks>
/// The bodies are I.G.I. 2's world models (<see cref="Igi2HostedLevel.Models"/>) of the soldiers in the world, as the tick last
/// posed them, read from the I.G.I. 2 packs the guest content opened and drawn by I.G.I. 2's own renderer of them, which skins
/// them all in one batch. Both games measure 4,096 units to the metre, so the bodies stand in the host's world as in their own.
/// </remarks>
public sealed class Igi2HostedLevelPresenter : ITaskPresenter
{
    private readonly Igi2HostedLevel _level;
    private readonly Igi2ViewModelRenderer _renderer;
    private float _blend = 1f;

    /// <summary>Initializes a new instance of the <see cref="Igi2HostedLevelPresenter"/> class.</summary>
    /// <param name="device">The device to draw with.</param>
    /// <param name="content">Where the models and their textures are read from.</param>
    /// <param name="level">The hosted level, whose soldiers are drawn.</param>
    /// <param name="logger">Where problems go.</param>
    public Igi2HostedLevelPresenter(IGraphicsDevice device, Igi2GuestContent content, Igi2HostedLevel level, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(content);
        _level = level ?? throw new ArgumentNullException(nameof(level));
        _renderer = new Igi2ViewModelRenderer(device, content.Guest.Resources, logger);
        _renderer.Load(content.Packs);
    }

    /// <inheritdoc/>
    public void Draw(in Camera3D camera, in Matrix4f projection, float blend)
    {
        _blend = blend;
        _renderer.DrawWorld(_level.Models(), camera, projection, blend);
    }

    /// <inheritdoc/>
    public void DrawOutlines(IReadOnlyDictionary<object, Bgra32> marked, in Camera3D camera, float radiusPixels) =>
        _renderer.DrawWorldOutlines(_level.Models(), marked, camera, radiusPixels, _blend);

    /// <inheritdoc/>
    /// <remarks>The soldiers stand in the level's light like its own: they shadow the ground under them and the lamps.</remarks>
    public bool CastsShadow => true;

    /// <inheritdoc/>
    public void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection) => _renderer.DrawWorldMuzzleFlashes(camera, projection);

    /// <inheritdoc/>
    /// <remarks>
    /// The level's particles, whichever player stands in it: a guest player's own presenter leaves them to this one, so they
    /// are drawn once.
    /// </remarks>
    public void DrawParticles(in Camera3D camera, in Matrix4f projection, float blend)
    {
        if (_level.Particles is { } particles)
        {
            _renderer.DrawParticles(particles, camera, projection, blend);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _renderer.Dispose();
}
