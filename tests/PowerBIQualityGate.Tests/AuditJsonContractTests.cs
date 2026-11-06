using System.Text.Json;

using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;
using PowerBIQualityGate.Cli.Rendering;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class AuditJsonContractTests
{
    [Fact]
    public void AuditParserAcceptsJsonFormat()
    {
        var result = CliApplication.CreateRootCommand()
            .Parse(["audit", "examples/valid-pbir-tmdl", "--format", "json"]);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidFixtureProducesStableMachineEnvelope()
    {
        var (exit, json, text, errors) = Run("valid-pbir-tmdl");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, text);
        Assert.Equal(string.Empty, errors);
        Assert.False(
            json.Contains('\u001b'),
        "JSON output must not contain ANSI escape characters.");
        Assert.DoesNotContain("SUM(", json);
        Assert.Equal(AuditJsonRenderer.SchemaVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("pbiq", root.GetProperty("tool").GetProperty("name").GetString());
        Assert.StartsWith("0.3.0-alpha.1", root.GetProperty("tool").GetProperty("version").GetString()!);
        Assert.Equal("audit", root.GetProperty("command").GetString());
        Assert.Equal("Example.pbip", root.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal("pass_with_coverage_gaps", root.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("result").GetProperty("exitCode").GetInt32());
        Assert.Equal(4, root.GetProperty("summary").GetProperty("rulesEvaluated").GetInt32());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("information").GetInt32());
        Assert.Equal(4, root.GetProperty("rules").GetArrayLength());
        Assert.Equal(3, root.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failure").ValueKind);
    }

    [Fact]
    public void AuditJsonNeverInventsSourceLinesOrFilePaths()
    {
        var (_, json, _, _) = Run("valid-pbir-tmdl");
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("diagnostics")[0];
        var location = first.GetProperty("location");
        Assert.False(string.IsNullOrWhiteSpace(location.GetProperty("objectPath").GetString()));
        Assert.Equal(JsonValueKind.Null, location.GetProperty("filePath").ValueKind);
        Assert.Equal(JsonValueKind.Null, location.GetProperty("line").ValueKind);
        Assert.Equal("information", first.GetProperty("severity").GetString());
    }

    [Fact]
    public void MissingTypeClassificationIsExplicitCoverageNotConfirmedWarning()
    {
        var (_, json, _, _) = Run("valid-pbir-tmdl");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var measure = root.GetProperty("coverage").GetProperty("measures");
        Assert.Equal(2, measure.GetProperty("visible").GetInt32());
        Assert.Equal(0, measure.GetProperty("checkedNumeric").GetInt32());
        Assert.Equal(2, measure.GetProperty("candidates").GetInt32());
        Assert.Equal(2, measure.GetProperty("unknownType").GetInt32());
        Assert.Equal(2, measure.GetProperty("needsReview").GetInt32());
        Assert.Equal(0, measure.GetProperty("classificationPercent").GetDecimal());
        Assert.True(measure.GetProperty("hasGaps").GetBoolean());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("warnings").GetInt32());
    }

    [Fact]
    public void WarningPolicyKeepsExistingExitOneAndWritesJsonToStdout()
    {
        var (exit, json, text, errors) = Run("warning-pbir-tmdl", failOnWarning: true);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, text);
        Assert.Equal(string.Empty, errors);
        Assert.Equal("fail_warning_policy", doc.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("warnings").GetInt32());
        Assert.True(doc.RootElement.GetProperty("policy").GetProperty("failOnWarning").GetBoolean());
    }

    [Fact]
    public void CoverageGateKeepsExitOneWithoutChangingFindingSeverities()
    {
        var (exit, json, _, _) = Run("valid-pbir-tmdl", minimumMeasureCoverage: 95);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, exit);
        Assert.Equal("fail_coverage_policy", doc.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(95, doc.RootElement.GetProperty("policy").GetProperty("minimumMeasureCoverage").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("warnings").GetInt32());
    }

    [Fact]
    public void FailureIsOneJsonDocumentAndDoesNotExposeAbsoluteInputPath()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "pbiq-missing-" + Guid.NewGuid().ToString("N"), "Absent.pbip");
        using var output = new TestConsole();
        using var errors = new TestConsole();
        using var jsonOutput = new StringWriter();
        var exit = AuditCommand.Execute(absolutePath, output, errors, jsonOutput: jsonOutput);
        var json = jsonOutput.ToString();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2, exit);
        Assert.Equal("not_evaluated", doc.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("PROJECT_DISCOVERY_FAILED", doc.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("Absent.pbip", doc.RootElement.GetProperty("project").GetProperty("name").GetString());
        Assert.DoesNotContain(Path.GetDirectoryName(absolutePath)!, json);
        Assert.Equal(0, doc.RootElement.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(string.Empty, output.Output);
        Assert.Equal(string.Empty, errors.Output);
    }

    [Fact]
    public void JsonIsDeterministicForIdenticalPublicFixture()
    {
        var first = Run("valid-pbir-tmdl");
        var second = Run("valid-pbir-tmdl");
        Assert.Equal(first.json, second.json);
    }

    private static (int exit, string json, string text, string errors) Run(
        string fixture, bool failOnWarning = false, int? minimumMeasureCoverage = null)
    {
        using var output = new TestConsole();
        using var errors = new TestConsole();
        using var jsonOutput = new StringWriter();
        var exit = AuditCommand.Execute(ExamplePath(fixture), output, errors,
            failOnWarning: failOnWarning,
            minimumMeasureCoverage: minimumMeasureCoverage,
            jsonOutput: jsonOutput);
        return (exit, jsonOutput.ToString(), output.Output, errors.Output);
    }

    private static string ExamplePath(string fixture)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
            {
                return Path.Combine(directory.FullName, "examples", fixture);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
