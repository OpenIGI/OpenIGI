using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>An explosion as <c>Explosion0_NewEx</c> (<c>igi2 0x50A650</c>) is given it.</summary>
/// <param name="Position">Where it goes off.</param>
/// <param name="Count">How many times it goes off, <c>nExplosion</c>.</param>
/// <param name="Period">The ticks before it first goes off, and the bound on each wait after, <c>nExplosionTime</c>.</param>
/// <param name="Damage">The damage at its heart, <c>vDamage</c>.</param>
/// <param name="FullRange">How far the full damage reaches, <c>vFullRange</c>.</param>
/// <param name="FalloffRange">How far past that it falls away, <c>vFallOffRange</c>.</param>
/// <param name="BurnRange">The size of the mark it burns, <c>vBurnRange</c>.</param>
/// <param name="FragmentCount">How many fragments it throws, <c>nFragment</c>.</param>
/// <param name="FireballCount">How many fireballs it shows, <c>nFireball</c>.</param>
/// <param name="Ground">What it struck, which is burnt; <see langword="null"/> for nothing.</param>
/// <param name="FragmentModel">The fragments' model.</param>
/// <param name="Sound">Its sound.</param>
/// <param name="AiEvent">The AI event it raises, <c>eHumanAIEvent</c>; -1 for none.</param>
/// <param name="AiEventFlags">The event's flags.</param>
/// <param name="AiEventRange">How far the event carries.</param>
/// <param name="Direction">The direction the fragments are thrown about.</param>
/// <param name="MaxAlpha">The cone the fragments are thrown in, in radians.</param>
/// <param name="MinSpeed">The slowest a fragment leaves.</param>
/// <param name="MaxSpeed">The fastest.</param>
/// <param name="WeaponType">The weapon it came from, or <see langword="null"/>.</param>
/// <param name="Owner">Who set it off, or <see langword="null"/>.</param>
/// <param name="Falloff">How its damage falls away.</param>
public sealed record Igi2ExplosionRequest(
    Vector3d Position,
    int Count,
    int Period,
    float Damage,
    float FullRange,
    float FalloffRange,
    float BurnRange,
    int FragmentCount,
    int FireballCount,
    Igi2Intersection? Ground,
    string FragmentModel,
    string Sound,
    int AiEvent,
    int AiEventFlags,
    float AiEventRange,
    Vector3f Direction,
    float MaxAlpha,
    float MinSpeed,
    float MaxSpeed,
    Igi2WeaponTypeTask? WeaponType,
    IIgi2WeaponOwner? Owner,
    Igi2ExplosionFalloff Falloff);
