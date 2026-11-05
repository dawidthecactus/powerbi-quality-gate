using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Quality.Rules;

namespace PowerBIQualityGate.Tests;

public sealed class ModelAuditCoverageTests
{
    [Fact]
    public void CoveragePartitionsEveryMeasureAndExposesActualRuleScopes()
    {
        var snapshot = MixedModel();
        var result = new ModelQualityAuditResult("Example", ["SM-001", "SM-002", "SM-003", "SM-004"], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, result);
        var measures = Assert.IsType<MeasureFormattingCoverage>(coverage.MeasureFormatting);

        Assert.Equal(9, measures.Total);
        Assert.Equal(7, measures.Visible);
        Assert.Equal(3, measures.CheckedNumeric);
        Assert.Equal(2, measures.Formatted);
        Assert.Equal(1, measures.FormattedStatic);
        Assert.Equal(1, measures.FormattedDynamic);
        Assert.Equal(1, measures.Unformatted);
        Assert.Equal(3, measures.UnknownType);
        Assert.Equal(1, measures.NonNumeric);
        Assert.Equal(2, measures.Hidden);
        Assert.Equal(measures.Total, measures.CheckedNumeric + measures.Skipped);

        Assert.Equal(new RuleObjectCoverage("SM-001", 1, 1, 2), coverage.Rules[0]);
        Assert.Equal(new RuleObjectCoverage("SM-002", 3, 6, 9), coverage.Rules[1]);
        Assert.Equal(new RuleObjectCoverage("SM-003", 1, 2, 3), coverage.Rules[2]);
        Assert.Equal(new RuleObjectCoverage("SM-004", 1, 1, 2), coverage.Rules[3]);
    }

    [Fact]
    public void Sm002FindingsUseSameEligibilityAsCoverage()
    {
        var snapshot = MixedModel();
        var coverage = ModelAuditCoverageAnalyzer.Analyze(
            snapshot, new ModelQualityAuditResult("Example", ["SM-002"], []));
        var issues = new UnformattedNumericMeasureRule().Evaluate(snapshot);

        Assert.Single(issues);
        Assert.Contains(issues, issue => issue.Location == "Fact[Missing]");
        Assert.Equal(issues.Count, coverage.MeasureFormatting!.Unformatted);
    }

    [Fact]
    public void LargeSyntheticModelOf763MeasuresKeepsUnknownsVisibleInCoverage()
    {
        var measures = Enumerable.Range(1, 760)
            .Select(i => new TomMeasureSnapshot($"Synthetic{i}", "1", null, false))
            .Concat(
            [
                new TomMeasureSnapshot("NoFormat", "1", null, false, "Double"),
                new TomMeasureSnapshot("Static", "1", "#,0", false, "Int64"),
                new TomMeasureSnapshot("Dynamic", "1", null, false, "Decimal", true)
            ]).ToArray();
        var snapshot = new TomModelSnapshot("Synthetic", 1600,
            [new TomTableSnapshot("Fact", false, [], measures, 0, 1)], []);
        var audit = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(snapshot);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot, audit);
        var formatting = Assert.IsType<MeasureFormattingCoverage>(coverage.MeasureFormatting);

        Assert.Equal(763, snapshot.Measures);
        Assert.Equal(763, formatting.Total);
        Assert.Equal(760, formatting.UnknownType);
        Assert.Equal(3, formatting.CheckedNumeric);
        Assert.Equal(2, formatting.Formatted);
        Assert.Equal(1, formatting.Unformatted);
        Assert.Equal(1, audit.WarningCount);
        Assert.Contains(audit.Findings, finding =>
            finding.RuleCode == "SM-002" && finding.Location == "Fact[NoFormat]");
    }

    [Fact]
    public void UnrecognizedTypeRemainsUnknownEvenWithAFormatString()
    {
        var snapshot = new TomModelSnapshot("Demo", 1600,
            [new TomTableSnapshot("Fact", false, [],
                [new TomMeasureSnapshot("Ambiguous", "1", "0.00", false, "CustomType")], 0, 1)], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(
            snapshot, new ModelQualityAuditResult("Demo", ["SM-002"], []));

        Assert.Equal(1, coverage.MeasureFormatting!.UnknownType);
        Assert.Equal(0, coverage.MeasureFormatting.CheckedNumeric);
        Assert.Empty(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Theory]
    [InlineData("INT64")]
    [InlineData("decimal")]
    [InlineData("Double")]
    public void KnownNumericTypesAreCaseInsensitive(string dataType)
    {
        var snapshot = new TomModelSnapshot("Demo", 1600,
            [new TomTableSnapshot("Fact", false, [],
                [new TomMeasureSnapshot("Value", "1", null, false, dataType)], 0, 1)], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(
            snapshot, new ModelQualityAuditResult("Demo", ["SM-002"], []));

        Assert.Equal(1, coverage.MeasureFormatting!.Unformatted);
        Assert.Single(new UnformattedNumericMeasureRule().Evaluate(snapshot));
    }

    [Fact]
    public void UnregisteredOrUnevaluatedRuleDoesNotClaimCoverage()
    {
        var snapshot = MixedModel();
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot,
            new ModelQualityAuditResult("Example", ["SM-003", "SM-099"], []));

        Assert.Single(coverage.Rules);
        Assert.Equal("SM-003", coverage.Rules[0].RuleCode);
        Assert.Null(coverage.MeasureFormatting);
    }

    [Fact]
    public void NoEvaluatedRulesMeansNoClaimedCoverage()
    {
        var coverage = ModelAuditCoverageAnalyzer.Analyze(MixedModel(),
            new ModelQualityAuditResult("Example", [], []));

        Assert.Empty(coverage.Rules);
        Assert.Null(coverage.MeasureFormatting);
    }

    [Fact]
    public void EmptyModelKeepsAllTotalsAtZero()
    {
        var snapshot = new TomModelSnapshot("Empty", 1600, [], []);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(snapshot,
            new ModelQualityAuditResult("Empty", ["SM-001", "SM-002", "SM-003", "SM-004"], []));

        Assert.All(coverage.Rules, scope =>
        {
            Assert.Equal(0, scope.Checked);
            Assert.Equal(0, scope.Skipped);
            Assert.Equal(0, scope.Total);
        });
        Assert.Equal(0, coverage.MeasureFormatting!.Visible);
    }

    [Fact]
    public void CoverageForSyntheticTmdlFixtureBalancesWithDeserializedTomObjects()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "PowerBIQualityGate.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Solution root not found.");
        }

        var definition = Path.Combine(directory.FullName, "examples", "warning-pbir-tmdl",
            "Example.SemanticModel", "definition");
        var model = new TomModelReader().Read(definition);
        var result = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault()).Run(model);
        var coverage = ModelAuditCoverageAnalyzer.Analyze(model, result);

        Assert.Equal(model.Measures, coverage.MeasureFormatting!.Total);
        Assert.Equal(model.Columns, coverage.Rules.Single(rule => rule.RuleCode == "SM-003").Total);
        Assert.Equal(model.Relationships.Count, coverage.Rules.Single(rule => rule.RuleCode == "SM-001").Total);
        Assert.Equal(coverage.MeasureFormatting.Total,
            coverage.MeasureFormatting.CheckedNumeric + coverage.MeasureFormatting.Skipped);
        Assert.Equal(4, coverage.Rules.Count);
    }

    private static TomModelSnapshot MixedModel() => new("Example", 1600,
    [
        new TomTableSnapshot("Fact", false,
        [
            new TomColumnSnapshot("Id", "Int64", "Data", false, null),
            new TomColumnSnapshot("Secret", "String", "Data", true, null)
        ],
        [
            new TomMeasureSnapshot("Missing", "1", null, false, "Int64"),
            new TomMeasureSnapshot("Formatted", "1", "0.00", false, "Decimal"),
            new TomMeasureSnapshot("Dynamic", "1", null, false, "Double", true),
            new TomMeasureSnapshot("Unknown", "1", null, false),
            new TomMeasureSnapshot("Variant", "1", null, false, "Variant"),
            new TomMeasureSnapshot("Text", "\"hi\"", null, false, "String"),
            new TomMeasureSnapshot("Hidden", "1", null, true, "Int64"),
            new TomMeasureSnapshot("Unsupported", "1", null, false, "Custom")
        ], 0, 1),
        new TomTableSnapshot("Secret", true,
            [new TomColumnSnapshot("Id", "Int64", "Data", false, null)],
            [new TomMeasureSnapshot("HiddenByTable", "1", null, false, "Double")], 0, 1)
    ],
    [
        new TomRelationshipSnapshot("active", "Fact[Id]", "Secret[Id]", true, "BothDirections"),
        new TomRelationshipSnapshot("inactive", "Fact[Id]", "Secret[Id]", false, "OneDirection")
    ]);
}
