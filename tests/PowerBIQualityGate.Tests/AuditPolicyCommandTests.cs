using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class AuditPolicyCommandTests
{
    [Fact]
    public void RootCommandParsesFailOnWarningFlag()
    {
        var parsed = CliApplication.CreateRootCommand().Parse(
            ["audit", "examples/warning-pbir-tmdl", "--fail-on-warning", "--no-color"]);

        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void WarningFixtureDefaultPolicyExitsZero()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(ExamplePath("warning-pbir-tmdl"), output, errors);

        Assert.Equal(0, exitCode);
        Assert.Contains("SM-001", output.Output);
        Assert.Contains("1 warning", output.Output);
        Assert.Contains("AUDIT: PASS WITH ADVISORIES", output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void WarningFixtureStrictPolicyExitsOneAndUsesStderr()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(ExamplePath("warning-pbir-tmdl"),
            output, errors, failOnWarning: true);

        Assert.Equal(1, exitCode);
        Assert.Contains("SM-001", errors.Output);
        Assert.Contains("AUDIT: FAIL (WARNING POLICY)", errors.Output);
        Assert.Contains("Active policy", errors.Output);
        Assert.Contains("exit code: 1", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void InformationOnlyWithStrictPolicyExitsZero()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"),
            output, errors, failOnWarning: true);

        Assert.Equal(0, exitCode);
        Assert.Contains("SM-003", output.Output);
        Assert.Contains("AUDIT: PASS WITH ADVISORIES", output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void InvalidStructureKeepsExitCodeOneWithStrictPolicy()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(ExamplePath("invalid-pbip-shortcut"),
            output, errors, failOnWarning: true);

        Assert.Equal(1, exitCode);
        Assert.Contains("AUDIT: NOT RUN", errors.Output);
        Assert.Contains("STRUCTURE VALIDATION FAILED", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void MissingProjectKeepsExitCodeTwoWithStrictPolicy()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 120;
        var missing = Path.Combine(Path.GetTempPath(), "pbiq-absent-" + Guid.NewGuid().ToString("N"));

        var exitCode = AuditCommand.Execute(missing, output, errors, failOnWarning: true);

        Assert.Equal(2, exitCode);
        Assert.Contains("PROJECT DISCOVERY FAILED", errors.Output);
        Assert.Contains("AUDIT: NOT RUN", errors.Output);
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
