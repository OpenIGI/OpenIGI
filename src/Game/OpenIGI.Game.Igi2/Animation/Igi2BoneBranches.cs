namespace OpenIGI.Game.Igi2.Animation;

/// <summary>The named branches of a human rig, and their marking, as <c>HumanAnim_Init</c> (<c>igi2 0x58F650</c>) finds them.</summary>
public static class Igi2BoneBranches
{
    /// <summary>The bone the whole body hangs from.</summary>
    public const string CenterBone = "center";

    /// <summary>The bone whose branch the upper body animates: the spine, arms and head.</summary>
    public const string LowerBodyBone = "lower body";

    /// <summary>The bone above it, <c>upper body</c>.</summary>
    public const string UpperBodyBone = "upper body";

    /// <summary>The shoulders' bone.</summary>
    public const string ShouldersBone = "shoulders";

    /// <summary>The head's bone.</summary>
    public const string HeadBone = "head";

    /// <summary>Marks a branch of a hierarchy, or everything but it, as <c>Hierarchy_MarkSubHierarchy</c> (<c>igi2 0x57F3A0</c>) does.</summary>
    /// <param name="rig">The hierarchy.</param>
    /// <param name="branch">The branch's bone, or <c>-1</c> for none.</param>
    /// <param name="include">Whether the branch is marked; otherwise everything else is.</param>
    /// <returns>One mark per bone.</returns>
    public static bool[] Mark(Formats.Animation.Igi2BoneHierarchy rig, int branch, bool include)
    {
        ArgumentNullException.ThrowIfNull(rig);
        bool[] marks = new bool[rig.BoneCount];
        for (int bone = 0; bone < marks.Length; bone++)
        {
            int parent = rig.Parents[bone];
            marks[bone] = bone == branch ? include : parent < 0 ? !include : marks[parent];
        }

        return marks;
    }

    /// <summary>A bone's index by its name, as <c>Hierarchy_GetBone</c> (<c>igi2 0x57F2F0</c>) finds it.</summary>
    /// <param name="names">The bones' names, in bone order.</param>
    /// <param name="name">The name.</param>
    /// <returns>The index, or <c>-1</c>.</returns>
    public static int IndexOf(IReadOnlyList<string> names, string name)
    {
        ArgumentNullException.ThrowIfNull(names);
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
