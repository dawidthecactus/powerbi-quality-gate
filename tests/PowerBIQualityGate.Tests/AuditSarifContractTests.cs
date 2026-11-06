using System.Text.Json;

using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;
using PowerBIQualityGate.Cli.Rendering;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class AuditSarifContractTests
{
    [Fact]
    public void ParserAcceptsSarifFormatAndSourceRoot()
    {
        var result = CliApplication.CreateRootCommand().Parse(
            ["audit", "examples/valid-pbir-tmdl", "--format", "sarif", "--sarif-source-root", "."]);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidFixtureEmitsSarif21WithMappedLocations()
    {
        var (exit, json, stdout, stderr) = Run("valid-pbir-tmdl");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var run = root.GetProperty("runs")[0];
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Equal("pbiq", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.Equal(4, run.GetProperty("tool").GetProperty("driver").GetProperty("rules").GetArrayLength());
        Assert.Equal(3, run.GetProperty("results").GetArrayLength());
        Assert.True(run.GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean());
        Assert.DoesNotContain("SUM(", json);
        Assert.False(json.Contains('\u001b'));

        var result = Assert.Single(run.GetProperty("results").EnumerateArray(), item =>
            item.GetProperty("properties").GetProperty("objectPath").GetString() == "Sales[ProductId]");
        Assert.Equal("note", result.GetProperty("level").GetString());
        var source = result.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("Example.SemanticModel/definition/tables/Sales.tmdl",
            source.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(8, source.GetProperty("region").GetProperty("startLine").GetInt32());
    }

    [Fact]
    public void WarningFixtureProducesWarningWithExitOneAndSourceLine()
    {
        var (exit, json, _, stderr) = Run("warning-pbir-tmdl", failOnWarning: true);
        using var doc = JsonDocument.Parse(json);
        var run = doc.RootElement.GetProperty("runs")[0];
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(1, run.GetProperty("properties").GetProperty("pbiqExitCode").GetInt32());
        var warning = Assert.Single(run.GetProperty("results").EnumerateArray(), item =>
            item.GetProperty("ruleId").GetString() == "SM-001");
        Assert.Equal("warning", warning.GetProperty("level").GetString());
        Assert.Equal("Example.SemanticModel/definition/relationships.tmdl",
            warning.GetProperty("locations")[0].GetProperty("physicalLocation")
                .GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void ExplicitSourceRootRebasesFileToRepositoryCheckout()
    {
        var (exit, json, _, _) = Run("valid-pbir-tmdl", sourceRoot: SolutionRoot());
        using var doc = JsonDocument.Parse(json);
        var result = Assert.Single(doc.RootElement.GetProperty("runs")[0]
            .GetProperty("results").EnumerateArray(), item =>
                item.GetProperty("properties").GetProperty("objectPath").GetString() == "Sales[ProductId]");
        var uri = result.GetProperty("locations")[0].GetProperty("physicalLocation")
            .GetProperty("artifactLocation").GetProperty("uri").GetString();
        Assert.Equal("examples/valid-pbir-tmdl/Example.SemanticModel/definition/tables/Sales.tmdl", uri);
        Assert.Equal(0, exit);
    }

    [Fact]
    public void WrongSourceRootIsRejectedBeforeAudit()
    {
        using var external = new TemporaryDirectory();
        Assert.False(AuditSarifRenderer.IsValidSourceRoot(external.DirectoryPath,
            ExamplePath("valid-pbir-tmdl")));
    }

    [Fact]
    public void MissingProjectReturnsValidEmptySarifAndExitTwo()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pbiq-sarif-missing-" + Guid.NewGuid().ToString("N"));
        using var output = new TestConsole();
        using var errors = new TestConsole();
        using var writer = new StringWriter();
        var exit = AuditCommand.Execute(missing, output, errors, sarifOutput: writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        var run = doc.RootElement.GetProperty("runs")[0];
        Assert.Equal(2, exit);
        Assert.Empty(run.GetProperty("results").EnumerateArray());
        Assert.False(run.GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean());
        Assert.Equal("PROJECT_DISCOVERY_FAILED",
            run.GetProperty("properties").GetProperty("pbiqFailureCode").GetString());
        Assert.DoesNotContain(missing, writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }


    [Fact]
    public void SarifOutputIsDeterministicForSameFixture()
    {
        var first = Run("valid-pbir-tmdl");
        var second = Run("valid-pbir-tmdl");
        Assert.Equal(first.json, second.json);
    }

    private static (int exit, string json, string stdout, string stderr) Run(
        string fixture, bool failOnWarning = false, string? sourceRoot = null)
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        using var writer = new StringWriter();
        var exit = AuditCommand.Execute(ExamplePath(fixture), output, errors,
            failOnWarning: failOnWarning, sarifOutput: writer, sarifSourceRoot: sourceRoot);
        return (exit, writer.ToString(), output.Output, errors.Output);
    }

    private static string ExamplePath(string fixture) =>
        Path.Combine(SolutionRoot(), "examples", fixture);

    private static string SolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "pbiq-sarif-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath);
    }
}
