using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws launcher missiles with the model named by their projectile task.</summary>
internal sealed class MissileRenderer : IMissilePresenter
{
    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly MagicObjectRegistry _magic;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, HeldWeaponRenderer?> _models = new(StringComparer.OrdinalIgnoreCase);
    private LoadedMission? _mission;
    private bool _disposed;

    public MissileRenderer(IGraphicsDevice device, ResourceRegistry resources, MagicObjectRegistry magic, IOpenIgiLogger? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _magic = magic ?? throw new ArgumentNullException(nameof(magic));
        _logger = logger ?? NullLogger.Instance;
    }

    public void Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mission = mission;
    }

    public void Draw(MissileSimulation missiles, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(missiles);
        if (_mission is null)
        {
            return;
        }

        foreach (LiveMissile missile in missiles.Missiles)
        {
            HeldWeaponRenderer? model = GetModel(missile.ModelName);
            if (model is null)
            {
                continue;
            }

            Vector3d position = new(
                missile.PreviousPosition.X + ((missile.Position.X - missile.PreviousPosition.X) * alpha),
                missile.PreviousPosition.Y + ((missile.Position.Y - missile.PreviousPosition.Y) * alpha),
                missile.PreviousPosition.Z + ((missile.Position.Z - missile.PreviousPosition.Z) * alpha));
            if (!ShownProjectile.TryAlongFlight(missile.Velocity, out Matrix3f rotation))
            {
                continue;
            }

            model.Sun = Sun;
            model.Draw(new BoneAttachment(position, rotation), camera, projection, false, 0, WeaponDrawParts.BodyOnly);
        }
    }

    /// <summary>Draws M203 rounds through the same dynamic projectile model path.</summary>
    public void Draw(M203Simulation projectiles, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(projectiles);
        if (_mission is null)
        {
            return;
        }

        foreach (M203ProjectileState projectile in projectiles.Projectiles)
        {
            HeldWeaponRenderer? model = GetModel(projectile.ModelName);
            if (model is null)
            {
                continue;
            }

            Vector3d position = new(
                projectile.PreviousPosition.X + ((projectile.Position.X - projectile.PreviousPosition.X) * alpha),
                projectile.PreviousPosition.Y + ((projectile.Position.Y - projectile.PreviousPosition.Y) * alpha),
                projectile.PreviousPosition.Z + ((projectile.Position.Z - projectile.PreviousPosition.Z) * alpha));
            // Retail 0x476670 stores the launch basis at +0x70 and 0x476340 never changes it.
            // Rebuilding a frame from the falling velocity made the nose pitch down in flight and
            // caused the rendered round to disagree with the native projectile task.
            model.Sun = Sun;
            model.Draw(
                new BoneAttachment(position, projectile.Orientation),
                camera,
                projection,
                false,
                0,
                WeaponDrawParts.BodyOnly);
        }
    }

    /// <summary>Draws what another game's weapons have in the world, each by its own model.</summary>
    public void Draw(IReadOnlyList<ShownProjectile> shown, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(shown);
        if (_mission is null)
        {
            return;
        }

        foreach (ShownProjectile projectile in shown)
        {
            if (GetModel(projectile.Model) is not { } model)
            {
                continue;
            }

            model.Sun = Sun;
            model.Draw(
                new BoneAttachment(Vector3d.Lerp(projectile.PreviousPosition, projectile.Position, alpha), projectile.Orientation),
                camera,
                projection,
                false,
                0,
                WeaponDrawParts.BodyOnly);
        }
    }

    /// <inheritdoc/>
    public FixedFunctionSun? Sun { get; set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (HeldWeaponRenderer? model in _models.Values)
        {
            model?.Dispose();
        }
        _models.Clear();
        _mission = null;
    }

    private HeldWeaponRenderer? GetModel(string modelName)
    {
        if (_models.TryGetValue(modelName, out HeldWeaponRenderer? existing))
        {
            return existing;
        }

        HeldWeaponRenderer renderer = new(_device, _resources, _magic, _logger);
        HeldWeaponRenderer? result = renderer.Load(_mission!, modelName) ? renderer : null;
        if (result is null)
        {
            renderer.Dispose();
            _logger.Warning("Missile", $"The mission's packs hold no missile model '{modelName}'; that missile flies unseen.");
        }
        _models[modelName] = result;
        return result;
    }
}
