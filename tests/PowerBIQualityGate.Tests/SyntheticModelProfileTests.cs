using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

namespace PowerBIQualityGate.Tests;

/// <summary>
/// Public-domain synthetic object counts inspired by observed project sizes.
/// No real customer names, DAX, sources or model files are included.
/// </summary>
public sealed class SyntheticModelProfileTests
{
    [Theory]
    [InlineData("Aviation", 16, 96, 96, 8)]
    [InlineData("NC", 159, 1869, 120, 123)]
    [InlineData("Nielsen", 36, 172, 763, 5)]
    public void SyntheticProfileMatchesDeclaredSize(string profile, int tables,
        int columns, int measures, int relationships)
    {
        var model = Create(profile, tables, columns, measures, relationships);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(model, audit);
        var decision = new CoverageAwareGatePolicy().Evaluate(audit, coverage);

        Assert.Equal(tables, model.Tables.Count);
        Assert.Equal(columns, model.Columns);
        Assert.Equal(measures, model.Measures);
        Assert.Equal(relationships, model.Relationships.Count);
        Assert.Equal(4, audit.RulesEvaluated);
        Assert.Equal(measures, coverage.MeasureFormatting!.Total);
        Assert.Equal(0, decision.ExitCode);
    }

    [Fact]
    public void SyntheticNielsenReplicatesUnknownTypeGapWithoutBusinessData()
    {
        var model = Create("Nielsen", 36, 172, 763, 5);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(model, audit);

        Assert.Equal(763, coverage.MeasureFormatting!.UnknownType);
        Assert.Equal(0, coverage.MeasureFormatting.CheckedNumeric);
        Assert.Equal(1, new CoverageAwareGatePolicy(MinimumMeasureCoverage: 95)
            .Evaluate(audit, coverage).ExitCode);
    }

    [Fact]
    public void SyntheticNcReplicatesTenBidirectionalWarnings()
    {
        var model = Create("NC", 159, 1869, 120, 123);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);

        Assert.Equal(10, audit.WarningCount);
        Assert.All(audit.Findings.Where(f => f.RuleCode == "SM-001"),
            finding => Assert.Equal("SM-001", finding.RuleCode));
        Assert.Equal(QualityGateOutcome.FailedOnWarnings,
            new CoverageAwareGatePolicy(FailOnWarning: true)
                .Evaluate(audit, ModelAuditCoverageAnalyzer.Analyze(model, audit)).Outcome);
    }

    private static TomModelSnapshot Create(string profile, int tableCount,
        int columnCount, int measureCount, int relationshipCount)
    {
        var tables = Enumerable.Range(0, tableCount).Select(t =>
        {
            var columns = Enumerable.Range(0, columnCount / tableCount + (t < columnCount % tableCount ? 1 : 0))
                .Select(c => new TomColumnSnapshot($"Column{t}_{c}", "Int64", "Data", false, null))
                .ToArray();
            var measures = Enumerable.Range(0, measureCount / tableCount + (t < measureCount % tableCount ? 1 : 0))
                .Select(m => new TomMeasureSnapshot($"Measure{t}_{m}", "1", "#,0", false,
                    profile == "Nielsen" ? "Unknown" : "Double"))
                .ToArray();
            return new TomTableSnapshot($"Table{t}", false, columns, measures, 0, 1);
        }).ToArray();

        var relationships = Enumerable.Range(0, relationshipCount)
            .Select(i => new TomRelationshipSnapshot($"Relationship{i}",
                "Table0[Column0_0]", "Table1[Column1_0]", true,
                profile == "NC" && i < 10 ? "BothDirections" : "OneDirection"))
            .ToArray();
        return new TomModelSnapshot($"Synthetic-{profile}", 1600, tables, relationships);
    }
}
