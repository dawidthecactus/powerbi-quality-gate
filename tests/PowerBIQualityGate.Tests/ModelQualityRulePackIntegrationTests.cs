using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

namespace PowerBIQualityGate.Tests;

public sealed class ModelQualityRulePackIntegrationTests
{
    [Fact]
    public void IncludedTmdlExample_CanBeAuditedWithoutChangingItsFiles()
    {
        var definition = Path.Combine(FindExample(), "Example.SemanticModel", "definition");
        var before = File.ReadAllText(Path.Combine(definition, "tables", "Sales.tmdl"));
        var snapshot = new TomModelReader().Read(definition);

        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);

        Assert.Equal(4, audit.RulesEvaluated);
        Assert.Equal(snapshot.Name, audit.ModelName);
        Assert.Contains(audit.Findings, finding => finding.RuleCode == "SM-003" &&
            finding.Location == "Sales[ProductId]");
        Assert.Equal(before, File.ReadAllText(Path.Combine(definition, "tables", "Sales.tmdl")));
    }

    private static string FindExample()
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
