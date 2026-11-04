using PowerBIQualityGate.Cli;

namespace PowerBIQualityGate.Tests;

public class CliApplicationTests
{
    [Fact]
    public void RootCommand_ShouldHaveDescription()
    {
        var root = CliApplication.CreateRootCommand();

        Assert.Contains("Power BI", root.Description);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public void BuiltInOptions_ShouldSucceed(string option)
    {
        var root = CliApplication.CreateRootCommand();
        var result = root.Parse([option]);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.Invoke());
    }

    [Fact]
    public void UnknownOption_ShouldFail()
    {
        var root = CliApplication.CreateRootCommand();
        var result = root.Parse(["--unknown-option"]);

        Assert.NotEmpty(result.Errors);
        Assert.NotEqual(0, result.Invoke());
    }
}