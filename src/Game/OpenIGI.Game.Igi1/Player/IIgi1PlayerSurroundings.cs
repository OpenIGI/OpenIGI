using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// What Project I.G.I.'s player is hurt and dies in: the level's clock and streams, its sounds, its corpse and death camera,
/// and the soldiers who hear it land. The game's own mission provides one, and so does another game's level it stands in.
/// </summary>
public interface IIgi1PlayerSurroundings
{
    /// <summary>Gets the level's tick count.</summary>
    int Tick { get; }

    /// <summary>Gets the engine's own stream, which the sound variants, the shake's share and the wash's corners draw from.</summary>
    IgiRandom SoundRandom { get; }

    /// <summary>Gets the stream the death's fall is drawn from.</summary>
    Random ShotRandom { get; }

    /// <summary>Gets where the player's harm is reported.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Plays one of a cry's variants, flat: the player is the listener.</summary>
    /// <param name="prefix">The cry's definition prefix.</param>
    /// <param name="variants">How many variants it has.</param>
    void PlayHit(string prefix, int variants);

    /// <summary>Plays one of a death cry's variants, flat.</summary>
    /// <param name="prefix">The cry's definition prefix.</param>
    /// <param name="variants">How many variants it has.</param>
    void PlayDeath(string prefix, int variants);

    /// <summary>Plays a sound definition, flat.</summary>
    /// <param name="name">The definition.</param>
    /// <param name="priority">Its priority.</param>
    void PlayDefinition(string name, int priority);

    /// <summary>Plays a round's impact on the player's body.</summary>
    /// <param name="position">Where it struck, in world units.</param>
    void PlayBulletImpact(Vector3d position);

    /// <summary>Plays the ground's landing sound.</summary>
    /// <param name="landing">The sound and where, in world units.</param>
    void PlayLanding((string Sound, Vector3d PositionInUnits) landing);

    /// <summary>Stops the player's held voices as it dies: the binoculars' and the ladder's, and its weapons' loops.</summary>
    void SilenceForDeath();

    /// <summary>Throws the player's third-person body into its death.</summary>
    /// <param name="fall">The death animation.</param>
    /// <param name="feet">Where the body stands, in world units.</param>
    /// <param name="facing">The facing whose backward axis points away from the killer.</param>
    /// <param name="bone">The bone the killing round went through, or <c>-1</c>.</param>
    /// <param name="travel">The killing thing's travel, in world units.</param>
    /// <param name="category">The killing weapon's category.</param>
    /// <param name="velocity">The body's velocity.</param>
    void KillBody(int fall, Vector3d feet, float facing, int bone, Vector3d travel, WeaponCategory category, Vector3d velocity);

    /// <summary>Pulls the camera back from the body as it falls.</summary>
    /// <param name="feet">Where the body stands, in world units.</param>
    /// <param name="facing">The facing the body falls along.</param>
    void BeginDeathCamera(Vector3d feet, float facing);

    /// <summary>Tells the soldiers the player landed: every ground contact posts type 11.</summary>
    /// <param name="feet">Where it landed, in world units.</param>
    /// <param name="radiusUnits">How far it is heard.</param>
    void HearLanding(Vector3d feet, double radiusUnits);

    /// <summary>Stands the player's body up again, as a round of a match begins with everyone alive.</summary>
    void ReviveBody();

    /// <summary>Ends the death camera, giving the view back to the player's eye.</summary>
    void EndDeathCamera();
}
