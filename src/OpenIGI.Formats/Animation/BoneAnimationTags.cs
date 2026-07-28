using OpenIGI.Core;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// The chunk ids of a bone animation set, as the loader (<c>0x4D67A0</c>) compares them.
/// </summary>
public static class BoneAnimationTags
{
    /// <summary>The file's top-level form type — the skeleton object.</summary>
    public static readonly FourCc BoneObject = FourCc.FromString("BOBJ");

    /// <summary>The bone hierarchy group.</summary>
    public static readonly FourCc BoneHierarchy = FourCc.FromString("BOBH");

    /// <summary>The skeleton header: <c>{ u32 skeletonId, u32 boneCount }</c>.</summary>
    public static readonly FourCc SkeletonHeader = FourCc.FromString("BOSH");

    /// <summary>The parent index per bone, <c>-1</c> for the root.</summary>
    public static readonly FourCc ParentList = FourCc.FromString("PLST");

    /// <summary>The rest translation per bone, three floats each.</summary>
    public static readonly FourCc TranslationList = FourCc.FromString("TLST");

    /// <summary>The animation list group.</summary>
    public static readonly FourCc AnimationList = FourCc.FromString("BOAL");

    /// <summary>The animation-list header: <c>{ u32 animationCount, u32 idLimit }</c>.</summary>
    public static readonly FourCc AnimationListHeader = FourCc.FromString("BALH");

    /// <summary>One animation's group.</summary>
    public static readonly FourCc Animation = FourCc.FromString("BOAN");

    /// <summary>An animation's header: <c>{ f32 duration, u32 flags, u32 animationId }</c>.</summary>
    public static readonly FourCc AnimationHeader = FourCc.FromString("BOAH");

    /// <summary>The translation track's key count.</summary>
    public static readonly FourCc TranslationHeader = FourCc.FromString("BOTH");

    /// <summary>The translation track's keys.</summary>
    public static readonly FourCc TranslationData = FourCc.FromString("BOTD");

    /// <summary>A bone's rotation track key count.</summary>
    public static readonly FourCc RotationHeader = FourCc.FromString("BORH");

    /// <summary>A bone's rotation track keys.</summary>
    public static readonly FourCc RotationData = FourCc.FromString("BORD");

    /// <summary>The event count.</summary>
    public static readonly FourCc EventHeader = FourCc.FromString("BOEH");

    /// <summary>The event records.</summary>
    public static readonly FourCc EventData = FourCc.FromString("BOED");

    /// <summary>The <c>.mtp</c> pack chunk naming the animation sets a level loads.</summary>
    public static readonly FourCc PackAnimationNames = FourCc.FromString("BANM");
}
