using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Services;

/// <summary>
/// The level's distance field as the real-time lighting model holds it: read from the cache or baked on
/// the workers, adopted into the clipmaps that follow the camera, and thrown away when it is no longer
/// wanted or no longer the right resolution.
/// </summary>
/// <param name="device">The device the clipmaps live on.</param>
/// <param name="logger">Where bakes are reported.</param>
/// <param name="work">The workers the field bakes on.</param>
/// <param name="host">The level up now and the cache its fields are kept in.</param>
/// <param name="adopted">Called once a field is in use, so the surface cache can converge against it.</param>
/// <param name="discarding">Called before the field is thrown away, so whatever was converging against it stops.</param>
internal sealed class DistanceFieldResidency(
    IGraphicsDevice device,
    IOpenIgiLogger logger,
    IWorkScheduler work,
    IGlobalIlluminationHost host,
    Action adopted,
    Action discarding) : IDisposable
{
    private readonly IGraphicsDevice _device = device;
    private readonly IOpenIgiLogger _logger = logger;
    private readonly IWorkScheduler _work = work;
    private readonly IGlobalIlluminationHost _host = host;
    private readonly Action _adopted = adopted;
    private readonly Action _discarding = discarding;

    /// <summary>Gets the distance-field bake in flight, if any.</summary>
    public DistanceFieldBake? FieldBake { get; private set; }

    /// <summary>Gets the level's exact distance field, which the clipmaps are checked against.</summary>
    public SceneDistanceField? DistanceFieldScene { get; private set; }

    /// <summary>Gets the clipmaps the level's distance field is resampled into around the camera, once baked.</summary>
    public GlobalDistanceField? DistanceField { get; private set; }

    /// <summary>Gets or sets whether the clipmaps have been checked against the exact field this level.</summary>
    public bool FieldValidated { get; set; }

    // The resolution the field currently in memory was actually built at, so a change can be noticed and
    // rebuilt from rather than quietly ignored until the next level load.
    private int _builtFieldResolution;

    // What has already been ATTEMPTED, as opposed to what succeeded. A field bake can come back with
    // nothing to upload, and this is asked for once a frame. Without this, one failure becomes a full
    // re-traversal of every placement, every frame, for as long as the level is up. A retry needs a
    // reason, and the reason is that the resolution changed.
    private int _attemptedFieldResolution;

    /// <summary>Gets whether the field is wanted and still on its way: baking, or not yet attempted at this resolution.</summary>
    public bool IsPending =>
        RenderCVars.DistanceFields.Value && DistanceField is null
        && (FieldBake is not null || _attemptedFieldResolution != RenderCVars.DistanceFieldResolution.Value);

    public void EnsureDistanceFields()
    {
        bool wanted = RenderCVars.DistanceFields.Value && _device.Capabilities.SupportsCompute;
        int resolution = RenderCVars.DistanceFieldResolution.Value;

        if (!wanted)
        {
            DiscardDistanceFields();
            return;
        }

        // A resolution change invalidates the built field rather than being ignored until the next load:
        // the voxel grid IS the resolution, so there is nothing to adjust in place.
        if (DistanceField is not null && resolution != _builtFieldResolution)
        {
            DiscardDistanceFields();
        }

        if (DistanceField is not null || FieldBake is not null)
        {
            return;
        }

        // Nothing built, nothing baking, and this resolution has already been tried: the bake produced
        // nothing and starting it again would produce nothing again.
        if (resolution == _attemptedFieldResolution)
        {
            return;
        }

        _attemptedFieldResolution = resolution;
        _builtFieldResolution = resolution;

        // The cache first, and this is the whole point of it: a hit means the lighting model engages on
        // the NEXT frame rather than several seconds later, so the level opens already lit instead of
        // opening on the artists' lightmaps and crossing over while the player watches.
        if (_host.CurrentMissionId is { } missionId
            && _host.FieldCache.TryLoad(missionId, resolution) is { } cached)
        {
            AdoptDistanceField(cached, DistanceFieldAtlas.Build(cached.Fields), fromCache: true);
            return;
        }

        FieldBake = DistanceFieldBake.Start(_host.Scene!.Placements, _work, resolution, _logger);
        _logger.Information(
            "GlobalIllumination",
            $"Baking the level's distance fields at {resolution} voxels; the level will light from the "
            + "artists' lightmaps until it lands, then cross over. The result is cached, so this happens "
            + "once per mission.");
    }

    /// <summary>The clipmaps' shape for the trace this device runs.</summary>
    /// <remarks>
    /// Twice the resolution where nothing is traced: a quarter-metre voxel near the eye and half a metre a
    /// building away do not hold the slab over a doorway or a canopy's roof, and the software gathers'
    /// rays slipped through it on some frames and not others -- which a night of small bright lamp pools
    /// showed as clouds crawling over every wall, fifteen per cent of the bounce changing frame to frame
    /// where the traced gather changed half of one. Eight megabytes, and about a millisecond of rebuilds
    /// while the camera travels. Where rays are traced the field answers only far and coarse questions,
    /// and keeps the cheaper shape.
    /// </remarks>
    private GlobalDistanceFieldSettings DistanceFieldSettings() =>
        _device.Capabilities.SupportsRayQuery && RenderCVars.HardwareRayTracing.Value
            ? GlobalDistanceFieldSettings.Default
            : GlobalDistanceFieldSettings.Default with { Resolution = 128 };

    /// <summary>Takes a baked field into use, and writes it to the cache when it was freshly baked.</summary>
    /// <param name="scene">The level's field.</param>
    /// <param name="atlas">Every distinct model's field, packed for the GPU.</param>
    /// <param name="fromCache">
    /// Whether this came off disk, in which case there is nothing to write back.
    /// </param>
    /// <remarks>
    /// One place, because the field arrives by four routes -- off the worker on an ordinary load, off the
    /// worker mid-level when the lighting model is switched on, straight off disk, and with a backdrop that
    /// read or baked it behind the menu -- and all of them have to produce the same clipmaps and leave the
    /// same state behind. Uploading the atlas is why this cannot live on the worker: a GPU texture belongs
    /// to the context that made it.
    /// </remarks>
    public void AdoptDistanceField(SceneDistanceField scene, DistanceFieldAtlas atlas, bool fromCache)
    {
        DistanceFieldScene = scene;
        FieldValidated = false;
        DistanceField?.Dispose();
        DistanceField = GlobalDistanceField.Create(_device, scene, atlas, DistanceFieldSettings(), _logger);

        _adopted();

        if (fromCache || _host.CurrentMissionId is not { } missionId)
        {
            return;
        }

        // Written under the resolution it was actually baked at rather than whatever the variable says
        // now: the player can move r_sdf_resolution while a bake is in flight, and a file keyed by the
        // new value would be a field that does not match its own key.
        _host.FieldCache.Save(missionId, _builtFieldResolution, scene);
    }

    public void DiscardDistanceFields()
    {
        _discarding();
        // Clearing the latch as well as the structures, so the next level -- or the next resolution --
        // gets its own attempt rather than inheriting this one's verdict.
        _attemptedFieldResolution = 0;
        FieldBake?.Cancel();
        FieldBake = null;
        DistanceField?.Dispose();
        DistanceField = null;
        DistanceFieldScene = null;
        FieldValidated = false;
    }

    /// <summary>Takes a finished distance-field bake out of flight.</summary>
    /// <param name="baked">Receives what it produced.</param>
    /// <returns><see langword="true"/> when one finished.</returns>
    public bool TryCollectFieldBake(out DistanceFieldBake.Result baked)
    {
        if (FieldBake is { } pending && pending.TryCollect(out baked))
        {
            FieldBake = null;
            return true;
        }

        baked = default;
        return false;
    }

    /// <summary>Waits for the bake in flight and adopts it, as a capture must rather than race the worker.</summary>
    public void WaitForFieldBake()
    {
        if (FieldBake is { } pending && pending.WaitForResult() is { } ready)
        {
            FieldBake = null;
            AdoptDistanceField(ready.Scene, ready.Atlas, fromCache: false);
        }
    }

    /// <summary>Takes a backdrop's distance field, read or still baking, as the level's own.</summary>
    /// <param name="resolution">The resolution it was read or is being baked at.</param>
    /// <param name="read">The field read from the cache, if it was there.</param>
    /// <param name="baking">The bake in flight, if it was not.</param>
    public void AdoptBackdropField(int resolution, SceneDistanceField? read, DistanceFieldBake? baking)
    {
        if (!RenderCVars.DistanceFields.Value || !_device.Capabilities.SupportsCompute)
        {
            // Started before the lite profile was in force -- the very first backdrop is built before
            // there is one -- and wanted by nobody now. Play bakes its own if it needs one.
            baking?.Cancel();
            return;
        }

        _attemptedFieldResolution = resolution;
        _builtFieldResolution = resolution;

        if (read is not null)
        {
            AdoptDistanceField(read, DistanceFieldAtlas.Build(read.Fields), fromCache: true);
        }
        else if (baking is not null)
        {
            // Handed to the same per-frame collect a played mission's bake uses, which adopts it and
            // writes the cache when it lands -- under a held frame, so the menu does not show the
            // cross-over either.
            FieldBake = baking;
        }
    }

    /// <summary>Forgets the field a session is about to replace, without freeing it.</summary>
    public void Forget()
    {
        FieldBake = null;
        DistanceFieldScene = null;
        DistanceField = null;
        FieldValidated = false;
    }

    /// <inheritdoc/>
    public void Dispose() => DistanceField?.Dispose();
}
