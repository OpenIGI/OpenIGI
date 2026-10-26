using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws the weapon models the level's live <c>GunPickup</c> tasks place in the world.</summary>
/// <remarks>
/// Retail <c>GunPickup::PostLoad</c> resolves the roster's <c>Gun Model</c> and creates a normal rigid
/// world object for it. Reusing the multipart weapon renderer keeps attached slides, magazines and
/// authored textures together, while a synthetic attachment supplies the pickup task's world transform.
/// </remarks>
internal sealed class GunPickupRenderer : IGunPickupPresenter
{
    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly MagicObjectRegistry _magic;
    private readonly IOpenIgiLogger _logger;
    private readonly List<Entry> _entries = [];
    private bool _disposed;

    public GunPickupRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        MagicObjectRegistry magic,
        IOpenIgiLogger? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _magic = magic ?? throw new ArgumentNullException(nameof(magic));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets or sets the modern surface descriptions applied to pickup models.</summary>
    public MaterialLibrary Materials { get; set; } = MaterialLibrary.Empty;

    /// <summary>Loads every resolved pickup model in a level.</summary>
    public void Load(LoadedMission mission, GunPickupRegistry pickups)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(pickups);
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (GunPickup pickup in pickups.Items)
        {
            Add(mission, pickup);
        }
    }

    /// <summary>Loads one runtime drop after the level's initial pickup list was built.</summary>
    public void Add(LoadedMission mission, GunPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(pickup);
        ObjectDisposedException.ThrowIf(_disposed, this);

        HeldWeaponRenderer weapon = new(_device, _resources, _magic, _logger) { Materials = Materials };
        if (!weapon.Load(mission, pickup.Weapon.GunModel))
        {
            weapon.Dispose();
            return;
        }

        if (pickup.IsDynamic && weapon.CreatePickupCollider() is { } collider)
        {
            pickup.UseCollider(collider);
        }

        _entries.Add(new Entry(pickup, weapon));
    }

    /// <summary>Prepares the interpolated pickup transforms for one rendered frame.</summary>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (Entry entry in _entries)
        {
            entry.Pickup.Present(tickFraction, smooth);
        }
    }

    /// <summary>Draws every pickup that has not been consumed.</summary>
    public void Draw(in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (Entry entry in _entries)
        {
            if (!entry.Pickup.IsCollected)
            {
                entry.Renderer.Sun = Sun;
                entry.Renderer.Draw(
                    new BoneAttachment(entry.Pickup.DrawPosition, entry.Pickup.DrawOrientation),
                    camera,
                    projection,
                    firedThisTick: false,
                    shotSequence: 0);
            }
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
        foreach (Entry entry in _entries)
        {
            entry.Renderer.Dispose();
        }

        _entries.Clear();
    }

    private sealed record Entry(GunPickup Pickup, HeldWeaponRenderer Renderer);
}
