using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>What the player's states ask of the world, answered from the collision world and the level's sounds.</summary>
internal sealed class Igi2LevelSurroundings(Igi2CollisionWorld world, Igi2Sounds sounds) : IIgi2HumanSurroundings
{
    /// <summary>Gets or sets the body these surroundings are asked about.</summary>
    public Igi2Human? Owner { get; set; }

    public IIgi2CollisionQuery Collision { get; } = world;

    /// <summary>Gets or sets the list detection looks over, which a dying soldier joins.</summary>
    public Detection.Igi2DetectionWorld? Detection { get; set; }

    public void RegisterDetectable(Igi2Human body) => Detection?.Register(body);

    /// <summary>Gets the collision world itself, whose placed solids the level's moving objects carry.</summary>
    public Igi2CollisionWorld World { get; } = world;

    /// <summary>Gets or sets the level's pickups, which a thrown weapon joins.</summary>
    public Pickups.Igi2PickupList? Pickups { get; set; }

    /// <summary>Gets or sets the level's grenades, which a soldier's throw joins.</summary>
    public Weapons.Igi2Grenades? Grenades { get; set; }

    public bool ThrowGrenade(in Weapons.Igi2GrenadeThrow grenade)
    {
        if (Grenades is not { } grenades)
        {
            return false;
        }

        grenades.Launch(grenade);
        return grenades.KindOf(grenade.WeaponType) == Weapons.Igi2GrenadeKind.Normal;
    }

    /// <summary>Gets or sets what resolves a thrown weapon's fixed model, whose radius the pickup takes.</summary>
    public Func<string, Formats.World.MefModel?>? Models { get; set; }

    public void ThrowGunPickup(
        Igi2WeaponTypeTask weaponType,
        Vector3d position,
        IReadOnlyList<float> orientation,
        float throwGamma,
        int ammoInClip,
        IReadOnlyList<int> ammoCount,
        bool isThrowShort = true)
    {
        Weapons.Igi2WeaponTables? tables = Owner switch
        {
            Soldiers.Igi2HumanSoldier soldier => soldier.Weapon.Inventory?.Tables,
            Igi2HumanPlayer player => player.Weapon.Inventory?.Tables,
            _ => null,
        };
        if (Pickups is not { } pickups || tables is null)
        {
            return;
        }

        float radius = weaponType.FixedModel.Length > 0 && Models?.Invoke(weaponType.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
        pickups.Add(OpenIGI.Game.Igi2.Pickups.Igi2GunPickup.Thrown(weaponType, tables, position, orientation, throwGamma, isMoving: true, isThrowShort, ammoInClip, ammoCount, radius));
    }

    public bool IsOnElevator => false;

    public bool FitsLyingFlat() => Owner?.FitsWhereItStands() ?? true;

    public float? SoundFalloffEnd(string name) =>
        sounds.Definitions.TryResolve(name, out Engine.Audio.SoundDefinition? definition) && definition!.Kind == Engine.Audio.SoundDefinitionKind.Sound
            ? definition.MaximumDistanceMetres
            : null;

    public void SendLandingEvent()
    {
    }

    /// <summary>Gets or sets the level's water, which the owner's depth is asked of.</summary>
    public World.Igi2Water? Water { get; set; }

    // The body the owner was last found in, Human_s.iWaterIndex.
    private int _waterIndex = -1;

    public bool IsUnderWater(out float depth)
    {
        depth = 0f;
        _waterIndex = -1;
        return Water is { } water && Owner is { } owner && water.IsUnderWater(owner.Position, out depth, out _waterIndex);
    }

    public void StirWater(bool isRunning)
    {
        if (Water is { } water && Owner is { } owner)
        {
            water.PlayerMovementEffect(owner.Position, _waterIndex, isRunning);
        }
    }

    /// <summary>Gets the level's conveyor belts, by level id.</summary>
    public Dictionary<int, Interaction.Igi2ConveyorBeltTask> Conveyors { get; } = [];

    /// <summary>Gets or sets the session's materials, which name a footing's material, or <see langword="null"/> for none.</summary>
    public Igi2GameMaterials? Materials { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human_Move</c> (<c>igi2 0x501685</c>) asks whatever stands underfoot, and whatever does not answer pushes nothing: only a
    /// belt's own solid does (<see cref="Interaction.Igi2ConveyorBeltTask.Push"/>).
    /// </remarks>
    public Vector3f? PushOnConveyor(IWorldCollider ground, WorldIntersection footing)
    {
        ArgumentNullException.ThrowIfNull(footing);
        if (ground is Igi2RigidObject { Owner: Igi2CollisionOwner { Depth: 0 } owner }
            && owner.TypeName == Interaction.Igi2ConveyorBeltTask.TypeName
            && Conveyors.TryGetValue(owner.LevelId, out Interaction.Igi2ConveyorBeltTask? belt))
        {
            return belt.Push(footing.EnterMaterial is int row ? Materials?[row]?.Name : null);
        }

        return default(Vector3f);
    }

    /// <summary>Gets or sets the level's 2-D particles, which blood is thrown through.</summary>
    public Igi2Particles? Particles { get; set; }

    /// <summary>Gets or sets every human registered in the level, <c>Human_ptQTaskList</c>.</summary>
    public IReadOnlyList<Igi2Human> Humans { get; set; } = [];

    /// <summary>Gets or sets where the AI's events go, <c>HumanAI_SendEvent</c>, once the level's AI is made.</summary>
    public Action<Ai.Igi2AiEvent>? AiEvents { get; set; }

    /// <inheritdoc/>
    public void SendAiEvent(in Ai.Igi2AiEvent aiEvent) => AiEvents?.Invoke(aiEvent);

    public string? LandingSound(int material) =>
        Materials?[material] is { LandSound.Length: > 0 } game ? game.LandSound : null;

    public Igi2GameMaterial? Material(int row) => Materials?[row];

    public IIgi2SoundInstance? PlaySoundAt(string name, Vector3d position) =>
        Owner is Igi2HumanPlayer ? StartSound(name) : sounds.New(name, position);

    public void PlaySound(string name) => StartSound(name);

    public void ShowStatusMessage(string key) => StatusMessage?.Invoke(key);

    /// <summary>Gets or sets where a line the game says to the player goes: the player's status message screen.</summary>
    public Action<string>? StatusMessage { get; set; }

    public IIgi2SoundInstance StartSound(string name) =>
        sounds.New(name, View(), View) ?? (IIgi2SoundInstance)new SilentSound();

    private Vector3d View() => Owner?.View.Position ?? default;

    /// <summary>Gets the things the player may activate, in the order the stationary tree offers them.</summary>
    public List<Interaction.IIgi2Activatable> Activatables { get; } = [];

    /// <summary>Gets or sets the level's things by task id, which a soldier's activation names.</summary>
    public IReadOnlyDictionary<int, Interaction.IIgi2Activatable>? ById { get; set; }

    public Interaction.IIgi2Activatable? ActivatableById(int id) =>
        ById is { } byId && byId.TryGetValue(id, out Interaction.IIgi2Activatable? thing) ? thing : null;

    /// <inheritdoc/>
    /// <remarks>Each thing's sphere against the player's: the two meet when their centres are no further apart than their radii added.</remarks>
    public IEnumerable<Interaction.IIgi2Activatable> FindActivatables(Vector3d centre, float radius)
    {
        foreach (Interaction.IIgi2Activatable candidate in Activatables)
        {
            Vector3d d = candidate.Centre - centre;
            double reach = radius + candidate.Radius;
            if ((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z) <= reach * reach)
            {
                yield return candidate;
            }
        }
    }

    private sealed class SilentSound : IIgi2SoundInstance
    {
        public bool IsAlive => false;

        public void MoveToHuman()
        {
        }

        public void Delete()
        {
        }
    }
}
