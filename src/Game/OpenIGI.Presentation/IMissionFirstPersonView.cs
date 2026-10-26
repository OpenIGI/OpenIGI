using OpenIGI.Core.Graphics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation;

/// <summary>What the mission's first-person view draws from one frame.</summary>
/// <param name="ShowViewModel">Whether the rig is to be drawn at all this frame.</param>
/// <param name="ViewModelTanX">The view model's own horizontal half-angle tangent.</param>
/// <param name="Smooth">Whether the rig is smoothed between ticks.</param>
/// <param name="TickBlend">How far the frame is between the last tick and the next.</param>
/// <param name="FarPlane">The far plane, in metres.</param>
/// <param name="Terrain">The terrain the binoculars' range is also traced against, or <see langword="null"/>.</param>
/// <param name="Arms">The arms, or <see langword="null"/>.</param>
/// <param name="Held">The weapon in hand, or <see langword="null"/>.</param>
/// <param name="LoadedForWeapon">The mission whose packs a newly held weapon's model is read from, or <see langword="null"/>.</param>
/// <param name="Sun">The level's fixed-function light the rig is lit by, or <see langword="null"/>.</param>
public readonly record struct FirstPersonFrame(
    bool ShowViewModel,
    float ViewModelTanX,
    bool Smooth,
    float TickBlend,
    float FarPlane,
    TerrainData? Terrain,
    IViewModelPresenter? Arms,
    IHeldWeaponPresenter? Held,
    LoadedMission? LoadedForWeapon,
    FixedFunctionSun? Sun = null);

/// <summary>What the first-person view drew.</summary>
/// <param name="DrewRig">Whether it drew the title's rig, in which case nothing else draws a first-person view.</param>
/// <param name="BinocularRangeMetres">The binoculars' range readout, or <see langword="null"/> when the glasses are down.</param>
public readonly record struct FirstPersonResult(bool DrewRig, float? BinocularRangeMetres);

/// <summary>A title's first-person view, drawn over the world.</summary>
public interface IMissionFirstPersonView
{
    /// <summary>Draws the rig and what it holds, or the optics that replace them.</summary>
    /// <param name="eye">The eye the frame is drawn from, in metres.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>What it drew.</returns>
    FirstPersonResult Draw(in Camera3D eye, in FirstPersonFrame frame);

    /// <summary>Keeps the drawn weapon on whatever the cycle has put in hand; it only does anything on the tick the weapon changed.</summary>
    /// <param name="held">The weapon's presenter.</param>
    /// <param name="loadedMission">The mission whose packs the model is read from.</param>
    void FollowHeldWeapon(IHeldWeaponPresenter held, LoadedMission loadedMission);
}
