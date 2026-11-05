using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class AuditCommandTests
{
    [Fact]
    public void AuditIsRegisteredWithHelpAndNoColorOption()
    {
        var root = CliApplication.CreateRootCommand();
        var result = root.Parse(["audit", "examples/valid-pbir-tmdl", "--no-color"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidExampleRunsAllFourRulesAndReportsTechnicalKeys()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        output.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(ExamplePath("valid-pbir-tmdl"), output, errors);

        Assert.Equal(0, exitCode);
        Assert.Contains("4 rules evaluated", output.Output);
        Assert.Contains("SM-003", output.Output);
        Assert.Contains("Sales[ProductId]", output.Output);
        Assert.Contains("AUDIT: PASS WITH COVERAGE GAPS", output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void BrokenPbipDoesNotAuditAndReturnsStructuralFailure()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(ExamplePath("invalid-pbip-shortcut"), output, errors);

        Assert.Equal(1, exitCode);
        Assert.Contains("STRUCTURE VALIDATION FAILED", errors.Output);
        Assert.Contains("AUDIT: NOT RUN", errors.Output);
        Assert.Contains("pbiq scan", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void MissingDirectoryReturnsDiscoveryExitCodeTwo()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        errors.Profile.Width = 120;
        var missing = Path.Combine(Path.GetTempPath(), "pbiq-audit-" + Guid.NewGuid().ToString("N"));

        var exitCode = AuditCommand.Execute(missing, output, errors);

        Assert.Equal(2, exitCode);
        Assert.Contains("PROJECT DISCOVERY FAILED", errors.Output);
        Assert.Contains("AUDIT: NOT RUN", errors.Output);
        Assert.Equal(string.Empty, output.Output);
    }

    [Fact]
    public void UnsupportedExternalModelIsNotReportedAsClean()
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        using var fixture = new ExternalModelFixture();
        errors.Profile.Width = 120;

        var exitCode = AuditCommand.Execute(fixture.Path, output, errors);

        Assert.Equal(2, exitCode);
        Assert.Contains("LOCAL TMDL REQUIRED", errors.Output);
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

    private sealed class ExternalModelFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pbiq-audit-external-" + Guid.NewGuid().ToString("N"));

        public ExternalModelFixture()
        {
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "Test.pbip"),
                """
                {"version":"1.0","artifacts":[{"report":{"path":"Test.Report"}}]}
                """);
            var report = System.IO.Path.Combine(Path, "Test.Report");
            Directory.CreateDirectory(report);
            File.WriteAllText(System.IO.Path.Combine(report, "definition.pbir"),
                """
                {"version":"1.0","datasetReference":{"byConnection":{"connectionString":"Data Source=powerbi://somewhere"}}}
                """);
            File.WriteAllText(System.IO.Path.Combine(report, "report.json"), "{}");
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
