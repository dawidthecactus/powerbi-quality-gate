using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class CoverageAwareGateCommandTests
{
    [Fact]
    public void ParserAcceptsMinimumCoverageAndFailOnWarningTogether()
    {
        var parsed = CliApplication.CreateRootCommand().Parse(
            ["audit", "example.pbip", "--min-measure-coverage", "95", "--fail-on-warning"]);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void RealTmdlWithUnknownMeasuresIsNonBlockingByDefault()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;
        var code = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"), output, errors);

        Assert.Equal(0, code);
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", output.Output);
        Assert.Contains("Coverage gap:", output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void RealTmdlWithUnknownMeasuresFailsExplicitThresholdToStderr()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 120;
        var code = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"),
            output, errors, minimumMeasureCoverage: 95);

        Assert.Equal(1, code);
        Assert.Contains("AUDIT: FAIL (COVERAGE POLICY)", errors.Output);
        Assert.Contains("minimum 95%", errors.Output);
        Assert.Contains("exit code: 1", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void AZeroPercentThresholdAllowsUnknownTypesWithoutClaimingFullCoverage()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;
        var code = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"),
            output, errors, minimumMeasureCoverage: 0);

        Assert.Equal(0, code);
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", output.Output);
        Assert.Contains("minimum 0%", output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void CoveragePolicyDoesNotHideInvalidStructure()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        var code = AuditCommand.Execute(ExamplePath("invalid-pbip-shortcut"),
            output, errors, minimumMeasureCoverage: 100);

        Assert.Equal(1, code);
        Assert.Contains("STRUCTURE VALIDATION FAILED", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidThresholdDoesNotAttemptAudit(int percent)
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        var code = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"),
            output, errors, minimumMeasureCoverage: percent);

        Assert.Equal(2, code);
        Assert.Contains("INVALID COVERAGE THRESHOLD", errors.Output);
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
