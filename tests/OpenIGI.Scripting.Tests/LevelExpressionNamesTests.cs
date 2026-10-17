using OpenIGI.Scripting.Expressions;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class LevelExpressionNamesTests
{
    [Theory]
    [InlineData("this.isRun", "ConditionalContainer_12", "ConditionalContainer_12.isRun")]
    [InlineData("Door_4.isOpen", "ConditionalContainer_12", "Door_4.isOpen")]
    [InlineData("thisValue", "Task_1", "thisValue")]
    public void This_reads_as_the_owners_name(string name, string owner, string expected)
    {
        Assert.Equal(expected, LevelExpressionNames.ResolveThis(name, owner));
    }

    [Theory]
    [InlineData("Door_4.isExists", true)]
    [InlineData("Door_4.isOpen", false)]
    public void An_exists_probe_is_any_name_holding_the_field(string name, bool expected)
    {
        Assert.Equal(expected, LevelExpressionNames.IsExistsProbe(name));
    }
}
