using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>A class given to a type whose parameters stay the ones level data declares.</summary>
public sealed class RegisterDeclaredTests
{
    private static readonly TaskParameter[] Declared =
    [
        new("Speed", ScriptParamType.Real32),
        new("Name", ScriptParamType.String32),
    ];

    [Fact]
    public void AClassRegisteredBeforeTheLevelDeclaresItsTypeTakesTheDeclaredParameters()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.RegisterDeclared<Mover>("Mover");

        types.Declare("Mover", Declared);

        Assert.Same(type, types.Get("Mover"));
        Assert.False(type.IsDeclarationOnly);
        Assert.True(type.IsDeclared);
        Assert.Equal(Declared, type.Parameters);
    }

    [Fact]
    public void AClassRegisteredAfterTheLevelDeclaredItsTypeKeepsTheDeclaredParameters()
    {
        TaskTypeRegistry types = new();
        types.Declare("Mover", Declared);

        TaskType type = types.RegisterDeclared<Mover>("Mover");

        Assert.False(type.IsDeclarationOnly);
        Assert.True(type.IsDeclared);
        Assert.Equal(Declared, type.Parameters);
    }

    [Fact]
    public void ATaskOfTheTypeIsMadeAsTheClassAndIsStillADeclaredTask()
    {
        TaskTypeRegistry types = new();
        types.RegisterDeclared<Mover>("Mover");
        types.Declare("Mover", Declared);
        TaskTree tree = new(types);

        GameTask task = tree.New(tree.Root, types.Get("Mover"));

        Assert.IsType<Mover>(task);
        Assert.IsAssignableFrom<DeclaredTask>(task);
    }

    [Fact]
    public void ASecondLevelMayDeclareTheTypeWithAnotherList()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.RegisterDeclared<Mover>("Mover");
        types.Declare("Mover", Declared);
        TaskParameter[] other = [new("Speed", ScriptParamType.Real32)];

        types.Declare("Mover", other);

        Assert.Equal(other, type.Parameters);
    }

    private sealed class Mover : DeclaredTask;
}
