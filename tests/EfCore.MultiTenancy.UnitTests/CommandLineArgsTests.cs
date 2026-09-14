using EfCore.MultiTenancy.EfCore.Administration;

namespace EfCore.MultiTenancy.UnitTests;

public class CommandLineArgsTests
{
    [Fact]
    public void Parse_RepeatedFlag_CollectsEveryValueInOrder()
    {
        var args = CommandLineArgs.Parse(new[] { "--tenant", "Acme|acme", "--tenant", "Globex|globex" });

        Assert.Equal(new[] { "Acme|acme", "Globex|globex" }, args.GetValues("tenant"));
    }

    [Fact]
    public void Parse_FlagAtEndOfArgs_IsASwitch()
    {
        var args = CommandLineArgs.Parse(new[] { "--schema", "acme", "--force" });

        Assert.True(args.HasSwitch("force"));
        Assert.Equal(new[] { "acme" }, args.GetValues("schema"));
    }

    [Fact]
    public void Parse_FlagImmediatelyFollowedByAnotherFlag_IsASwitch_NotAValue()
    {
        var args = CommandLineArgs.Parse(new[] { "--force", "--keep-schema" });

        Assert.True(args.HasSwitch("force"));
        Assert.True(args.HasSwitch("keep-schema"));
        Assert.Empty(args.GetValues("force"));
    }

    [Fact]
    public void Parse_MissingFlag_ReturnsEmptyValuesAndFalseSwitch()
    {
        var args = CommandLineArgs.Parse(new[] { "--schema", "acme" });

        Assert.Empty(args.GetValues("id"));
        Assert.False(args.HasSwitch("force"));
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForFlagNames()
    {
        var args = CommandLineArgs.Parse(new[] { "--Schema", "acme" });

        Assert.Equal(new[] { "acme" }, args.GetValues("schema"));
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("-schema")]
    [InlineData("--")]
    public void Parse_TokenNotShapedLikeAFlag_Throws(string badToken)
    {
        Assert.Throws<CommandArgumentException>(() => CommandLineArgs.Parse(new[] { badToken }));
    }
}
