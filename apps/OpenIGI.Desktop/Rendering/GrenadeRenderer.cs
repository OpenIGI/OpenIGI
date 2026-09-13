using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws every grenade in the air with its own retail model, tumbling as it flies.</summary>
/// <remarks>
/// <para>
/// The physics table names the models — <c>135_01_1</c> for the frag, <c>136_01_1</c> for the mine,
/// and <c>137_01_1</c> for the flashbang — and the multipart weapon renderer already knows how to load and draw one, so a
/// grenade in flight is the same machinery as a pickup on the ground, at a transform the simulation
/// supplies. Positions interpolate between ticks like everything else drawn, and the tumble runs on
/// the grenade's own moving-tick clock, so a settled grenade lies still on the floor it landed on
/// instead of spinning in place while its fuse runs down.
/// </para>
/// </remarks>
internal sealed class GrenadeRenderer : IDisposable
{
    /// <summary>How fast a flying grenade tumbles, in radians per tick.</summary>
    public const float TumbleRadiansPerTick = GrenadePhysics.TumbleRadiansPerTick;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly MagicObjectRegistry _magic;
    private readonly IIgiLogger _logger;
    private HeldWeaponRenderer? _frag;
    private HeldWeaponRenderer? _mine;
    private HeldWeaponRenderer? _flashbang;
    private bool _disposed;

    public GrenadeRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        MagicObjectRegistry magic,
        IIgiLogger? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _magic = magic ?? throw new ArgumentNullException(nameof(magic));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Loads the two grenade models out of a mission's packs.</summary>
    /// <param name="mission">The mission whose packs hold them.</param>
    public void Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _frag = LoadModel(mission, "135_01_1");
        _mine = LoadModel(mission, "136_01_1");
        _flashbang = LoadModel(mission, "137_01_1");
    }

    /// <summary>Draws every grenade in the pool.</summary>
    /// <param name="grenades">The simulation being drawn.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    public void Draw(GrenadeSimulation grenades, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(grenades);

        foreach (LiveGrenade grenade in grenades.Grenades)
        {
            HeldWeaponRenderer? model = grenade.IsProximityMine
                ? _mine ?? _frag
                : grenade.Type == GrenadeExplosionType.Flashbang
                    ? _flashbang ?? _frag
                    : _frag;
            if (model is null)
            {
                continue;
            }

            Vector3d position = PresentationBlend.Lerp(grenade.PreviousPosition, grenade.Position, alpha);

            // The native task owns the orientation after landing. The helper also covers a settled
            // object whose one-tick contact flag has already been cleared before this frame is drawn.
            Matrix3f tumble = GrenadePhysics.PresentationOrientation(grenade, alpha);

            model.Draw(
                new BoneAttachment(position, tumble),
                camera,
                projection,
                firedThisTick: false,
                shotSequence: 0,
                WeaponDrawParts.BodyOnly);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frag?.Dispose();
        _mine?.Dispose();
        _flashbang?.Dispose();
        _frag = null;
        _mine = null;
        _flashbang = null;
    }

    private HeldWeaponRenderer? LoadModel(LoadedMission mission, string model)
    {
        HeldWeaponRenderer renderer = new(_device, _resources, _magic, _logger);
        if (renderer.Load(mission, model))
        {
            return renderer;
        }

        _logger.Warning("Grenade", $"The packs hold no grenade model '{model}'; those grenades fly unseen.");
        renderer.Dispose();
        return null;
    }
}
