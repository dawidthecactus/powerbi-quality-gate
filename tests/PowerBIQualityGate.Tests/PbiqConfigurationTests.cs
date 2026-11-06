using System.Text.Json;

using PowerBIQualityGate.Cli;
using PowerBIQualityGate.Cli.Commands;
using PowerBIQualityGate.Cli.Configuration;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

using Spectre.Console.Testing;

namespace PowerBIQualityGate.Tests;

public sealed class PbiqConfigurationTests
{
    [Fact]
    public void NoImplicitConfigIsBackwardCompatible()
    {
        var config = PbiqConfiguration.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pbip"));
        Assert.Empty(config.DisabledRules);
        Assert.Empty(config.Exclusions);
        Assert.Null(config.FailOnWarning);
        Assert.Null(config.MinimumMeasureCoverage);
    }

    [Fact]
    public void ValidConfigParsesRulesPolicyAndExactExclusions()
    {
        using var config = TempConfig.Create("""
            version: 1
            rules:
              SM-004: false
            policy:
              failOnWarning: true
              minMeasureCoverage: 95
            exclusions:
              - rule: SM-003
                object: Sales[ProductId]
            """);
        var parsed = PbiqConfiguration.Load(ExamplePath("valid-pbir-tmdl"), config.Path);
        Assert.Contains("SM-004", parsed.DisabledRules);
        Assert.True(parsed.FailOnWarning);
        Assert.Equal(95, parsed.MinimumMeasureCoverage);
        var exclusion = Assert.Single(parsed.Exclusions);
        Assert.Equal("SM-003", exclusion.Rule);
        Assert.Equal("Sales[ProductId]", exclusion.Object);
    }

    [Theory]
    [InlineData("version: 2")]
    [InlineData("version: 1\nunknown: value")]
    [InlineData("version: 1\nrules:\n  SM-999: false")]
    [InlineData("version: 1\nrules:\n  SM-001: maybe")]
    [InlineData("version: 1\npolicy:\n  minMeasureCoverage: 101")]
    [InlineData("version: 1\nrules:\n  SM-002: false\npolicy:\n  minMeasureCoverage: 95")]
    [InlineData("version: 1\nrules:\n  SM-001: false\n  SM-002: false\n  SM-003: false\n  SM-004: false")]
    [InlineData("version: 1\nexclusions:\n  - rule: SM-003")]
    public void InvalidConfigFailsClosed(string yaml)
    {
        using var config = TempConfig.Create(yaml);
        Assert.Throws<PbiqConfigurationException>(() =>
            PbiqConfiguration.Load(ExamplePath("valid-pbir-tmdl"), config.Path));
    }

    [Fact]
    public void DuplicateExclusionIsRejected()
    {
        using var config = TempConfig.Create("""
            version: 1
            exclusions:
              - rule: SM-003
                object: Sales[ProductId]
              - rule: SM-003
                object: Sales[ProductId]
            """);
        Assert.Throws<PbiqConfigurationException>(() =>
            PbiqConfiguration.Load(ExamplePath("valid-pbir-tmdl"), config.Path));
    }

    [Fact]
    public void ExplicitMissingConfigIsAnError()
    {
        Assert.Throws<PbiqConfigurationException>(() =>
            PbiqConfiguration.Load(ExamplePath("valid-pbir-tmdl"),
                Path.Combine(Path.GetTempPath(), "pbiq-missing-" + Guid.NewGuid() + ".yml")));
    }

    [Fact]
    public void ParserAcceptsConfigOption()
    {
        var parsed = CliApplication.CreateRootCommand().Parse(
            ["audit", "examples/valid-pbir-tmdl", "--config", "settings.yml", "--format", "json"]);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void ConfigPolicyFailOnWarningBlocksExistingWarningFixture()
    {
        using var config = TempConfig.Create("version: 1\npolicy:\n  failOnWarning: true\n");
        var (exit, root, error) = Run("warning-pbir-tmdl", config.Path);
        Assert.Equal(1, exit);
        Assert.Equal("fail_warning_policy", root.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void DisablingRuleDoesNotPretendThatItWasEvaluated()
    {
        using var config = TempConfig.Create("version: 1\nrules:\n  SM-001: false\n");
        var (exit, root, error) = Run("warning-pbir-tmdl", config.Path);
        Assert.Equal(0, exit);
        Assert.DoesNotContain(root.GetProperty("rules").EnumerateArray().Select(item => item.GetString()),
            code => code == "SM-001");
        Assert.Equal(0, root.GetProperty("summary").GetProperty("warnings").GetInt32());
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void ExactExclusionRemovesFindingButRecordsSuppression()
    {
        var (_, original, _) = Run("warning-pbir-tmdl", null);
        var location = original.GetProperty("diagnostics").EnumerateArray()
            .First(item => item.GetProperty("ruleId").GetString() == "SM-001")
            .GetProperty("location").GetProperty("objectPath").GetString();
        using var config = TempConfig.Create($"version: 1\nexclusions:\n  - rule: SM-001\n    object: '{location!.Replace("'", "''", StringComparison.Ordinal)}'\n");
        var (exit, filtered, error) = Run("warning-pbir-tmdl", config.Path, failOnWarning: true);
        Assert.Equal(0, exit);
        Assert.Equal(0, filtered.GetProperty("summary").GetProperty("warnings").GetInt32());
        Assert.Equal(1, filtered.GetProperty("summary").GetProperty("suppressed").GetInt32());
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void InvalidConfigReturnsOneJsonFailureAndExitTwo()
    {
        using var config = TempConfig.Create("version: 4\n");
        var (exit, json, text) = Run("valid-pbir-tmdl", config.Path);
        Assert.Equal(2, exit);
        Assert.Equal("not_evaluated", json.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("INVALID_CONFIGURATION", json.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal(string.Empty, text);
    }

    private static (int Exit, JsonElement Json, string TextError) Run(
        string fixture, string? configPath, bool failOnWarning = false)
    {
        using var output = new TestConsole();
        using var error = new TestConsole();
        using var writer = new StringWriter();
        var exit = AuditCommand.Execute(ExamplePath(fixture), output, error,
            failOnWarning: failOnWarning, jsonOutput: writer, configPath: configPath);
        using var doc = JsonDocument.Parse(writer.ToString());
        return (exit, doc.RootElement.Clone(), error.Output);
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

    private sealed class TempConfig : IDisposable
    {
        public string Path { get; }

        private TempConfig(string path) => Path = path;

        public static TempConfig Create(string yaml)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "pbiq-config-test-" + Guid.NewGuid().ToString("N") + ".yml");
            File.WriteAllText(path, yaml);
            return new TempConfig(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
