using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

namespace PowerBIQualityGate.Tests;

public sealed class QualityGatePolicyTmdlIntegrationTests
{
    [Fact]
    public void IncludedWarningFixtureDeserializesAndTriggersSm001()
    {
        var definition = Path.Combine(ExamplePath("warning-pbir-tmdl"), "Example.SemanticModel", "definition");
        var original = File.ReadAllText(Path.Combine(definition, "relationships.tmdl"));
        var snapshot = new TomModelReader().Read(definition);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);

        Assert.Equal(4, audit.RulesEvaluated);
        Assert.Contains(audit.Findings, f => f.RuleCode == "SM-001");
        Assert.Equal(1, audit.WarningCount);
        Assert.Equal(0, audit.ErrorCount);
        Assert.Equal(0, new QualityGatePolicy().Evaluate(audit).ExitCode);
        Assert.Equal(1, new QualityGatePolicy(FailOnWarning: true).Evaluate(audit).ExitCode);
        Assert.Equal(original, File.ReadAllText(Path.Combine(definition, "relationships.tmdl")));
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
