using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>A mod makes a registered task type out of its own class, the type staying the one the tree and its subtypes know.</summary>
public sealed class TaskTypeReplaceTests
{
    [Fact]
    public void A_replaced_type_keeps_its_id_and_parent_and_is_made_as_the_new_class()
    {
        TaskTypeRegistry types = new();
        TaskType root = types.Register<ProbeTask>("Root");
        TaskType probe = types.Register<ProbeTask>("Probe", root);
        TaskType child = types.Register<ProbeTask>("Child", probe);

        TaskType replaced = types.Replace<WalkerTask>("Probe");

        Assert.Same(probe, replaced);
        Assert.Same(root, replaced.Parent);
        Assert.True(child.IsSubtypeOf(replaced));
        TaskTree tree = new(types);
        GameTask made = tree.New(tree.Root, probe);
        Assert.IsType<WalkerTask>(made);
        Assert.Same(probe, made.Type);
    }

    [Fact]
    public void Only_a_type_a_class_implements_can_be_replaced()
    {
        TaskTypeRegistry types = new();
        types.Declare("OnlyDeclared", []);

        Assert.Throws<KeyNotFoundException>(() => types.Replace<ProbeTask>("Missing"));
        Assert.Throws<KeyNotFoundException>(() => types.Replace<ProbeTask>("OnlyDeclared"));
    }
}
