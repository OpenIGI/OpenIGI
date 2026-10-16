using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>The record kinds an I.G.I. 2 clip's event stream carries.</summary>
/// <remarks>
/// <para>
/// The values are the <c>nType</c> byte of <c>AnimEvent_s</c>, and the set is closed:
/// <c>AnimController_Step</c> (<c>igi2 0x5549A0</c>) and <c>AnimController_Seek</c> (<c>0x553F60</c>)
/// abort with "Unknown animation event encountered" on anything else. Across the 1,244 clips the game
/// ships, these are the only six that occur, and each is always the length its record type has.
/// </para>
/// <para>
/// Types 0, 2 and 5 are accepted by the stepper and skipped, but no shipped clip contains one, so they
/// are not modelled.
/// </para>
/// </remarks>
public enum Igi2AnimationEventType
{
    /// <summary><c>AnimEvent_Link_t</c>, 17 dwords: re-attach an attach object to a bone.</summary>
    Link = 1,

    /// <summary><c>AnimEvent_TranslationKey_t</c>, 6 dwords: a root translation key.</summary>
    Translation = 3,

    /// <summary><c>AnimEvent_RotationKey_t</c>, 18 dwords: one bone's rotation key.</summary>
    Rotation = 4,

    /// <summary><c>AnimEvent_Trigger_t</c>, 8 dwords: fire an anim trigger at a bone.</summary>
    Trigger = 6,

    /// <summary><c>AnimEvent_AttachKey_t</c>, 11 dwords: one attach object's key.</summary>
    AttachKey = 7,

    /// <summary>The end of the stream, 3 dwords.</summary>
    End = 255,
}

/// <summary>One root translation key of an I.G.I. 2 clip.</summary>
/// <param name="Time">The key's time, in the clip's integer units.</param>
/// <param name="Translation">The root's position.</param>
/// <remarks>
/// Interpolated <b>linearly</b> to the next key — <c>AnimController_InterpolateHandler</c>
/// (<c>igi2 0x5540F0</c>). There are no translation tangents, unlike I.G.I. 1's Hermite track.
/// </remarks>
public readonly record struct Igi2TranslationKey(int Time, Vector3f Translation);

/// <summary>One bone rotation key of an I.G.I. 2 clip.</summary>
/// <param name="Time">The key's time, in the clip's integer units.</param>
/// <param name="Rotation">The bone's orientation at the key.</param>
/// <param name="IncomingTangent">The SQUAD tangent of the segment ending at this key.</param>
/// <param name="OutgoingTangent">The SQUAD tangent of the segment starting at this key.</param>
/// <param name="Ops">
/// The orientation's operation count as the file stores it, its <c>Quat32_t.nOps</c>, which a turn built on the key counts
/// on from.
/// </param>
/// <remarks>
/// <c>AnimEvent_RotationKey_t</c>: orientation at <c>+0x0C</c> with its <c>nOps</c> at <c>+0x1C</c>, incoming tangent at
/// <c>+0x20</c>, outgoing at <c>+0x34</c>.
/// </remarks>
public readonly record struct Igi2RotationKey(
    int Time,
    Quaternion32 Rotation,
    Quaternion32 IncomingTangent,
    Quaternion32 OutgoingTangent,
    int Ops = 0);

/// <summary>One key of an attach object's own track.</summary>
/// <param name="Time">The key's time, in the clip's integer units.</param>
/// <param name="Translation">The object's position relative to its bone.</param>
/// <param name="Rotation">The object's orientation relative to its bone.</param>
public readonly record struct Igi2AttachKey(int Time, Vector3f Translation, Quaternion32 Rotation);

/// <summary>An anim trigger an I.G.I. 2 clip fires as it plays through a time.</summary>
/// <param name="Time">When it fires, in the clip's integer units.</param>
/// <param name="Bone">The bone it is placed on, or <see cref="NoBone"/> for the object's own position.</param>
/// <param name="TriggerId">The anim trigger id, resolved through <c>LOCAL:animtrigger</c>.</param>
/// <param name="Position">Where it fires, in the bone's space.</param>
/// <remarks>
/// <c>AnimEvent_Trigger_t</c>. <c>AnimController_TriggerEvents</c> (<c>igi2 0x553D50</c>) rotates the
/// position by the bone's orientation and adds the bone's world position, or uses the object's position
/// when the bone is <c>-1</c>, and fires only for a bone the controller animates.
/// </remarks>
public readonly record struct Igi2AnimationTrigger(int Time, int Bone, int TriggerId, Vector3f Position)
{
    /// <summary>The bone value meaning "the object's own position".</summary>
    public const int NoBone = -1;
}

/// <summary>A point in an I.G.I. 2 clip where an attach object moves to another bone.</summary>
/// <param name="Time">When it happens, in the clip's integer units.</param>
/// <param name="AttachObject">The index of the attach object it moves.</param>
/// <param name="Bone">The bone it moves to.</param>
/// <param name="Orientation">Its new orientation relative to the bone.</param>
/// <param name="Translation">Its new position relative to the bone.</param>
/// <remarks>
/// <c>AnimEvent_Link_t</c>. Applied by both the stepper and the seeker: the attach object's bone,
/// relative position and relative orientation are overwritten and its parent is told.
/// </remarks>
public readonly record struct Igi2AnimationLink(
    int Time,
    int AttachObject,
    int Bone,
    Matrix3f Orientation,
    Vector3f Translation);

/// <summary>An object a clip carries with it — a parachute, a pistol in a cutscene hand.</summary>
/// <param name="Name">The model name, up to 15 characters.</param>
/// <param name="Position">Its initial position relative to its bone.</param>
/// <param name="Orientation">Its initial orientation relative to its bone.</param>
/// <param name="Bone">The bone it starts on.</param>
/// <param name="Id">The id the clip's owner knows it by.</param>
/// <param name="Keys">Its own key track, empty when it only follows its bone.</param>
/// <remarks><c>Anim_AttachObject_t</c>: a <c>Mesh3D_AttachObject_t</c> plus the id and the first key.</remarks>
public sealed record Igi2AnimationAttachObject(
    string Name,
    Vector3f Position,
    Matrix3f Orientation,
    int Bone,
    int Id,
    IReadOnlyList<Igi2AttachKey> Keys);
