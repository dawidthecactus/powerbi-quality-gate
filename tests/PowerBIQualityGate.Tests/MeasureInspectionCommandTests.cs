using PowerBIQualityGate.Cli;

namespace PowerBIQualityGate.Tests;

public sealed class MeasureInspectionCommandTests
{
    [Fact]
    public void MeasureInventoryRunsAgainstRealSyntheticPbip()
    {
        var result = CliApplication.CreateRootCommand().Parse(
            ["inspect", ExampleProject(), "--measures", "--no-color"]);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.Invoke());
    }

    [Fact]
    public void TomAndMeasureInventoryOptionsCannotBeCombined()
    {
        var result = CliApplication.CreateRootCommand().Parse(
            ["inspect", ExampleProject(), "--tom", "--measures"]);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Invoke());
    }

    private static string ExampleProject()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", "valid-pbir-tmdl");
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
