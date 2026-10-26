using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Systems;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// Draws Project I.G.I.'s soldiers in another game's level: the game's own soldier renderer over the hosted level's simulation,
/// its models and textures read from the game's install.
/// </summary>
/// <remarks>
/// The hosted level runs the soldiers (<see cref="Igi1HostedLevel.Tick"/>); the renderer follows each tick it closes, and draws
/// the bodies between the last two as the game's own missions draw theirs.
/// </remarks>
public sealed class Igi1HostedLevelPresenter : ITaskPresenter
{
    private readonly SoldierRenderer _renderer;

    /// <summary>Initializes a new instance of the <see cref="Igi1HostedLevelPresenter"/> class.</summary>
    /// <param name="device">The device to draw with.</param>
    /// <param name="content">The game's content the soldiers are made of.</param>
    /// <param name="level">The hosted level.</param>
    /// <param name="logger">Where problems are reported.</param>
    public Igi1HostedLevelPresenter(IGraphicsDevice device, Igi1GuestContent content, Igi1HostedLevel level, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(level);
        MagicObjectRegistry? magic = content.Guest.Session.Services.TryGet(out MagicObjectRegistry? table) ? table : null;
        _renderer = new SoldierRenderer(device, content.Guest.Resources, logger, magic, simulation: level.Soldiers);
        _renderer.Present(content.Mission, level.Groups);
        _renderer.Arm(content.Mission, content.Arsenal.Weapons);
    }

    /// <inheritdoc/>
    public bool CastsShadow => true;

    /// <inheritdoc/>
    public void Draw(in Camera3D camera, in Matrix4f projection, float blend)
    {
        _renderer.ObserveView(camera, projection);
        _renderer.BeginFrame(blend, smooth: true);
        _renderer.DrawGroundShadows(camera, projection);
        _renderer.Draw(camera, projection);
    }

    /// <inheritdoc/>
    public void DrawMuzzleFlashes(in Camera3D camera, in Matrix4f projection) => _renderer.DrawMuzzleFlashes(camera, projection);

    /// <inheritdoc/>
    public void Dispose() => _renderer.Dispose();
}
