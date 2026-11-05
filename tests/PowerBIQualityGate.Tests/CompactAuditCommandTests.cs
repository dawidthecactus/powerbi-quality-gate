using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;
using PowerBIQualityGate.Cli.Rendering;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class CompactAuditCommandTests
{
    [Theory]
    [InlineData("--details")]
    [InlineData("--quiet")]
    public void NewOutputModesAreRecognizedByCliParser(string option)
    {
        var parsed = CliApplication.CreateRootCommand().Parse(["audit", "sample.pbip", option]);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void RealTmdlSummaryDoesNotListAffectedColumns()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;
        var exitCode = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"), output, errors);
        Assert.Equal(0, exitCode);
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", output.Output);
        Assert.Contains("SM-003", output.Output);
        Assert.Contains("3 findings", output.Output);
        Assert.False(output.Output.Contains("Product[ProductId]", StringComparison.Ordinal));
        Assert.False(output.Output.Contains("WHY IT MATTERS", StringComparison.Ordinal));
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void RealTmdlDetailsGroupGuidanceRatherThanRepeat()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;
        var exitCode = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"), output, errors,
            mode: AuditOutputMode.Details);
        Assert.Equal(0, exitCode);
        Assert.Contains("FINDINGS BY RULE", output.Output);
        Assert.Contains("Product[ProductId]", output.Output);
        Assert.Contains("Sales[OrderId]", output.Output);
        var guidanceGroups = output.Output.Split("WHY IT MATTERS:", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, guidanceGroups);
    }

    [Fact]
    public void RealTmdlQuietReturnsSingleLineOnStdout()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        var exitCode = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"), output, errors,
            mode: AuditOutputMode.Quiet);
        Assert.Equal(0, exitCode);
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", output.Output);
        Assert.Single(output.Output.TrimEnd().Split('\n'));
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void StrictWarningQuietFailsToStderrWithoutModelMetadata()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        var exitCode = AuditCommand.Execute(ExamplePath("warning-pbir-tmdl"), output, errors,
            failOnWarning: true, mode: AuditOutputMode.Quiet);
        Assert.Equal(1, exitCode);
        Assert.Contains("AUDIT: FAIL (WARNING POLICY)", errors.Output);
        Assert.Single(errors.Output.TrimEnd().Split('\n'));
        Assert.False(errors.Output.Contains("Sales[ProductId]", StringComparison.Ordinal));
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void StrictCoverageQuietFailsToStderrWithExitOne()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        var exitCode = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"), output, errors,
            minimumMeasureCoverage: 95, mode: AuditOutputMode.Quiet);
        Assert.Equal(1, exitCode);
        Assert.Contains("AUDIT: FAIL (COVERAGE POLICY)", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void MissingProjectQuietStillUsesExitTwo()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        var path = Path.Combine(Path.GetTempPath(), "pbiq-absent-" + Guid.NewGuid().ToString("N"));
        var exitCode = AuditCommand.Execute(path, output, errors, mode: AuditOutputMode.Quiet);
        Assert.Equal(2, exitCode);
        Assert.Contains("AUDIT: NOT RUN", errors.Output);
        Assert.Contains("exit code: 2", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    private static string ExamplePath(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", name);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
